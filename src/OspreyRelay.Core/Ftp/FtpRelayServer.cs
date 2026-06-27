using System.Net;
using System.Net.Sockets;
using OspreyRelay.Core.Config;
using OspreyRelay.Core.Logging;
using OspreyRelay.Core.Smtp;

namespace OspreyRelay.Core.Ftp;

public class FtpRelayServer
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    private readonly ConfigManager _config;
    private readonly IFileStorer _storer;
    private readonly RelayLogger _logger;

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }

    public FtpRelayServer(ConfigManager config, IFileStorer storer, RelayLogger logger)
    {
        _config = config;
        _storer = storer;
        _logger = logger;
    }

    public void Start(int port, string bindAddress)
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();

        var ip = bindAddress is "0.0.0.0" or "*" or ""
            ? IPAddress.Any
            : IPAddress.Parse(bindAddress);

        _listener = new TcpListener(ip, port);
        _listener.Start();
        Port      = port;
        IsRunning = true;

        _logger.Info($"FTP relay listening on {bindAddress}:{port} " +
                     $"(passive ports {_config.Config.FtpPassivePortMin}–{_config.Config.FtpPassivePortMax})");

        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _cts?.Cancel();
        _listener?.Stop();
        IsRunning = false;
        _logger.Info("FTP relay stopped.");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        var router = new FtpFileRouter(_config);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, router, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.Error($"FTP accept error: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, FtpFileRouter router, CancellationToken ct)
    {
        var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.ToString() ?? "unknown";
        _logger.Info($"FTP connection from {remote}");

        using (client)
        {
            try
            {
                var session = new FtpSession(client, _config, _storer, router, _logger);
                await session.RunAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.Error($"FTP session error ({remote}): {ex.Message}");
            }
        }
    }
}
