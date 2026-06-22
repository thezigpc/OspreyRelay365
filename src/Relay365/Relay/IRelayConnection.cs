using Relay365.Core.Logging;

namespace Relay365.Relay;

/// <summary>
/// Abstraction over the relay lifecycle and log stream.
/// Swap the implementation to move from in-process → named pipe → web socket
/// without touching MainForm.
/// </summary>
public interface IRelayConnection : IDisposable
{
    /// <summary>True when the relay is actively processing SMTP connections.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Fired for every log entry produced by the relay.
    /// May be raised on a background thread — callers must marshal to the UI thread.
    /// </summary>
    event EventHandler<LogEntry> LogReceived;

    /// <summary>Begin monitoring (start file watcher, open pipe, etc.).</summary>
    void Open();

    /// <summary>Stop monitoring and release resources, but do not stop the relay itself.</summary>
    void Close();

    /// <summary>Start the relay (in-process or via SCM / named-pipe command).</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Stop the relay.</summary>
    Task StopAsync(CancellationToken ct = default);
}
