using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using OspreyRelay.Core.Config;
using OspreyRelay.Core.Logging;

namespace OspreyRelay.Core.Smtp;

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
    // deliveryToAddress: overrides the RCPT TO address(es); null = use email.EnvelopeTo as-is.
    // rewriteToHeader: also rewrites the embedded To: header in the MIME message. Optional because
    //   this may invalidate DKIM signatures on the original message (use with care).
    public async Task SendAsync(
        ReceivedEmail email, SmarthostConfig config,
        CancellationToken ct = default,
        string? deliveryToAddress = null, bool rewriteToHeader = false)
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

        // Optionally rewrite the embedded To: header (caller opts in; may break DKIM).
        if (rewriteToHeader && !string.IsNullOrWhiteSpace(deliveryToAddress))
        {
            mime.To.Clear();
            mime.To.Add(new MailboxAddress(deliveryToAddress, deliveryToAddress));
            _logger.Debug($"Rewrote To: header → {deliveryToAddress}");
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

        // RCPT TO: use delivery override address if set, otherwise original envelope recipients.
        var sender = new MailboxAddress("", envelopeFrom);
        IEnumerable<string> rcptTo = !string.IsNullOrWhiteSpace(deliveryToAddress)
            ? new[] { deliveryToAddress }
            : email.EnvelopeTo;
        var recipients = rcptTo.Select(t => new MailboxAddress("", t));
        await smtp.SendAsync(mime, sender, recipients, ct);
        await smtp.DisconnectAsync(true, ct);

        var effectiveTo = !string.IsNullOrWhiteSpace(deliveryToAddress)
            ? deliveryToAddress : string.Join(",", email.EnvelopeTo);
        _logger.Debug($"Smarthost delivered: {envelopeFrom} → {effectiveTo} via {config.Host}:{config.Port}");
    }
}
