using Microsoft.Extensions.Hosting;
using OspreyRelay.Core.Config;
using OspreyRelay.M365.Graph;
using OspreyRelay.Core.Logging;
using OspreyRelay.Core.Routing;
using OspreyRelay.Core.Smtp;

namespace OspreyRelay.App.Service;

public class RelayHostedService : IHostedService
{
    private readonly ConfigManager _configManager;
    private readonly SmtpRelayServer _server;
    private readonly MessageProcessor _processor;

    public RelayHostedService(ConfigManager configManager, RelayLogger logger)
    {
        _configManager = configManager;

        var mailSender  = new GraphMailSender(configManager, logger);
        var fileStorer  = new GraphFileStorer(configManager, mailSender, logger);
        var routing     = new RoutingEngine(configManager);
        _processor      = new MessageProcessor(routing, mailSender, fileStorer, configManager, logger);
        _server         = new SmtpRelayServer(_processor, logger);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _configManager.Load();
        _processor.RefreshCredentials();

        var cfg = _configManager.Config;
        if (!cfg.IsConfigured)
            throw new InvalidOperationException(
                "365Relay is not configured. Run the desktop app to complete setup first.");

        _server.Start(cfg.RelayPort, cfg.BindAddress, (long)cfg.MaxMessageSizeMb * 1024 * 1024);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _server.Stop();
        return Task.CompletedTask;
    }
}
