using System.Net;
using System.Net.Sockets;
using System.Text;
using OspreyRelay.Core.Config;
using OspreyRelay.Core.Logging;
using OspreyRelay.Core.Routing;
using OspreyRelay.Core.Smtp;

namespace OspreyRelay.Core.Ftp;

internal class FtpSession
{
    private readonly TcpClient _client;
    private readonly ConfigManager _config;
    private readonly IFileStorer _storer;
    private readonly FtpFileRouter _router;
    private readonly RelayLogger _logger;
    private readonly IPAddress _localIp;

    private enum State { Initial, WaitingForPassword, LoggedIn, Closed }

    private State   _state       = State.Initial;
    private string  _pendingUser = "";
    private string  _username    = "";
    private string  _cwd         = "/";

    private TcpListener? _passiveListener;

    public FtpSession(
        TcpClient client, ConfigManager config,
        IFileStorer storer, FtpFileRouter router, RelayLogger logger)
    {
        _client   = client;
        _config   = config;
        _storer   = storer;
        _router   = router;
        _logger   = logger;

        // The local IP of the accepted connection — used to advertise in PASV.
        // When the server is bound to 0.0.0.0, this reflects the actual NIC the client reached.
        var localEp = (IPEndPoint?)client.Client.LocalEndPoint;
        _localIp = localEp?.Address ?? IPAddress.Loopback;
        if (_localIp.IsIPv4MappedToIPv6)
            _localIp = _localIp.MapToIPv4();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var stream = _client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        using var writer = new StreamWriter(stream, Encoding.ASCII, bufferSize: 1024, leaveOpen: true);
        writer.NewLine   = "\r\n";
        writer.AutoFlush = true;

        await writer.WriteLineAsync("220 Osprey Relay FTP Service ready.");

        while (!ct.IsCancellationRequested && _state != State.Closed)
        {
            string? line;
            try { line = await reader.ReadLineAsync(ct); }
            catch (OperationCanceledException) { break; }

            if (line == null) break;

            var idx = line.IndexOf(' ');
            var cmd = (idx < 0 ? line : line[..idx]).ToUpperInvariant().Trim();
            var arg = idx < 0 ? "" : line[(idx + 1)..].Trim();

            await DispatchAsync(cmd, arg, writer, ct);
        }

        _passiveListener?.Stop();
        _passiveListener = null;
    }

    // ── Command dispatch ──────────────────────────────────────────────────────

    private async Task DispatchAsync(string cmd, string arg, StreamWriter w, CancellationToken ct)
    {
        switch (cmd)
        {
            case "QUIT":
                await w.WriteLineAsync("221 Goodbye.");
                _state = State.Closed;
                return;

            case "NOOP":
                await w.WriteLineAsync("200 OK.");
                return;

            case "FEAT":
                await w.WriteLineAsync("211-Features:");
                await w.WriteLineAsync(" PASV");
                await w.WriteLineAsync(" UTF8");
                await w.WriteLineAsync("211 End");
                return;

            case "SYST":
                await w.WriteLineAsync("215 UNIX Type: L8");
                return;

            case "USER":
                _pendingUser = arg;
                _state       = State.WaitingForPassword;
                await w.WriteLineAsync($"331 Password required for {arg}.");
                return;

            case "PASS":
                if (_state != State.WaitingForPassword)
                {
                    await w.WriteLineAsync("503 Bad sequence of commands.");
                    return;
                }
                if (Authenticate(_pendingUser, arg))
                {
                    _username = _pendingUser;
                    _state    = State.LoggedIn;
                    _logger.Info($"FTP login: {_username} from {_client.Client.RemoteEndPoint}");
                    await w.WriteLineAsync("230 User logged in.");
                }
                else
                {
                    _state = State.Initial;
                    _logger.Warning($"FTP login failed for '{_pendingUser}' from {_client.Client.RemoteEndPoint}");
                    await w.WriteLineAsync("530 Login incorrect.");
                }
                return;
        }

        if (_state != State.LoggedIn)
        {
            await w.WriteLineAsync("530 Not logged in.");
            return;
        }

        await DispatchAuthenticatedAsync(cmd, arg, w, ct);
    }

