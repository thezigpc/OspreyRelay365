namespace OspreyRelay.Core.Smtp;

/// <summary>
/// Thrown by IMailSender when Graph returns 503/504 (service temporarily unavailable).
/// MessageProcessor catches this to attempt smarthost failover before saving to unrouted.
/// </summary>
public class TransientGraphException : Exception
{
    public TransientGraphException(string message) : base(message) { }
    public TransientGraphException(string message, Exception inner) : base(message, inner) { }
}
