using System.Text.Json;
using Relay365.Core.Config;
using Relay365.Core.Graph;
using Relay365.Core.Logging;
using Relay365.Core.Routing;

namespace Relay365.Core.Smtp;

/// <summary>
/// Dispatches a received email based on the RoutingEngine decision.
/// Handles unrouted fallback (local folder, OneDrive redirect, email-as-attachment).
/// SmtpSession calls ProcessAsync() and translates the result to SMTP reply codes.
/// </summary>
public class MessageProcessor
{
    private readonly RoutingEngine _routing;
    private readonly GraphMailSender _mailSender;
    private readonly GraphFileStorer _fileStorer;
    private readonly ConfigManager _configManager;
    private readonly RelayLogger _logger;

    public MessageProcessor(
        RoutingEngine routing,
        GraphMailSender mailSender,
        GraphFileStorer fileStorer,
        ConfigManager configManager,
        RelayLogger logger)
    {
        _routing      = routing;
        _mailSender   = mailSender;
        _fileStorer   = fileStorer;
        _configManager = configManager;
        _logger       = logger;
    }

    public void RefreshCredentials() => _mailSender.RefreshClient();

    // ── Result ────────────────────────────────────────────────────────────────

    public record ProcessResult(bool Success, string SmtpCode, string SmtpMessage)
    {
        public static ProcessResult Ok(string msg = "Message accepted for delivery") =>
            new(true, "250", msg);

        public static ProcessResult TempFail(string msg) =>
            new(false, "421", msg);

        public static ProcessResult PermFail(string msg) =>
            new(false, "550", msg);
    }

    // ── Dispatch ──────────────────────────────────────────────────────────────

    public async Task<ProcessResult> ProcessAsync(ReceivedEmail email, CancellationToken ct)
    {
        RouteDecision decision;
        try
        {
            decision = _routing.Resolve(email);
            _logger.Info($"Route: [{decision.MatchSource}] from={email.EnvelopeFrom} " +
                         $"to={string.Join(",", email.EnvelopeTo)}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Routing error: {ex.Message}");
            await HandleUnroutedAsync(email, $"Routing error: {ex.Message}", ct);
            return ProcessResult.Ok("Message accepted (routing error — saved to unrouted)");
        }

        if (decision.IsReject)
        {
            _logger.Warning(
                $"Rejected: no route for {string.Join(",", email.EnvelopeTo)}");
            return ProcessResult.PermFail(
                "No route configured for this recipient. Message not accepted.");
        }

        try
        {
            if (decision.IsUnrouted)
            {
                await HandleUnroutedAsync(email, "No matching rule", ct);
                return ProcessResult.Ok("Message accepted (unrouted — saved for recovery)");
            }

            switch (decision.Type)
            {
                case FileDestinationType.EmailRelay:
                    var smarthostCfg = _configManager.Config;
                    try
                    {
                        await _mailSender.SendAsync(email, decision, ct);
                    }
                    catch (TransientGraphException ex) when (smarthostCfg.SmarthostEnabled
                        && !string.IsNullOrWhiteSpace(smarthostCfg.SmarthostHost))
                    {
                        _logger.Warning($"Graph unavailable: {ex.Message} — attempting smarthost failover ({smarthostCfg.SmarthostHost})");
                        await new SmtpSmarthostSender(_configManager, _logger).SendAsync(email, ct);
                        _logger.Success($"Smarthost failover succeeded for {string.Join(",", email.EnvelopeTo)}");
                    }
                    break;

                case FileDestinationType.OneDrive:
                case FileDestinationType.SharePoint:
                    await _fileStorer.StoreAsync(email, decision, ct);
                    break;
            }

            return ProcessResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error($"Processing failed ({decision.MatchSource}): {ex.Message}");
            await HandleUnroutedAsync(email, ex.Message, ct);
            return ProcessResult.Ok("Message accepted (processing failed — saved to unrouted)");
        }
    }

    // ── Unrouted handling ─────────────────────────────────────────────────────

