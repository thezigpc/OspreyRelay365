using Microsoft.Extensions.Hosting;
using Relay365.Core.Config;
using Relay365.Core.Graph;
using Relay365.Core.Logging;
using Relay365.Core.Routing;
using Relay365.Core.Smtp;

namespace Relay365.Service;

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