    private async Task DispatchAuthenticatedAsync(string cmd, string arg, StreamWriter w, CancellationToken ct)
    {
        switch (cmd)
        {
            case "PWD":
            case "XPWD":
                await w.WriteLineAsync($"257 \"{_cwd}\" is the current directory.");
                break;

            case "CWD":
            case "XCWD":
                _cwd = ResolvePath(arg);
                await w.WriteLineAsync($"250 Directory changed to \"{_cwd}\".");
                break;

            case "CDUP":
                _cwd = _cwd == "/" ? "/" : _cwd[.._cwd.LastIndexOf('/')];
                if (_cwd.Length == 0) _cwd = "/";
                await w.WriteLineAsync($"250 Directory changed to \"{_cwd}\".");
                break;

            case "TYPE":
                await w.WriteLineAsync("200 Type set.");
                break;

            case "PASV":
                await HandlePasvAsync(w);
                break;

            case "LIST":
            case "NLST":
                await HandleListAsync(w, ct);
                break;

            case "STOR":
                await HandleStorAsync(arg, w, ct);
                break;

            case "RETR":
                await w.WriteLineAsync("550 Not available — receive-only server.");
                break;

            case "DELE":
            case "MKD":
            case "RMD":
            case "RNFR":
            case "RNTO":
            case "SITE":
            case "APPE":
                await w.WriteLineAsync("502 Command not implemented.");
                break;

            default:
                await w.WriteLineAsync($"502 Command not implemented: {cmd}");
                break;
        }
    }

    // ── PASV ─────────────────────────────────────────────────────────────────

    private async Task HandlePasvAsync(StreamWriter w)
    {
        // Close any previously allocated but unused passive listener.
        _passiveListener?.Stop();
        _passiveListener = null;

        var cfg  = _config.Config;
        var bind = IPAddress.Any;

        for (var port = cfg.FtpPassivePortMin; port <= cfg.FtpPassivePortMax; port++)
        {
            try
            {
                var listener = new TcpListener(bind, port);
                listener.Start(1);
                _passiveListener = listener;

                var ip = _localIp.AddressFamily == AddressFamily.InterNetwork
                    ? _localIp.GetAddressBytes()
                    : IPAddress.Loopback.GetAddressBytes();   // fallback for non-IPv4 local addresses

                var p1 = port / 256;
                var p2 = port % 256;
                await w.WriteLineAsync(
                    $"227 Entering Passive Mode ({ip[0]},{ip[1]},{ip[2]},{ip[3]},{p1},{p2}).");
                return;
            }
            catch (SocketException) { /* port in use — try next */ }
        }

        await w.WriteLineAsync("421 Unable to open passive connection — no ports available.");
    }

    // ── LIST ─────────────────────────────────────────────────────────────────

