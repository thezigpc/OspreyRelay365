namespace OspreyRelay.Core.Smtp;

public class ReceivedEmail
{
    public string EnvelopeFrom { get; set; } = "";
    public List<string> EnvelopeTo { get; set; } = new();
    public byte[] RawData { get; set; } = Array.Empty<byte>();
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
