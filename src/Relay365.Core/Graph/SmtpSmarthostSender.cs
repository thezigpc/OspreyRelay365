using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Relay365.Core.Config;
using Relay365.Core.Logging;
using Relay365.Core.Smtp;

namespace Relay365.Core.Graph;

/// <summary>
/// Delivers email via a traditional SMTP smarthost.
/// Used in two modes:
///   1. Failover — called by MessageProcessor when Graph returns 503/504 on an EmailRelay route.
///   2. Intentional route — called directly for SmarthostRelay rules, with per-rule or global config.
/// </summary>
public class SmtpSmarthostSender
{
    private readonly ConfigManager _configManager;
    private readonly RelayLogger _logger;

    public SmtpSmarthostSender(ConfigManager configManager, RelayLogger logger)
    {
        _configManager = configManager;
        _logger = logger;
    }

    // ── Failover overload (global config) ─────────────────────────────────────
    // Builds SmarthostConfig from RelayConfig and delegates to the explicit overload.
    public Task SendAsync(ReceivedEmail email, CancellationToken ct = default)
    {
        var cfg = _configManager.Config;
        var config = new SmarthostConfig(
            Host: cfg.SmarthostHost,
            Port: cfg.SmarthostPort,
            Tls: cfg.SmarthostTls,
            Username: cfg.SmarthostUsername,
            Password: cfg.SmarthostPassword,
            UseOriginalFrom: cfg.SmarthostUseOriginalFrom,
            FallbackSenderEmail: cfg.FallbackSenderEmail);
        return SendAsync(email, config, ct);
    }

    // ── Explicit config overload (rule-based and failover both end here) ──────
    public async Task SendAsync(ReceivedEmail email, SmarthostConfig config, CancellationToken ct = default)
    {
        MimeMessage mime;
        using (var ms = new MemoryStream(email.RawData))
            mime = await MimeMessage.LoadAsync(ms, ct);

        // Optionally substitute From: header with the configured fallback sender.
        // The original envelope-from is preserved as Reply-To so replies still work.
        string envelopeFrom = email.EnvelopeFrom;
        if (!config.UseOriginalFrom && !string.IsNullOrWhiteSpace(config.FallbackSenderEmail))
        {
            if (!string.IsNullOrWhiteSpace(email.EnvelopeFrom))
            {
                mime.ReplyTo.Clear();
                mime.ReplyTo.Add(new MailboxAddress(email.EnvelopeFrom, email.EnvelopeFrom));
            }
            mime.From.Clear();
            mime.From.Add(new MailboxAddress(config.FallbackSenderEmail, config.FallbackSenderEmail));
            envelopeFrom = config.FallbackSenderEmail;
        }

        var socketOptions = config.Tls switch
        {
            SmarthostTls.SslTls   => SecureSocketOptions.SslOnConnect,
            SmarthostTls.StartTls => SecureSocketOptions.StartTls,
            _                     => SecureSocketOptions.None
        };

        using var smtp = new SmtpClient { Timeout = 30_000 };
        await smtp.ConnectAsync(config.Host, config.Port, socketOptions, ct);

        if (!string.IsNullOrWhiteSpace(config.Username))
            await smtp.AuthenticateAsync(config.Username, config.Password, ct);

        // Use explicit envelope addresses so SMTP MAIL FROM/RCPT TO match what was received.
        var sender     = new MailboxAddress("", envelopeFrom);
        var recipients = email.EnvelopeTo.Select(t => new MailboxAddress("", t));
        await smtp.SendAsync(mime, sender, recipients, ct);
        await smtp.DisconnectAsync(true, ct);

        _logger.Debug($"Smarthost delivered: {envelopeFrom} → {string.Join(",", email.EnvelopeTo)} via {config.Host}:{config.Port}");
    }
}
