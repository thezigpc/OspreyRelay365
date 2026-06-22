using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Relay365.Core.Config;
using Relay365.Core.Logging;
using Relay365.Core.Smtp;

namespace Relay365.Core.Graph;

/// <summary>
/// Delivers email via a traditional SMTP smarthost as a failover when Microsoft 365 is unreachable.
/// Used by MessageProcessor when a TransientGraphException is caught on an EmailRelay route.
/// Intended for corporate smarthosts (e.g. Barracuda) that queue and spool before final delivery.
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

    public async Task SendAsync(ReceivedEmail email, CancellationToken ct = default)
    {
        var cfg = _configManager.Config;

        MimeMessage mime;
        using (var ms = new MemoryStream(email.RawData))
            mime = await MimeMessage.LoadAsync(ms, ct);

        // Optionally substitute From: header with the configured fallback sender.
        // The original envelope-from is preserved as Reply-To so replies still work.
        string envelopeFrom = email.EnvelopeFrom;
        if (!cfg.SmarthostUseOriginalFrom && !string.IsNullOrWhiteSpace(cfg.FallbackSenderEmail))
        {
            if (!string.IsNullOrWhiteSpace(email.EnvelopeFrom))
            {
                mime.ReplyTo.Clear();
                mime.ReplyTo.Add(new MailboxAddress(email.EnvelopeFrom, email.EnvelopeFrom));
            }
            mime.From.Clear();
            mime.From.Add(new MailboxAddress(cfg.FallbackSenderEmail, cfg.FallbackSenderEmail));
            envelopeFrom = cfg.FallbackSenderEmail;
        }

        var socketOptions = cfg.SmarthostTls switch
        {
            SmarthostTls.SslTls   => SecureSocketOptions.SslOnConnect,
            SmarthostTls.StartTls => SecureSocketOptions.StartTls,
            _                     => SecureSocketOptions.None
        };

        using var smtp = new SmtpClient { Timeout = 30_000 };
        await smtp.ConnectAsync(cfg.SmarthostHost, cfg.SmarthostPort, socketOptions, ct);

        if (!string.IsNullOrWhiteSpace(cfg.SmarthostUsername))
            await smtp.AuthenticateAsync(cfg.SmarthostUsername, cfg.SmarthostPassword, ct);

        // Use explicit envelope addresses so the SMTP MAIL FROM/RCPT TO match what was received.
        var sender     = new MailboxAddress("", envelopeFrom);
        var recipients = email.EnvelopeTo.Select(t => new MailboxAddress("", t));
        await smtp.SendAsync(mime, sender, recipients, ct);

        await smtp.DisconnectAsync(true, ct);

        _logger.Debug($"Smarthost delivered: {envelopeFrom} → {string.Join(",", email.EnvelopeTo)} via {cfg.SmarthostHost}:{cfg.SmarthostPort}");
    }
}
