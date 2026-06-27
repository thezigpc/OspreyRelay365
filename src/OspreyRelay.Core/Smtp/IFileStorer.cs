using OspreyRelay.Core.Config;
using OspreyRelay.Core.Routing;

namespace OspreyRelay.Core.Smtp;

public interface IFileStorer
{
    Task StoreAsync(ReceivedEmail received, RouteDecision decision, CancellationToken ct);
    Task StoreRawFileAsync(string originalFilename, byte[] data, FtpRouteDecision decision, PathVariableContext varCtx, CancellationToken ct);
}
