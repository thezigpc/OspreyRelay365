using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;
using Relay365.Core.Config;

namespace Relay365.Core.Graph;

public static class GraphClientFactory
{
    public static GraphServiceClient Create(RelayConfig config)
    {
        var credential = new ClientSecretCredential(
            config.TenantId,
            config.ClientId,
            config.ClientSecret);

        return new GraphServiceClient(credential);
    }

    public static GraphServiceClient CreateWithToken(string accessToken)
    {
        return new GraphServiceClient(new StaticTokenCredential(accessToken));
    }
}

/// <summary>Wraps a bearer token already obtained via MSAL for use with the Graph SDK.</summary>
internal sealed class StaticTokenCredential : TokenCredential
{
    private readonly AccessToken _token;

    public StaticTokenCredential(string token)
    {
        _token = new AccessToken(token, DateTimeOffset.MaxValue);
    }

    public override AccessToken GetToken(TokenRequestContext ctx, CancellationToken ct) => _token;

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext ctx, CancellationToken ct) =>
        ValueTask.FromResult(_token);
}
