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
    private const int LargeMailThresholdBytes = 3_500_000; // ~3.5 MB — above this use draft+send path

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

        // When the routing rule specifies a delivery address override (strip suffix or explicit
        // override), rewrite the mime.To header. Graph sendMail uses the To: header from the MIME
        // to determine recipients, so this is required for correct delivery — not optional.
        if (!string.IsNullOrWhiteSpace(decision.DeliveryToAddress))
        {
            mime.To.Clear();
            mime.To.Add(new MailboxAddress(decision.DeliveryToAddress, decision.DeliveryToAddress));
            _logger.Debug($"Rewrote To: header → {decision.DeliveryToAddress}");
        }

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

        bool isLarge = sendBytes.Length > LargeMailThresholdBytes;
        if (isLarge)
            _logger.Info($"Large message ({sendBytes.Length / 1024:N0} KB > {LargeMailThresholdBytes / 1024:N0} KB): using Graph draft path");

        // ── Explicit relay-via mailbox ────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(decision.RelayVia))
        {
            _logger.Info($"Relay rule → {decision.RelayVia}");
            if (isLarge)
                await SendViaDraftAsync(mime, decision.RelayVia, cfg.SaveToSentItems, ct);
            else
                await SendMimeAsync(sendBytes, decision.RelayVia, cfg.SaveToSentItems, ct);
            _logger.Success($"Relayed (rule): {originalFrom} → {recips}");
            return;
        }

        // ── From: passthrough ─────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(originalFrom))
        {
            bool passthroughOk;
            if (isLarge)
            {
                var (okDraft, statusDraft, errDraft) =
                    await TrySendViaDraftAsync(mime, originalFrom, cfg.SaveToSentItems, ct);
                passthroughOk = okDraft;
                if (!okDraft)
                {
                    _logger.Debug($"Draft passthrough failed HTTP {statusDraft}: {errDraft}");
                    bool noMailbox = statusDraft == 404 || IsNoMailboxError(errDraft);
                    if (!noMailbox)
                        throw new InvalidOperationException(
                            $"Graph rejected draft from '{originalFrom}' (HTTP {statusDraft}): {errDraft}");
                    _logger.Info($"'{originalFrom}' has no tenant mailbox ({statusDraft}), switching to fallback sender");
                }
            }
            else
            {
                var (ok, status, graphError) =
                    await TrySendMimeAsync(sendBytes, originalFrom, cfg.SaveToSentItems, ct);
                passthroughOk = ok;
                if (!ok)
                {
                    _logger.Debug($"Passthrough send failed HTTP {status}: {graphError}");
                    bool noMailbox = status == 404
                        || (status == 400 && IsNoMailboxError(graphError));
                    if (!noMailbox)
                        throw new InvalidOperationException(
                            $"Graph rejected sendMail from '{originalFrom}' (HTTP {status}): {graphError}");
                    _logger.Info($"'{originalFrom}' has no tenant mailbox ({status}), switching to fallback sender");
                }
            }

            if (passthroughOk)
            {
                _logger.Success($"Relayed: {originalFrom} → {recips}");
                return;
            }
        }

        // ── Fallback sender ───────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(cfg.FallbackSenderEmail))
            throw new InvalidOperationException(
                "From: address is not a tenant mailbox and no fallback sender is configured.");

        if (isLarge)
        {
            // Reload from the normalized CRLF bytes so MimeContent streams are fresh
            var fallbackMime = MimeMessage.Load(new MemoryStream(sendBytes));
            fallbackMime.From.Clear();
            fallbackMime.From.Add(new MailboxAddress(cfg.FallbackSenderEmail, cfg.FallbackSenderEmail));
            if (!string.IsNullOrWhiteSpace(originalFrom))
            {
                fallbackMime.ReplyTo.Clear();
                fallbackMime.ReplyTo.Add(new MailboxAddress(originalFrom, originalFrom));
            }
            await SendViaDraftAsync(fallbackMime, cfg.FallbackSenderEmail, cfg.SaveToSentItems, ct);
        }
        else
        {
            var fallbackBytes = RebuildForFallback(sendBytes, originalFrom, cfg.FallbackSenderEmail);
            await SendMimeAsync(fallbackBytes, cfg.FallbackSenderEmail, cfg.SaveToSentItems, ct);
        }
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

    private async Task<(bool ok, int status, string error)> TrySendViaDraftAsync(
        MimeMessage mime, string sender, bool saveToSent, CancellationToken ct)
    {
        try
        {
            await SendViaDraftAsync(mime, sender, saveToSent, ct);
            return (true, 202, "");
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
        {
            return (false, (int)ex.StatusCode!, ex.Message);
        }
    }

    // ── Draft + send path (for messages > LargeMailThresholdBytes) ────────────

    private async Task SendViaDraftAsync(
        MimeMessage mime, string senderEmail, bool saveToSent, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);

        // Step 1: create draft with message metadata and body (no attachments yet)
        var draftJson  = BuildDraftJson(mime);
        var createUrl  = $"{GraphBase}/users/{Uri.EscapeDataString(senderEmail)}/messages";
        string draftId;
        using (var req = new HttpRequestMessage(HttpMethod.Post, createUrl))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(draftJson, Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Graph create draft failed ({(int)resp.StatusCode}): {TryParseGraphError(body) ?? body}",
                    null, resp.StatusCode);
            using var doc = JsonDocument.Parse(body);
            draftId = doc.RootElement.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("Graph returned draft with no id");
        }
        _logger.Debug($"Draft created: {draftId[..Math.Min(16, draftId.Length)]}…");

        // Step 2: add attachments one by one
        const int smallAttachmentMax = 3 * 1024 * 1024; // 3 MB — above this use upload session
        var attachments = mime.Attachments.OfType<MimePart>()
            .Where(p => p.Content != null).ToList();

        foreach (var part in attachments)
        {
            using var buf = new MemoryStream();
            part.Content!.DecodeTo(buf);
            var data     = buf.ToArray();
            var filename = part.FileName ?? "attachment.bin";
            var mimeType = part.ContentType.MimeType;

            if (data.Length <= smallAttachmentMax)
            {
                var payload = new Dictionary<string, object>
                {
                    ["@odata.type"]  = "#microsoft.graph.fileAttachment",
                    ["name"]         = filename,
                    ["contentType"]  = mimeType,
                    ["contentBytes"] = Convert.ToBase64String(data)
                };
                var attUrl = $"{GraphBase}/users/{Uri.EscapeDataString(senderEmail)}/messages/{draftId}/attachments";
                using var req = new HttpRequestMessage(HttpMethod.Post, attUrl);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                using var resp = await Http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var err = TryParseGraphError(await resp.Content.ReadAsStringAsync(ct));
                    throw new HttpRequestException(
                        $"Graph add attachment '{filename}' failed ({(int)resp.StatusCode}): {err}",
                        null, resp.StatusCode);
                }
                _logger.Debug($"Attachment added inline: {filename} ({data.Length / 1024.0:F1} KB)");
            }
            else
            {
                await AddLargeAttachmentViaDraftAsync(token, senderEmail, draftId, filename, mimeType, data, ct);
            }
        }

        // Step 3: send the draft
        var sendUrl = $"{GraphBase}/users/{Uri.EscapeDataString(senderEmail)}/messages/{draftId}/send";
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, sendUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            // Empty body required — Graph 400s without a content-type
            req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req, ct);

            if (resp.IsSuccessStatusCode) return;

            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt == 0)
            {
                var retryAfter = (int)(resp.Headers.RetryAfter?.Delta?.TotalSeconds ?? 30);
                _logger.Warning($"Graph throttled on draft send — retrying in {retryAfter}s");
                await Task.Delay(TimeSpan.FromSeconds(retryAfter), ct);
                continue;
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.Debug($"Graph draft send {(int)resp.StatusCode} full response: {body}");
            var msg = TryParseGraphError(body) ?? body;

            if (resp.StatusCode is System.Net.HttpStatusCode.ServiceUnavailable
                                or System.Net.HttpStatusCode.GatewayTimeout)
                throw new TransientGraphException(
                    $"Graph temporarily unavailable ({(int)resp.StatusCode}): {msg}");

            throw new HttpRequestException(
                $"Graph draft send failed ({(int)resp.StatusCode}): {msg}",
                null, resp.StatusCode);
        }
    }

    private async Task AddLargeAttachmentViaDraftAsync(
        string token, string senderEmail, string messageId,
        string filename, string mimeType, byte[] data, CancellationToken ct)
    {
        // Create upload session
        var sessionUrl = $"{GraphBase}/users/{Uri.EscapeDataString(senderEmail)}/messages/{messageId}/attachments/createUploadSession";
        var sessionPayload = new Dictionary<string, object>
        {
            ["AttachmentItem"] = new Dictionary<string, object>
            {
                ["attachmentType"] = "file",
                ["name"]           = filename,
                ["size"]           = data.Length
            }
        };
        string uploadUrl;
        using (var req = new HttpRequestMessage(HttpMethod.Post, sessionUrl))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(JsonSerializer.Serialize(sessionPayload), Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Graph createUploadSession for '{filename}' failed ({(int)resp.StatusCode}): {TryParseGraphError(body) ?? body}",
                    null, resp.StatusCode);
            using var doc = JsonDocument.Parse(body);
            uploadUrl = doc.RootElement.GetProperty("uploadUrl").GetString()
                ?? throw new InvalidOperationException("No uploadUrl in createUploadSession response");
        }

        // Upload in 5 MB chunks (same size as GraphFileStorer)
        const int chunkSize = 5 * 1024 * 1024;
        int offset = 0;
        while (offset < data.Length)
        {
            var end   = Math.Min(offset + chunkSize, data.Length) - 1;
            var chunk = data[offset..(end + 1)];

            using var req = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
            req.Content = new ByteArrayContent(chunk);
            req.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, end, data.Length);
            req.Content.Headers.ContentLength = chunk.Length;

            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode
                && resp.StatusCode != System.Net.HttpStatusCode.Created)
            {
                var err = TryParseGraphError(await resp.Content.ReadAsStringAsync(ct));
                throw new HttpRequestException(
                    $"Attachment upload chunk failed ({(int)resp.StatusCode}): {err}",
                    null, resp.StatusCode);
            }
            offset += chunk.Length;
        }
        _logger.Debug($"Large attachment uploaded via session: {filename} ({data.Length / 1024.0:F1} KB)");
    }

    private static string BuildDraftJson(MimeMessage mime)
    {
        static List<object> ToRecipList(IEnumerable<MailboxAddress> mailboxes) =>
            mailboxes.Select(m => (object)new Dictionary<string, object>
            {
                ["emailAddress"] = new Dictionary<string, string>
                {
                    ["address"] = m.Address,
                    ["name"]    = string.IsNullOrWhiteSpace(m.Name) ? m.Address : m.Name
                }
            }).ToList();

        bool isHtml  = !string.IsNullOrEmpty(mime.HtmlBody);
        var bodyText = isHtml ? mime.HtmlBody! : (mime.TextBody ?? "");

        var msg = new Dictionary<string, object>
        {
            ["subject"] = mime.Subject ?? "",
            ["body"] = new Dictionary<string, string>
            {
                ["contentType"] = isHtml ? "HTML" : "Text",
                ["content"]     = bodyText
            },
            ["toRecipients"] = ToRecipList(mime.To.Mailboxes)
        };

        var cc      = ToRecipList(mime.Cc.Mailboxes);
        var bcc     = ToRecipList(mime.Bcc.Mailboxes);
        var replyTo = ToRecipList(mime.ReplyTo.Mailboxes);

        if (cc.Count > 0)      msg["ccRecipients"]  = cc;
        if (bcc.Count > 0)     msg["bccRecipients"] = bcc;
        if (replyTo.Count > 0) msg["replyTo"]       = replyTo;

        var fromMailbox = mime.From.Mailboxes.FirstOrDefault();
        if (fromMailbox != null)
            msg["from"] = new Dictionary<string, object>
            {
                ["emailAddress"] = new Dictionary<string, string>
                {
                    ["address"] = fromMailbox.Address,
                    ["name"]    = string.IsNullOrWhiteSpace(fromMailbox.Name)
                                  ? fromMailbox.Address : fromMailbox.Name
                }
            };

        return JsonSerializer.Serialize(msg);
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
            || e.Contains("InvalidUser", StringComparison.OrdinalIgnoreCase)
            || e.Contains("Access is denied", StringComparison.OrdinalIgnoreCase);
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
