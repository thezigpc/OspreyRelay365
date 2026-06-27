using OspreyRelay.Core.Config;
using OspreyRelay.Core.Routing;

namespace OspreyRelay.Core.Smtp;

public interface IMailSender
{
    Task SendAsync(ReceivedEmail email, RouteDecision decision, CancellationToken ct);
    void RefreshClient();
    Task<string> GetAccessTokenAsync(CancellationToken ct);
}
