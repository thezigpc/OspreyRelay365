namespace OspreyRelay.Core.Logging;

public enum LogLevel { Debug, Info, Success, Warning, Error }

public record LogEntry(DateTime Timestamp, LogLevel Level, string Message);

public class RelayLogger
{
    public event EventHandler<LogEntry>? LogReceived;

    public bool DebugMode { get; set; }

    private readonly string? _logFilePath;
    private readonly string? _debugLogFilePath;
    private readonly object _fileLock = new();

    public RelayLogger(string? logFilePath = null)
    {
        _logFilePath = logFilePath;
        if (logFilePath != null)
            _debugLogFilePath = Path.Combine(
                Path.GetDirectoryName(logFilePath)!, "relay-debug.log");
    }

    public void Log(LogLevel level, string message)
    {
        // Debug entries only written when debug mode is on
        if (level == LogLevel.Debug && !DebugMode) return;

        var entry = new LogEntry(DateTime.Now, level, message);

        lock (_fileLock)
        {
            try
            {
                var line = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} [{level,-7}] {message}{Environment.NewLine}";

                // All levels go to the main log
                if (_logFilePath != null)
                    File.AppendAllText(_logFilePath, line);

                // Debug level also goes to a dedicated debug log (always, when debug mode on)
                if (level == LogLevel.Debug && _debugLogFilePath != null)
                    File.AppendAllText(_debugLogFilePath, line);
            }
            catch { /* never let logging kill the relay */ }
        }

        LogReceived?.Invoke(this, entry);
    }

    public void Debug(string msg)   => Log(LogLevel.Debug, msg);
    public void Info(string msg)    => Log(LogLevel.Info, msg);
    public void Success(string msg) => Log(LogLevel.Success, msg);
    public void Warning(string msg) => Log(LogLevel.Warning, msg);
    public void Error(string msg)   => Log(LogLevel.Error, msg);
}