    private async Task HandleListAsync(StreamWriter w, CancellationToken ct)
    {
        if (_passiveListener == null)
        {
            await w.WriteLineAsync("425 Use PASV first.");
            return;
        }

        await w.WriteLineAsync("150 Opening data connection.");

        var listener = _passiveListener;
        _passiveListener = null;

        try
        {
            using var acceptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            acceptCts.CancelAfter(TimeSpan.FromSeconds(30));
            using var dataClient = await listener.AcceptTcpClientAsync(acceptCts.Token);
            listener.Stop();

            using var ds     = dataClient.GetStream();
            using var dw     = new StreamWriter(ds, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            var now          = DateTime.UtcNow.ToString("MMM dd HH:mm");

            foreach (var dir in VirtualSubdirsAt(_cwd))
                await dw.WriteLineAsync($"drwxr-xr-x  1 ftp ftp          0 {now} {dir}");
        }
        catch (OperationCanceledException)
        {
            listener.Stop();
        }
        catch (Exception ex)
        {
            listener.Stop();
            _logger.Warning($"FTP LIST error: {ex.Message}");
        }

        await w.WriteLineAsync("226 Transfer complete.");
    }

    // ── STOR ─────────────────────────────────────────────────────────────────

    private async Task HandleStorAsync(string filename, StreamWriter w, CancellationToken ct)
    {
        if (_passiveListener == null)
        {
            await w.WriteLineAsync("425 Use PASV first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(filename))
        {
            _passiveListener.Stop();
            _passiveListener = null;
            await w.WriteLineAsync("501 Missing filename.");
            return;
        }

        var decision = _router.Resolve(_username, _cwd);
        if (decision.IsUnrouted)
        {
            _passiveListener.Stop();
            _passiveListener = null;
            _logger.Warning($"FTP STOR '{filename}': {decision.MatchSource}");
            await w.WriteLineAsync("553 No routing rule matches this path/user.");
            return;
        }

        await w.WriteLineAsync("150 Opening data connection for file transfer.");

        var listener = _passiveListener;
        _passiveListener = null;

        byte[] data;
        try
        {
            using var acceptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            acceptCts.CancelAfter(TimeSpan.FromSeconds(30));
            using var dataClient = await listener.AcceptTcpClientAsync(acceptCts.Token);
            listener.Stop();

            using var ds  = dataClient.GetStream();
            using var buf = new MemoryStream();
            await ds.CopyToAsync(buf, ct);
            data = buf.ToArray();
        }
        catch (OperationCanceledException)
        {
            listener.Stop();
            await w.WriteLineAsync("426 Data connection timeout — transfer aborted.");
            return;
        }
        catch (Exception ex)
        {
            listener.Stop();
            _logger.Error($"FTP STOR data channel error for '{filename}': {ex.Message}");
            await w.WriteLineAsync("426 Connection closed; transfer aborted.");
            return;
        }

        _logger.Info($"FTP STOR '{filename}' from {_username}@{_cwd} ({data.Length / 1024.0:F1} KB) → {decision.MatchSource}");

        try
        {
            var now    = DateTime.UtcNow;
            var varCtx = new PathVariableContext
            {
                Username = _username,
                FtpPath  = _cwd,
                Date     = now.ToString("yyyy-MM-dd"),
                DateTime = now.ToString("yyyy-MM-dd_HHmmss"),
            };

            await _storer.StoreRawFileAsync(filename, data, decision, varCtx, ct);
            await w.WriteLineAsync("226 Transfer complete.");
        }
        catch (Exception ex)
        {
            _logger.Error($"FTP STOR upload failed for '{filename}': {ex.Message}");
            await w.WriteLineAsync("451 Requested action aborted: upload failed.");
        }
    }

    // ── Virtual directory listing ─────────────────────────────────────────────

    /// <summary>
    /// Returns the direct child directory names visible under <paramref name="cwd"/>,
    /// derived from the enabled FTP routing rules.
    /// </summary>
    private IEnumerable<string> VirtualSubdirsAt(string cwd)
    {
        // Normalise CWD: "/" → prefix "/", "/Invoices" → prefix "/Invoices/"
        var cwdNorm = cwd == "/" ? "" : cwd.TrimEnd('/');
        var prefix  = cwdNorm + "/";

        return _config.Config.FtpRules
            .Where(r => r.Enabled)
            .Select(r => "/" + r.VirtualPath.Trim('/'))   // normalise rule path to /segment
            .Where(p => p.Length > prefix.Length
                        && p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(p => p[prefix.Length..].Split('/')[0]) // first segment after the CWD prefix
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    private bool Authenticate(string username, string password)
    {
        if (_config.Config.FtpAcceptAnyLogin) return true;

        var user = _config.Config.FtpUsers.FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        if (user == null) return false;
        if (user.AcceptAnyPassword) return true;
        return user.Password == password;
    }

    // ── Path helpers ──────────────────────────────────────────────────────────

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return _cwd;
        var raw = path.StartsWith('/') ? path : _cwd.TrimEnd('/') + "/" + path;
        return NormalizeCwd(raw);
    }

    private static string NormalizeCwd(string path)
    {
        var parts = new Stack<string>();
        foreach (var seg in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (seg == "..") { if (parts.Count > 0) parts.Pop(); }
            else if (seg != ".") parts.Push(seg);
        }
        var segs = parts.Reverse().ToArray();
        return segs.Length == 0 ? "/" : "/" + string.Join("/", segs);
    }
}
