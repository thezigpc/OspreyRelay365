using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MimeKit;
using Relay365.Core.Config;
using Relay365.Core.Logging;
using Relay365.Core.Routing;
using Relay365.Core.Smtp;

namespace Relay365.Core.Graph;

/// <summary>
/// Stores email attachments (and optionally the full .eml) into
/// OneDrive for Business or SharePoint document libraries via Microsoft Graph.
///
/// Upload strategy:
///   ≤ 4 MB  → simple PUT  (/content endpoint)
///   > 4 MB  → upload session  (handles up to 250 GB)
///
/// Conflict behaviour is passed as @microsoft.graph.conflictBehavior in the
/// createUploadSession request body (or as a query param on simple PUT).
/// </summary>
public class GraphFileStorer
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private const string GraphBase = "https://graph.microsoft.com/v1.0";
    private const int SimpleUploadMaxBytes = 4 * 1024 * 1024; // 4 MB
    private const int UploadChunkSize = 5 * 1024 * 1024;      // 5 MB chunks

    private readonly ConfigManager _configManager;
    private readonly GraphMailSender _tokenSource; // reuses the auth credential
    private readonly RelayLogger _logger;

    public GraphFileStorer(
        ConfigManager configManager, GraphMailSender tokenSource, RelayLogger logger)
    {
        _configManager = configManager;
        _tokenSource = tokenSource;
        _logger = logger;
    }

    // ── Public entry point ────────────────────────────────────────────────────

    public async Task StoreAsync(
        ReceivedEmail received, RouteDecision decision, CancellationToken ct = default)
    {
        var cfg = _configManager.Config;

        MimeMessage mime;
        using (var ms = new MemoryStream(received.RawData))
            mime = await MimeMessage.LoadAsync(ms, ct);

        var mimeFrom   = mime.From.Mailboxes.FirstOrDefault()?.Address ?? received.EnvelopeFrom;
        // Use the effective delivery address for %to% so strip/override is reflected in paths.
        var matchedTo  = !string.IsNullOrWhiteSpace(decision.DeliveryToAddress)
                         ? decision.DeliveryToAddress
                         : string.IsNullOrWhiteSpace(decision.MatchedToAddress)
                             ? (received.EnvelopeTo.FirstOrDefault() ?? "")
                             : decision.MatchedToAddress;
        var atFrom     = mimeFrom.IndexOf('@');
        var atTo       = matchedTo.IndexOf('@');
        var receivedAt = received.ReceivedAt;

        // Build variable context
        var varCtx = new PathVariableContext
        {
            From          = mimeFrom,
            FromUpn       = atFrom > 0 ? mimeFrom[..atFrom] : mimeFrom,
            FromDomain    = atFrom > 0 ? mimeFrom[(atFrom + 1)..] : "",
            To            = matchedTo,
            ToUpn         = atTo > 0 ? matchedTo[..atTo] : matchedTo,
            ToDomain      = atTo > 0 ? matchedTo[(atTo + 1)..] : "",
            ToBaseDomain  = decision.ToBaseDomain,
            Suffix        = decision.CapturedSuffix,
            Subject       = mime.Subject ?? "",
            Date          = receivedAt.ToString("yyyy-MM-dd"),
            DateTime      = receivedAt.ToString("yyyy-MM-dd_HHmmss"),
            RegexCaptures = decision.RegexCaptures
        };

        var subjectDelim  = string.IsNullOrEmpty(decision.SubjectDelimiter)
                            ? cfg.DefaultSubjectDelimiter : decision.SubjectDelimiter;
        var spaceReplStr  = decision.FilenameSpaceReplacement ?? cfg.FilenameSpaceReplacement;
        char? spaceRepl   = spaceReplStr.Length > 0 ? spaceReplStr[0] : (char?)null;

        // Resolve folder path (apply variables)
        var rawPath  = string.IsNullOrWhiteSpace(decision.FolderPath) ? "/EmailRelay" : decision.FolderPath;
        var basePath = PathVariableResolver.ResolvePath(rawPath, varCtx, subjectDelim);

        // Resolve the drive ID — log which UPN/drive is being targeted
        var (driveId, resolvedUpn) = await ResolveDriveIdWithUpnAsync(decision, ct);
        _logger.Info($"Drive resolved: UPN={resolvedUpn ?? "(SharePoint)"} driveId={driveId[..Math.Min(12, driveId.Length)]}…");

        // Optional per-email subfolder
        if (decision.UsePerEmailSubfolder)
        {
            var sub = SanitizeFilename(
                $"{receivedAt:yyyy-MM-dd_HHmmss} {mime.Subject}");
            basePath = $"{basePath}/{sub}";
        }

        // Optional From: sender subfolder
        if (decision.FromSenderHandling is FromSenderHandling.AsSubfolder
                                        or FromSenderHandling.Both
            && !string.IsNullOrWhiteSpace(mimeFrom))
        {
            basePath = $"{basePath}/{SanitizeFilename(mimeFrom)}";
        }

        if (cfg.CreateMissingFolders)
            await EnsureFolderPathAsync(driveId, basePath, ct);

        var conflictBehavior = cfg.FileConflictBehavior switch
        {
            FileConflictBehavior.Replace => "replace",
            FileConflictBehavior.Fail    => "fail",
            _                            => "rename"
        };

        var savedCount   = 0;
        var hasTemplate  = !string.IsNullOrWhiteSpace(decision.FilenameTemplate);

        // ── Save attachments ──────────────────────────────────────────────────
        // Use BodyParts with an explicit IsAttachment check so only parts with
        // Content-Disposition: attachment are included by default.
        // When SaveEmbeddedImages is on, also include inline image parts with filenames.
        var attachments = mime.BodyParts
            .OfType<MimePart>()
            .Where(p => p.Content != null)
            .Where(p => p.IsAttachment
                        || (decision.SaveEmbeddedImages
                            && !string.IsNullOrWhiteSpace(p.FileName)
                            && string.Equals(p.ContentType?.MediaType, "image",
                                StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (decision.SaveWhat != SaveWhat.FullEml)
        {
            foreach (var part in attachments)
            {
                using var buf = new MemoryStream();
                part.Content?.DecodeTo(buf);
                var data = buf.ToArray();

                var originalName = part.FileName ?? $"attachment_{savedCount + 1}.bin";
                var filename = hasTemplate
                    ? PathVariableResolver.ResolveFilename(
                        decision.FilenameTemplate, varCtx, originalName, subjectDelim, spaceRepl)
                    : SanitizeFilename(originalName);

                await UploadFileAsync(driveId, basePath, filename, data, conflictBehavior, ct);
                savedCount++;
                _logger.Info($"Stored attachment: {basePath}/{filename} ({data.Length / 1024.0:F1} KB)");
            }
        }

        // ── Save body as .txt if AttachmentsAndBody ───────────────────────────
        if (decision.SaveWhat == SaveWhat.AttachmentsAndBody && !string.IsNullOrWhiteSpace(mime.TextBody))
        {
            var bodyOriginal = $"body_{receivedAt:yyyyMMdd_HHmmss}.txt";
            var bodyFilename = hasTemplate
                ? PathVariableResolver.ResolveFilename(
                    decision.FilenameTemplate, varCtx, bodyOriginal, subjectDelim, spaceRepl)
                : SanitizeFilename(bodyOriginal);
            var bodyBytes = Encoding.UTF8.GetBytes(mime.TextBody);
            await UploadFileAsync(driveId, basePath, bodyFilename, bodyBytes, conflictBehavior, ct);
            savedCount++;
        }

        // ── No attachments fallback ───────────────────────────────────────────
        if (savedCount == 0 && attachments.Count == 0)
        {
            if (decision.NoAttachmentBehavior == NoAttachmentBehavior.Skip)
            {
                _logger.Warning($"No attachments found — skipped per rule (from {mimeFrom})");
                return;
            }
        }

        // ── Save full .eml ────────────────────────────────────────────────────
        if (decision.SaveWhat == SaveWhat.FullEml
            || (savedCount == 0 && decision.NoAttachmentBehavior == NoAttachmentBehavior.SaveAsEml))
        {
            var emlOriginal = $"{receivedAt:yyyyMMdd_HHmmss}_{mime.Subject}.eml";
            var emlFilename = hasTemplate
                ? PathVariableResolver.ResolveFilename(
                    decision.FilenameTemplate, varCtx, emlOriginal, subjectDelim, spaceRepl)
                : SanitizeFilename(emlOriginal);
            await UploadFileAsync(
                driveId, basePath, emlFilename, received.RawData, conflictBehavior, ct);
            savedCount++;
            _logger.Info($"Stored .eml: {emlFilename}");
        }

        // ── Metadata tag (SharePoint column values) ───────────────────────────
        if (decision.FromSenderHandling is FromSenderHandling.AsMetadata
                                        or FromSenderHandling.Both)
        {
            _logger.Info("Metadata tagging requested — ensure SenderEmail/Subject/ReceivedDate " +
                         "columns exist on the target library.");
        }

        _logger.Success(
            $"Stored {savedCount} item(s) from {mimeFrom} → drive:{driveId} {basePath}");
    }

    // ── Drive ID resolution ───────────────────────────────────────────────────

    public async Task<string> ResolveDriveIdAsync(RouteDecision decision, CancellationToken ct)
        => (await ResolveDriveIdWithUpnAsync(decision, ct)).DriveId;

    private async Task<(string DriveId, string? Upn)> ResolveDriveIdWithUpnAsync(
        RouteDecision decision, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(decision.DriveId))
            return (decision.DriveId, null); // SharePoint: drive pre-resolved, no UPN

        if (decision.Type == FileDestinationType.OneDrive
            && !string.IsNullOrWhiteSpace(decision.OneDriveUser))
        {
            var upn = decision.OneDriveUser;
            var id  = await GetOneDriveDriveIdAsync(upn, ct);
            return (id, upn);
        }

        throw new InvalidOperationException(
            "Cannot resolve drive ID: no DriveId or OneDriveUser in route decision. " +
            $"Rule destination type={decision.Type}, OneDriveUser='{decision.OneDriveUser}'.");
    }

    public async Task<string> GetOneDriveDriveIdAsync(string userUpn, CancellationToken ct)
    {
        var token = await _tokenSource.GetAccessTokenAsync(ct);
        var url   = $"{GraphBase}/users/{Uri.EscapeDataString(userUpn)}/drive";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetString()
               ?? throw new InvalidOperationException($"Drive ID missing in response for {userUpn}");
    }

    // ── SharePoint library enumeration (called from UI picker) ───────────────

    public async Task<string> ResolveSiteIdAsync(string siteUrl, CancellationToken ct)
    {
        var token = await _tokenSource.GetAccessTokenAsync(ct);
        var uri   = new Uri(siteUrl.TrimEnd('/'));
        var host  = uri.Host;
        var path  = uri.AbsolutePath.TrimEnd('/');

        var url = $"{GraphBase}/sites/{host}:{path}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetString()
               ?? throw new InvalidOperationException("Site ID missing in response");
    }

    public async Task<List<(string Name, string DriveId)>> GetLibrariesAsync(
        string siteId, CancellationToken ct)
    {
        var token = await _tokenSource.GetAccessTokenAsync(ct);
        var url   = $"{GraphBase}/sites/{siteId}/drives";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        var results = new List<(string, string)>();
        foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
        {
            var name = item.GetProperty("name").GetString() ?? "";
            var id   = item.GetProperty("id").GetString() ?? "";
            if (!string.IsNullOrWhiteSpace(id))
                results.Add((name, id));
        }
        return results;
    }

    // ── SharePoint site search (called from UI picker) ───────────────────────

    /// <summary>Searches SharePoint sites by keyword. Requires Sites.ReadWrite.All.</summary>
    public async Task<List<(string DisplayName, string Url)>> SearchSitesAsync(
        string query, CancellationToken ct)
    {
        var token = await _tokenSource.GetAccessTokenAsync(ct);
        // Blank query → enumerate all sites (no search param). Using search=* only surfaces
        // system/designer entries on some tenants and misses real /sites/ paths entirely.
        var url = string.IsNullOrWhiteSpace(query)
            ? $"{GraphBase}/sites?$top=500"
            : $"{GraphBase}/sites?search={Uri.EscapeDataString(query)}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        var results = new List<(string, string)>();
        foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
        {
            var displayName = item.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
            var webUrl      = item.TryGetProperty("webUrl",      out var wu) ? wu.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(webUrl)) continue;

            // Keep only real team/communication sites. Designer, contentstorage, portals,
            // personal OneDrive, and root sites are excluded by this allowlist.
            if (!Uri.TryCreate(webUrl, UriKind.Absolute, out var uri)) continue;
            if (uri.Host.Contains("-my.", StringComparison.OrdinalIgnoreCase)) continue;
            var path = uri.AbsolutePath;
            if (!path.StartsWith("/sites/", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith("/teams/", StringComparison.OrdinalIgnoreCase)) continue;

            results.Add((displayName.Length > 0 ? displayName : webUrl, webUrl));
        }
        return results;
    }

    // ── Folder operations ─────────────────────────────────────────────────────

    /// <summary>
    /// Walks each segment of folderPath and creates any missing folders.
    /// Idempotent: existing folders are silently skipped.
    /// </summary>
    public async Task EnsureFolderPathAsync(string driveId, string folderPath, CancellationToken ct)
    {
        var segments = folderPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var built = "";
        var token = await _tokenSource.GetAccessTokenAsync(ct);

        foreach (var seg in segments)
        {
            var parentPath = string.IsNullOrEmpty(built) ? "" : $":/{built}:";
            var url = $"{GraphBase}/drives/{driveId}/root{parentPath}/children";

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    name = seg,
                    folder = new { },
                    // @microsoft.graph.conflictBehavior is a JSON key with @ — use dict
                }),
                Encoding.UTF8, "application/json");

            // Manually inject the conflict behavior key
            var payload = $"{{\"name\":\"{EscapeJson(seg)}\",\"folder\":{{}},\"@microsoft.graph.conflictBehavior\":\"fail\"}}";
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req, ct);

            // 409 Conflict = folder already exists → fine
            if (resp.StatusCode == System.Net.HttpStatusCode.Conflict
                || resp.IsSuccessStatusCode)
            {
                built = string.IsNullOrEmpty(built) ? seg : $"{built}/{seg}";
                continue;
            }

            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Could not create folder '{seg}' in path '{folderPath}': " +
                $"HTTP {(int)resp.StatusCode} — {GraphMailSender.TryParseGraphError(err) ?? err}");
        }
    }

    /// <summary>Checks whether a folder path exists in the drive.</summary>
    public async Task<bool> FolderExistsAsync(string driveId, string folderPath, CancellationToken ct)
    {
        var token = await _tokenSource.GetAccessTokenAsync(ct);
        var path  = NormalizePath(folderPath).TrimStart('/');
        var url   = $"{GraphBase}/drives/{driveId}/root:/{Uri.EscapeDataString(path)}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await Http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode;
    }

    // ── File upload ───────────────────────────────────────────────────────────

    private async Task UploadFileAsync(
        string driveId, string folderPath, string filename,
        byte[] data, string conflictBehavior, CancellationToken ct)
    {
        var token  = await _tokenSource.GetAccessTokenAsync(ct);
        var path   = $"{NormalizePath(folderPath).TrimStart('/')}/{filename}";

        if (data.Length <= SimpleUploadMaxBytes)
        {
            await SimpleUploadAsync(driveId, path, data, conflictBehavior, token, ct);
        }
        else
        {
            await SessionUploadAsync(driveId, path, data, conflictBehavior, token, ct);
        }
    }

    private async Task SimpleUploadAsync(
        string driveId, string itemPath, byte[] data,
        string conflictBehavior, string token, CancellationToken ct)
    {
        var url = $"{GraphBase}/drives/{driveId}/root:/{Uri.EscapeDataString(itemPath)}:/content" +
                  $"?@microsoft.graph.conflictBehavior={conflictBehavior}";

        using var req = new HttpRequestMessage(HttpMethod.Put, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new ByteArrayContent(data);
        req.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/octet-stream");

        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Upload failed for '{itemPath}': HTTP {(int)resp.StatusCode} — " +
                $"{GraphMailSender.TryParseGraphError(err) ?? err}");
        }
    }

    private async Task SessionUploadAsync(
        string driveId, string itemPath, byte[] data,
        string conflictBehavior, string token, CancellationToken ct)
    {
        // Create upload session
        var sessionUrl = $"{GraphBase}/drives/{driveId}/root:/{Uri.EscapeDataString(itemPath)}:/createUploadSession";
        var sessionBody = $"{{\"item\":{{\"@microsoft.graph.conflictBehavior\":\"{conflictBehavior}\"}}}}";

        using var sessionReq = new HttpRequestMessage(HttpMethod.Post, sessionUrl);
        sessionReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        sessionReq.Content = new StringContent(sessionBody, Encoding.UTF8, "application/json");

        string uploadUrl;
        using (var sessionResp = await Http.SendAsync(sessionReq, ct))
        {
            sessionResp.EnsureSuccessStatusCode();
            var sessionJson = await sessionResp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(sessionJson);
            uploadUrl = doc.RootElement.GetProperty("uploadUrl").GetString()
                        ?? throw new InvalidOperationException("Upload session URL missing");
        }

        // Upload in chunks
        var totalBytes = data.Length;
        var offset = 0;

        while (offset < totalBytes)
        {
            var chunkSize = Math.Min(UploadChunkSize, totalBytes - offset);
            var chunk = new byte[chunkSize];
            Array.Copy(data, offset, chunk, 0, chunkSize);

            using var chunkReq = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
            chunkReq.Content = new ByteArrayContent(chunk);
            chunkReq.Content.Headers.ContentRange =
                new System.Net.Http.Headers.ContentRangeHeaderValue(
                    offset, offset + chunkSize - 1, totalBytes);

            using var chunkResp = await Http.SendAsync(chunkReq, ct);

            if (!chunkResp.IsSuccessStatusCode
                && chunkResp.StatusCode != System.Net.HttpStatusCode.Accepted)
            {
                var err = await chunkResp.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException(
                    $"Chunk upload failed at offset {offset}: " +
                    $"HTTP {(int)chunkResp.StatusCode} — {err}");
            }

            offset += chunkSize;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string NormalizePath(string path)
    {
        path = path.Trim();
        if (!path.StartsWith('/')) path = '/' + path;
        return path.TrimEnd('/');
    }

    private static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);

        var result = sb.ToString().Trim();
        if (result.Length > 200) result = result[..200];
        return string.IsNullOrWhiteSpace(result) ? "file" : result;
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