    private async Task HandleUnroutedAsync(
        ReceivedEmail email, string reason, CancellationToken ct)
    {
        var cfg = _configManager.Config;

        // Always save locally first — safety net
        var localPath = SaveLocalUnrouted(email, reason);

        // Optional additional action
        if (cfg.UnroutedAction == UnroutedAction.OneDriveRedirect
            && !string.IsNullOrWhiteSpace(cfg.UnroutedOneDriveUser))
        {
            try
            {
                var decision = new RouteDecision
                {
                    Type                 = FileDestinationType.OneDrive,
                    OneDriveUser         = cfg.UnroutedOneDriveUser,
                    FolderPath           = cfg.UnroutedOneDrivePath,
                    SaveWhat             = SaveWhat.FullEml,
                    NoAttachmentBehavior = NoAttachmentBehavior.SaveAsEml,
                    SubjectDelimiter     = cfg.DefaultSubjectDelimiter,
                    MatchSource          = "UnroutedRedirect:OneDrive"
                };
                await _fileStorer.StoreAsync(email, decision, ct);
                _logger.Info($"Unrouted email also uploaded to OneDrive: {cfg.UnroutedOneDriveUser}");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Unrouted OneDrive redirect failed (local copy kept): {ex.Message}");
            }
        }
        else if (cfg.UnroutedAction == UnroutedAction.SharePointRedirect
            && !string.IsNullOrWhiteSpace(cfg.UnroutedSharePointDriveId))
        {
            try
            {
                var decision = new RouteDecision
                {
                    Type                 = FileDestinationType.SharePoint,
                    DriveId              = cfg.UnroutedSharePointDriveId,
                    FolderPath           = cfg.UnroutedSharePointFolderPath,
                    SaveWhat             = SaveWhat.FullEml,
                    NoAttachmentBehavior = NoAttachmentBehavior.SaveAsEml,
                    SubjectDelimiter     = cfg.DefaultSubjectDelimiter,
                    MatchSource          = "UnroutedRedirect:SharePoint"
                };
                await _fileStorer.StoreAsync(email, decision, ct);
                _logger.Info($"Unrouted email also uploaded to SharePoint: {cfg.UnroutedSharePointSiteUrl}");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Unrouted SharePoint redirect failed (local copy kept): {ex.Message}");
            }
        }
        else if (cfg.UnroutedAction == UnroutedAction.EmailAsAttachment
            && !string.IsNullOrWhiteSpace(cfg.UnroutedAlertEmail))
        {
            try
            {
                await SendUnroutedEmailAsync(email, cfg.UnroutedAlertEmail, reason, ct);
                _logger.Info($"Unrouted email forwarded as attachment to {cfg.UnroutedAlertEmail}");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Unrouted email forward failed (local copy kept): {ex.Message}");
            }
        }

        // Alert email (independent of action)
        if (!string.IsNullOrWhiteSpace(cfg.UnroutedAlertEmail)
            && cfg.UnroutedAction != UnroutedAction.EmailAsAttachment)
        {
            try { await SendAlertEmailAsync(email, cfg.UnroutedAlertEmail, reason, localPath, ct); }
            catch (Exception ex)
            {
                _logger.Warning($"Alert email failed: {ex.Message}");
            }
        }
    }

