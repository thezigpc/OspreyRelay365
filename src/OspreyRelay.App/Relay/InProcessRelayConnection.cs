using OspreyRelay.Core.Config;
using OspreyRelay.Core.Ftp;
using OspreyRelay.M365.Graph;
using OspreyRelay.Core.Logging;
using OspreyRelay.Core.Routing;
using OspreyRelay.Core.Smtp;

namespace OspreyRelay.App.Relay;

/// <summary>
/// Runs the SMTP relay server (and optionally the FTP bridge) inside the GUI process.
/// Used when no Windows Service is installed.
/// </summary>
public sealed class InProcessRelayConnection : IRelayConnection
{
    private readonly ConfigManager _config;
    private readonly RelayLogger _logger;
    private SmtpRelayServer? _server;
    private FtpRelayServer?  _ftpServer;

    public bool IsRunning => _server?.IsRunning == true;

    // Log events are raised directly by the shared RelayLogger instance
    public event EventHandler<LogEntry> LogReceived
    {
        add    => _logger.LogReceived += value;
        remove => _logger.LogReceived -= value;
    }

    public InProcessRelayConnection(ConfigManager config, RelayLogger logger)
    {
        _config = config;
        _logger = logger;
    }

    // Nothing to wire up for in-process mode — the logger is already shared
    public void Open()  { }
    public void Close() { }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (_server?.IsRunning == true) return Task.CompletedTask;

        var cfg        = _config.Config;
        var mailSender = new GraphMailSender(_config, _logger);
        var fileStorer = new GraphFileStorer(_config, mailSender, _logger);
        var routing    = new RoutingEngine(_config);
        var processor  = new MessageProcessor(routing, mailSender, fileStorer, _config, _logger);

        _server = new SmtpRelayServer(processor, _logger);
        _server.Start(cfg.RelayPort, cfg.BindAddress, (long)cfg.MaxMessageSizeMb * 1024 * 1024);

        if (cfg.FtpEnabled)
        {
            _ftpServer = new FtpRelayServer(_config, fileStorer, _logger);
            _ftpServer.Start(cfg.FtpPort, cfg.FtpBindAddress);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _server?.Stop();
        _server = null;
        _ftpServer?.Stop();
        _ftpServer = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _server?.Stop();
        _ftpServer?.Stop();
    }
}
