using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using MimeKit;
using Relay365.Core.Config;
using Relay365.Core.Logging;
using Relay365.Core.Smtp;

namespace Relay365.Core.Graph;

/// <summary>
/// Sends a received email via Microsoft Graph MIME passthrough.
/// Routing decisions are made upstream by RoutingEngine; this class is pure transport.
/// </summary>
public class GraphMailSender
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private const string GraphBase = "https://graph.microsoft.com/v1.0";

    // MimeKit defaults to Unix LF (\n). Graph requires RFC-2822 CRLF (\r\n).
    private static readonly FormatOptions SmtpFormat = CreateSmtpFormat();
    private static FormatOptions CreateSmtpFormat()
    {
        var f = FormatOptions.Default.Clone();
        f.NewLineFormat = NewLineFormat.Dos;
        return f;
    }

    private readonly ConfigManager _configManager;
    private readonly RelayLogger _logger;
    private ClientSecretCredential? _credential;

    public GraphMailSender(ConfigManager configManager, RelayLogger logger)
    {
        _configManager = configManager;
        _logger = logger;
    }

    public void RefreshClient()
    {
        var cfg = _configManager.Config;
        _credential = new ClientSecretCredential(cfg.TenantId, cfg.ClientId, cfg.ClientSecret);
    }

    /// <summary>
    /// Sends the email per the routing decision.
    /// If decision.RelayVia is set, sends directly via that mailbox.
    /// Otherwise attempts From: passthrough, falling back to FallbackSenderEmail.
    /// </summary>
    public async Task SendAsync(
        ReceivedEmail received, RouteDecision decision, CancellationToken ct = default)
    {
        if (_credential == null) RefreshClient();

        var cfg = _configManager.Config;

        MimeMessage mime;
        using (var ms = new MemoryStream(received.RawData))
            mime = await MimeMessage.LoadAsync(ms, ct);

        var mimeFrom     = mime.From.Mailboxes.FirstOrDefault()?.Address ?? "";
        var originalFrom = string.IsNullOrWhiteSpace(mimeFrom) ? received.EnvelopeFrom : mimeFrom;
        var recips       = string.Join(", ", received.EnvelopeTo);

        // Debug: show MIME structure so issues are diagnosable
        if (_logger.DebugMode)
        {
            var parts = mime.BodyParts.OfType<MimePart>().ToList();
            _logger.Debug($"MIME body parts: {parts.Count}");
            foreach (var p in parts)
                _logger.Debug($"  part {p.ContentType.MimeType} CTE={p.ContentTransferEncoding}");
        }

        // Normalize binary/8bit/Default parts before re-serialization.
        // Graph rejects raw 8-bit bytes; we decode to raw bytes and re-encode as base64/QP.
        bool hasProblematicEncoding = mime.BodyParts.OfType<MimePart>().Any(p =>
            p.Content is not null &&
            p.ContentTransferEncoding is ContentEncoding.Binary
                                      or ContentEncoding.EightBit
                                      or ContentEncoding.Default);

        if (hasProblematicEncoding)
        {
            NormalizeTransferEncoding(mime);
            _logger.Debug("MIME transfer encoding normalized (binary/8bit parts present)");
        }

        // Always re-serialize through MimeKit with CRLF line endings (RFC 2822 / Graph requirement).
        // Raw SMTP DATA bytes or MimeKit's default Unix LF output both cause Graph to return
        // "Invalid base64 string for MIME content".
        byte[] sendBytes;
        using (var ms = new MemoryStream())
        {
            mime.WriteTo(SmtpFormat, ms);
            sendBytes = ms.ToArray();
        }

        if (_logger.DebugMode)
        {
            _logger.Debug($"MIME serialized: {sendBytes.Length} bytes (problematic={hasProblematicEncoding})");
            // Show the full MIME as text so we can see exactly what Graph receives
            var preview = Encoding.Latin1.GetString(sendBytes);
            _logger.Debug($"--- MIME START ---\n{preview}\n--- MIME END ---");
        }

        // ── Explicit relay-via mailbox ────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(decision.RelayVia))
        {
            _logger.Info($"Relay rule → {decision.RelayVia}");
            await SendMimeAsync(sendBytes, decision.RelayVia, cfg.SaveToSentItems, ct);
            _logger.Success($"Relayed (rule): {originalFrom} → {recips}");
            return;
        }

        // ── From: passthrough ─────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(originalFrom))
        {
            var (ok, status, graphError) = await TrySendMimeAsync(
                sendBytes, originalFrom, cfg.SaveToSentItems, ct);

            if (ok)
            {
                _logger.Success($"Relayed: {originalFrom} → {recips}");
                return;
            }

            _logger.Debug($"Passthrough send failed HTTP {status}: {graphError}");

            // Treat as "no mailbox" only when Graph says the user/mailbox isn't found.
            // Other errors (permissions, MIME issues) should surface immediately.
            bool noMailbox = status == 404
                || (status == 400 && IsNoMailboxError(graphError));

            if (!noMailbox)
                throw new InvalidOperationException(
                    $"Graph rejected sendMail from '{originalFrom}' (HTTP {status}): {graphError}");

            _logger.Info($"'{originalFrom}' has no tenant mailbox ({status}), switching to fallback sender");
        }

        // ── Fallback sender ───────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(cfg.FallbackSenderEmail))
            throw new InvalidOperationException(
                "From: address is not a tenant mailbox and no fallback sender is configured.");

        var fallbackBytes = RebuildForFallback(sendBytes, originalFrom, cfg.FallbackSenderEmail);
        await SendMimeAsync(fallbackBytes, cfg.FallbackSenderEmail, cfg.SaveToSentItems, ct);
        _logger.Success($"Relayed via {cfg.FallbackSenderEmail}: {originalFrom} → {recips}");
    }

    // ── Internal: raw MIME POST to Graph ─────────────────────────────────────

    internal async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_credential == null) RefreshClient();
        var tok = await _credential!.GetTokenAsync(
            new Azure.Core.TokenRequestContext(
                new[] { "https://graph.microsoft.com/.default" }), ct);
        return tok.Token;
    }

    private async Task SendMimeAsync(
        byte[] mimeBytes, string senderEmail, bool saveToSent, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);
        var save  = saveToSent ? "true" : "false";
        var url   = $"{GraphBase}/users/{Uri.EscapeDataString(senderEmail)}" +
                    $"/sendMail?saveToSentItems={save}";

        // Graph sendMail MIME endpoint requires the MIME to be base64-encoded in the request body.
        // Sending raw MIME bytes produces ErrorMimeContentInvalidBase64String (HTTP 400).
        var base64Body = Encoding.ASCII.GetBytes(Convert.ToBase64String(mimeBytes));
        _logger.Debug($"POST {url} ({mimeBytes.Length} raw → {base64Body.Length} base64 bytes)");

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new ByteArrayContent(base64Body);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var resp = await Http.SendAsync(req.Clone(), ct);
            if (resp.IsSuccessStatusCode) return;

            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt == 0)
            {
                var retryAfter = (int)(resp.Headers.RetryAfter?.Delta?.TotalSeconds ?? 30);
                _logger.Warning($"Graph throttled — retrying in {retryAfter}s");
                await Task.Delay(TimeSpan.FromSeconds(retryAfter), ct);
                continue;
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            // Always log the full Graph error JSON in debug mode — error.code is often more useful than error.message
            _logger.Debug($"Graph {(int)resp.StatusCode} full response: {body}");
            var msg  = TryParseGraphError(body) ?? body;

            // 503/504 = Microsoft 365 temporarily unavailable — callers can fall over to smarthost
            if (resp.StatusCode is System.Net.HttpStatusCode.ServiceUnavailable
                                or System.Net.HttpStatusCode.GatewayTimeout)
                throw new TransientGraphException(
                    $"Graph temporarily unavailable (HTTP {(int)resp.StatusCode}): {msg}");

            throw new HttpRequestException(
                $"Graph sendMail failed ({(int)resp.StatusCode}): {msg}",
                null, resp.StatusCode);
        }
    }

    private async Task<(bool ok, int status, string error)> TrySendMimeAsync(
        byte[] mimeBytes, string sender, bool saveToSent, CancellationToken ct)
    {
        try
        {
            await SendMimeAsync(mimeBytes, sender, saveToSent, ct);
            return (true, 202, "");
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
        {
            return (false, (int)ex.StatusCode!, ex.Message);
        }
    }

    /// <summary>True when the Graph error indicates the sender address has no mailbox in this tenant.</summary>
    private static bool IsNoMailboxError(string graphError)
    {
        if (string.IsNullOrEmpty(graphError)) return false;
        var e = graphError.AsSpan();
        return e.Contains("MailboxNotEnabledForRESTAPI", StringComparison.OrdinalIgnoreCase)
            || e.Contains("ResourceNotFound", StringComparison.OrdinalIgnoreCase)
            || e.Contains("does not exist in the tenant", StringComparison.OrdinalIgnoreCase)
            || e.Contains("not in the tenant", StringComparison.OrdinalIgnoreCase)
            || e.Contains("InvalidUser", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Re-encodes any body parts using binary/8bit transfer encoding to base64 or
    /// quoted-printable. Graph's MIME endpoint requires standard encodings.
    /// Decodes the existing content bytes first so the output is actually correct,
    /// not just a relabelled binary stream.
    /// </summary>
    private static void NormalizeTransferEncoding(MimeMessage mime)
    {
        foreach (var part in mime.BodyParts.OfType<MimePart>())
        {
            var enc = part.ContentTransferEncoding;
            if (enc is ContentEncoding.Binary or ContentEncoding.EightBit or ContentEncoding.Default
                && part.Content is not null)
            {
                // Decode whatever is currently in the content stream to raw bytes
                var buf = new MemoryStream();
                part.Content.DecodeTo(buf);
                buf.Position = 0;

                // Re-wrap with a Graph-safe encoding
                var newEnc = part.ContentType.IsMimeType("text", "*")
                    ? ContentEncoding.QuotedPrintable
                    : ContentEncoding.Base64;
                part.Content = new MimeContent(buf, newEnc);
            }
        }
    }

    // Takes already-serialized bytes so we don't re-read exhausted MimeContent streams.
    // Uses CRLF line endings (SmtpFormat) for Graph compatibility.
    private static byte[] RebuildForFallback(
        byte[] mimeBytes, string originalFrom, string fallbackSender)
    {
        MimeMessage clone;
        using (var tmp = new MemoryStream(mimeBytes))
            clone = MimeMessage.Load(tmp);

        if (!string.IsNullOrWhiteSpace(originalFrom))
        {
            clone.ReplyTo.Clear();
            clone.ReplyTo.Add(new MailboxAddress(originalFrom, originalFrom));
        }

        clone.From.Clear();
        clone.From.Add(new MailboxAddress(fallbackSender, fallbackSender));

        using var ms = new MemoryStream();
        clone.WriteTo(SmtpFormat, ms);
        return ms.ToArray();
    }

    internal static string? TryParseGraphError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("error").GetProperty("message").GetString();
        }
        catch { return null; }
    }
}

/// <summary>
/// Thrown when Microsoft 365 / Azure AD is temporarily unavailable (HTTP 503/504 or token
/// acquisition failure due to a transient network issue).
/// MessageProcessor catches this to attempt smarthost failover before saving to unrouted.
/// </summary>
public class TransientGraphException : Exception
{
    public TransientGraphException(string message) : base(message) { }
    public TransientGraphException(string message, Exception inner) : base(message, inner) { }
}

// HttpRequestMessage is not reusable after SendAsync — clone for the 429 retry
internal static class HttpRequestMessageExtensions
{
    public static HttpRequestMessage Clone(this HttpRequestMessage req)
    {
        var clone = new HttpRequestMessage(req.Method, req.RequestUri);
        foreach (var h in req.Headers) clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        if (req.Content != null)
        {
            var bytes = req.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            clone.Content = new ByteArrayContent(bytes);
            foreach (var h in req.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }
        return clone;
    }
}