    private string SaveLocalUnrouted(ReceivedEmail email, string reason)
    {
        try
        {
            var dir = GetUnroutedDir(_configManager.Config.UnroutedLocalPath);
            Directory.CreateDirectory(dir);

            var stamp = email.ReceivedAt.ToString("yyyyMMdd_HHmmss_fff");
            var emlPath = Path.Combine(dir, $"{stamp}.eml");
            File.WriteAllBytes(emlPath, email.RawData);

            var meta = new
            {
                ReceivedAt = email.ReceivedAt,
                EnvelopeFrom = email.EnvelopeFrom,
                EnvelopeTo = email.EnvelopeTo,
                Reason = reason
            };
            File.WriteAllText(
                Path.Combine(dir, $"{stamp}.json"),
                JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

            _logger.Warning($"Unrouted email saved to: {emlPath} — reason: {reason}");

            PurgeExpiredUnrouted(dir);
            return emlPath;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to save unrouted email locally: {ex.Message}");
            return "";
        }
    }

    private void PurgeExpiredUnrouted(string dir)
    {
        var cfg = _configManager.Config;
        if (cfg.UnroutedLocalRetentionDays <= 0) return;

        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-cfg.UnroutedLocalRetentionDays);
            int deleted = 0;

            foreach (var eml in Directory.GetFiles(dir, "*.eml"))
            {
                if (File.GetCreationTimeUtc(eml) < cutoff)
                {
                    File.Delete(eml);
                    var json = Path.ChangeExtension(eml, ".json");
                    if (File.Exists(json)) File.Delete(json);
                    deleted++;
                }
            }

            if (deleted > 0)
                _logger.Info($"Auto-purged {deleted} unrouted file(s) older than {cfg.UnroutedLocalRetentionDays} days.");
        }
        catch (Exception ex)
        {
            _logger.Warning($"Unrouted auto-purge failed: {ex.Message}");
        }
    }

    private async Task SendAlertEmailAsync(
        ReceivedEmail email, string alertAddress, string reason, string localPath,
        CancellationToken ct)
    {
        var cfg = _configManager.Config;
        if (string.IsNullOrWhiteSpace(cfg.FallbackSenderEmail)) return;

        // Build a plain-text alert via Graph sendMail JSON (small, no attachment)
        var token = await _mailSender.GetAccessTokenAsync(ct);
        var body = new
        {
            message = new
            {
                subject = $"365 Relay: Unrouted email from {email.EnvelopeFrom}",
                body = new
                {
                    contentType = "Text",
                    content = $"An email could not be routed.\n\n" +
                              $"From: {email.EnvelopeFrom}\n" +
                              $"To: {string.Join(", ", email.EnvelopeTo)}\n" +
                              $"Received: {email.ReceivedAt:u}\n" +
                              $"Reason: {reason}\n\n" +
                              $"Local copy saved to: {localPath}"
                },
                toRecipients = new[]
                {
                    new { emailAddress = new { address = alertAddress } }
                }
            }
        };

        var url = $"https://graph.microsoft.com/v1.0/users/" +
                  $"{Uri.EscapeDataString(cfg.FallbackSenderEmail)}/sendMail";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(
            JsonSerializer.Serialize(body),
            System.Text.Encoding.UTF8, "application/json");

        using var resp = await new HttpClient().SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private async Task SendUnroutedEmailAsync(
        ReceivedEmail email, string toAddress, string reason, CancellationToken ct)
    {
        // Forward the original .eml as an attachment to the configured address
        // Uses a minimal Graph sendMail with a base64-encoded attachment
        var cfg = _configManager.Config;
        if (string.IsNullOrWhiteSpace(cfg.FallbackSenderEmail)) return;

        var token = await _mailSender.GetAccessTokenAsync(ct);
        var attachmentData = Convert.ToBase64String(email.RawData);

        var body = new
        {
            message = new
            {
                subject = $"365 Relay: Unrouted email from {email.EnvelopeFrom}",
                body = new
                {
                    contentType = "Text",
                    content = $"Unrouted email attached.\nFrom: {email.EnvelopeFrom}\n" +
                              $"Reason: {reason}"
                },
                toRecipients = new[]
                {
                    new { emailAddress = new { address = toAddress } }
                },
                attachments = new[]
                {
                    new
                    {
                        odataType = "#microsoft.graph.fileAttachment",
                        name = $"unrouted_{email.ReceivedAt:yyyyMMdd_HHmmss}.eml",
                        contentType = "message/rfc822",
                        contentBytes = attachmentData
                    }
                }
            }
        };

        var url = $"https://graph.microsoft.com/v1.0/users/" +
                  $"{Uri.EscapeDataString(cfg.FallbackSenderEmail)}/sendMail";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(
            JsonSerializer.Serialize(body),
            System.Text.Encoding.UTF8, "application/json");

        using var resp = await new HttpClient().SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Returns the local unrouted folder path.
    /// Pass <paramref name="configuredPath"/> from <see cref="RelayConfig.UnroutedLocalPath"/>
    /// to respect the user's custom override; blank falls back to the default location.
    /// </summary>
    public static string GetUnroutedDir(string? configuredPath = null) =>
        !string.IsNullOrWhiteSpace(configuredPath)
            ? configuredPath
            : Path.Combine(ConfigManager.GetConfigDir(), "unrouted");
}
