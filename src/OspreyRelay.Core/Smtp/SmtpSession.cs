using System.Net.Sockets;
using System.Text;
using OspreyRelay.Core.Logging;

namespace OspreyRelay.Core.Smtp;

public class SmtpSession
{
    private static readonly Encoding Latin1 = Encoding.GetEncoding("iso-8859-1");

    private readonly TcpClient _client;
    private readonly MessageProcessor _processor;
    private readonly RelayLogger _logger;
    private readonly long _maxMessageBytes;

    private string _envelopeFrom = "";
    private List<string> _envelopeTo = new();

    public SmtpSession(TcpClient client, MessageProcessor processor, RelayLogger logger,
        long maxMessageBytes = 26_214_400 /* 25 MB */)
    {
        _client = client;
        _processor = processor;
        _logger = logger;
        _maxMessageBytes = maxMessageBytes;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _client.ReceiveTimeout = 300_000;
        _client.SendTimeout = 30_000;

        await using var stream = _client.GetStream();
        using var reader = new StreamReader(stream, Latin1,
            detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Latin1,
            bufferSize: 4096, leaveOpen: true)
        { NewLine = "\r\n", AutoFlush = true };

        await writer.WriteLineAsync("220 relay.local ESMTP OspreyRelay365");

        while (!ct.IsCancellationRequested)
        {
            string? line;
            try { line = await reader.ReadLineAsync(ct); }
            catch { break; }
            if (line == null) break;

            var spaceIdx = line.IndexOf(' ');
            var verb = (spaceIdx < 0 ? line : line[..spaceIdx]).ToUpperInvariant().Trim();
            var arg  = spaceIdx < 0 ? "" : line[(spaceIdx + 1)..].Trim();

            switch (verb)
            {
                case "EHLO":
                case "HELO":
                    _envelopeFrom = "";
                    _envelopeTo.Clear();
                    await writer.WriteLineAsync($"250-relay.local Hello {arg}");
                    await writer.WriteLineAsync($"250-SIZE {_maxMessageBytes}");
                    await writer.WriteLineAsync("250-8BITMIME");
                    await writer.WriteLineAsync("250 PIPELINING");
                    break;

                case "MAIL":
                    _envelopeFrom = ExtractAddress(arg);
                    _envelopeTo.Clear();
                    await writer.WriteLineAsync("250 OK");
                    break;

                case "RCPT":
                    _envelopeTo.Add(ExtractAddress(arg));
                    await writer.WriteLineAsync("250 OK");
                    break;

                case "DATA":
                    if (_envelopeTo.Count == 0)
                    {
                        await writer.WriteLineAsync("503 Need RCPT before DATA");
                        break;
                    }
                    await writer.WriteLineAsync("354 End data with <CRLF>.<CRLF>");
                    var (raw, tooLarge, receivedBytes) = await ReadDataAsync(reader, ct);
                    await ProcessEmailAsync(raw, tooLarge, receivedBytes, writer, ct);
                    break;

                case "RSET":
                    _envelopeFrom = "";
                    _envelopeTo.Clear();
                    await writer.WriteLineAsync("250 OK");
                    break;

                case "NOOP":
                    await writer.WriteLineAsync("250 OK");
                    break;

                case "QUIT":
                    await writer.WriteLineAsync("221 Bye");
                    return;

                default:
                    await writer.WriteLineAsync($"500 Command '{verb}' not recognized");
                    break;
            }
        }
    }

    private async Task<(byte[] data, bool tooLarge, long bytesReceived)>
        ReadDataAsync(StreamReader reader, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        bool tooLarge = false;
        long totalReceived = 0;

        while (true)
        {
            var line = await reader.ReadLineAsync(ct)
                ?? throw new EndOfStreamException("Client closed during DATA");

            if (line == ".") break;
            if (line.StartsWith("..")) line = line[1..];

            var bytes = Latin1.GetBytes(line + "\r\n");
            totalReceived += bytes.Length;

            if (!tooLarge)
            {
                await ms.WriteAsync(bytes, ct);
                if (ms.Length > _maxMessageBytes)
                {
                    tooLarge = true;
                    ms.SetLength(0);
                }
            }
        }

        return (tooLarge ? Array.Empty<byte>() : ms.ToArray(), tooLarge, totalReceived);
    }

    private async Task ProcessEmailAsync(
        byte[] rawData, bool tooLarge, long receivedBytes,
        StreamWriter writer, CancellationToken ct)
    {
        if (tooLarge)
        {
            var sizeMb  = receivedBytes / 1024.0 / 1024.0;
            var limitMb = _maxMessageBytes / 1024.0 / 1024.0;
            _logger.Warning(
                $"Rejected oversized message: {sizeMb:F1} MB (limit {limitMb:F0} MB) " +
                $"from {_envelopeFrom}");
            await writer.WriteLineAsync(
                $"552 5.3.4 Message too large ({sizeMb:F1} MB). " +
                $"Relay limit is {limitMb:F0} MB.");
            _envelopeFrom = "";
            _envelopeTo.Clear();
            return;
        }

        try
        {
            var email = new ReceivedEmail
            {
                EnvelopeFrom = _envelopeFrom,
                EnvelopeTo   = new List<string>(_envelopeTo),
                RawData      = rawData
            };

            var result = await _processor.ProcessAsync(email, ct);

            await writer.WriteLineAsync(
                $"{result.SmtpCode} {Sanitize(result.SmtpMessage)}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Session error: {ex.Message}");
            await writer.WriteLineAsync($"421 Service temporarily unavailable: {Sanitize(ex.Message)}");
        }
        finally
        {
            _envelopeFrom = "";
            _envelopeTo.Clear();
        }
    }

    private static string ExtractAddress(string arg)
    {
        var lt = arg.IndexOf('<');
        var gt = arg.IndexOf('>');
        if (lt >= 0 && gt > lt) return arg[(lt + 1)..gt];
        var colon = arg.IndexOf(':');
        return (colon >= 0 ? arg[(colon + 1)..] : arg).Trim();
    }

    private static string Sanitize(string msg) =>
        new string(msg.Where(c => c >= 32 && c < 127).ToArray());
}
