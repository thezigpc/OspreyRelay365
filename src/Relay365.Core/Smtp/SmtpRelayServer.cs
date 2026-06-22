using System.Net;
using System.Net.Sockets;
using Relay365.Core.Logging;

namespace Relay365.Core.Smtp;

public class SmtpRelayServer
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private long _maxMessageBytes;

    private readonly MessageProcessor _processor;
    private readonly RelayLogger _logger;

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }

    public SmtpRelayServer(MessageProcessor processor, RelayLogger logger)
    {
        _processor = processor;
        _logger = logger;
    }

    public void Start(int port, string bindAddress, long maxMessageBytes = 26_214_400)
    {
        if (IsRunning) return;

        _maxMessageBytes = maxMessageBytes;
        _cts = new CancellationTokenSource();

        var ip = bindAddress is "0.0.0.0" or "*" or ""
            ? IPAddress.Any
            : IPAddress.Parse(bindAddress);

        _listener = new TcpListener(ip, port);
        _listener.Start();
        Port = port;
        IsRunning = true;

        _logger.Info($"SMTP relay listening on {bindAddress}:{port} " +
                     $"(max message {maxMessageBytes / 1024 / 1024} MB)");
        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _cts?.Cancel();
        _listener?.Stop();
        IsRunning = false;
        _logger.Info("SMTP relay stopped.");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.Error($"Accept error: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.ToString() ?? "unknown";
        _logger.Info($"Connection from {remote}");

        using (client)
        {
            try
            {
                var session = new SmtpSession(client, _processor, _logger, _maxMessageBytes);
                await session.RunAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.Error($"Session error ({remote}): {ex.Message}");
            }
        }
    }
}
