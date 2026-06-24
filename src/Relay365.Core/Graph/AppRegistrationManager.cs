using Microsoft.Graph;
using Microsoft.Graph.Applications.Item.AddPassword;
using Microsoft.Graph.Models;
using Relay365.Core.Logging;

namespace Relay365.Core.Graph;

public class AppRegistrationManager
{
    // Tag applied to every registration we create so we can find them again
    private const string RelayTag = "365Relay";

    private const string MailSendRoleId         = "b633e1c5-b582-4048-a93e-9f11b44c7e96";
    private const string MailReadWriteRoleId     = "e2a3a72e-5f79-4c64-b1b1-878b674786c6";
    private const string FilesReadWriteAllRoleId = "75359482-378d-4052-8f01-80520e7db3cd";
    private const string SitesReadWriteAllRoleId = "9492366f-7969-46a4-8d15-ed1a20078fff";
    private const string GraphAppId              = "00000003-0000-0000-c000-000000000000";

    private static readonly string[] AllRelayRoleIds =
    {
        MailSendRoleId, MailReadWriteRoleId, FilesReadWriteAllRoleId, SitesReadWriteAllRoleId
    };

    private readonly GraphServiceClient _adminClient;
    private readonly RelayLogger _logger;

    public AppRegistrationManager(GraphServiceClient adminClient, RelayLogger logger)
    {
        _adminClient = adminClient;
        _logger = logger;
    }

    public async Task<List<AppRegistrationInfo>> SearchExistingAsync(CancellationToken ct = default)
    {
        _logger.Info("Searching for existing 365Relay app registrations…");
        var results = new List<AppRegistrationInfo>();

        // Primary: filter by tag
        try
        {
            var page = await _adminClient.Applications.GetAsync(req =>
            {
                req.QueryParameters.Filter = $"tags/any(t:t eq '{RelayTag}')";
                req.QueryParameters.Select =
                    new[] { "id", "appId", "displayName", "createdDateTime", "passwordCredentials" };
            }, ct);

            if (page?.Value is { Count: > 0 } apps)
            {
                foreach (var a in apps)
                    results.Add(await MapAsync(a, ct));

                _logger.Success($"Found {results.Count} registration(s) via tag.");
                return results;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Tag filter unsupported, falling back to name search ({ex.Message})");
        }

        // Fallback: display name prefix
        try
        {
            var page = await _adminClient.Applications.GetAsync(req =>
            {
                req.QueryParameters.Filter = $"startswith(displayName,'{RelayTag}')";
                req.QueryParameters.Select =
                    new[] { "id", "appId", "displayName", "createdDateTime", "passwordCredentials" };
            }, ct);

            if (page?.Value != null)
                foreach (var a in page.Value)
                    results.Add(await MapAsync(a, ct));
        }
        catch (Exception ex)
        {
            _logger.Warning($"Name search failed: {ex.Message}");
        }

        _logger.Info($"Found {results.Count} registration(s).");
        return results;
    }

    public async Task<AppRegistrationInfo> CreateAsync(string displayName, CancellationToken ct = default)
    {
        _logger.Info($"Creating app registration '{displayName}'…");

        var app = new Application
        {
            DisplayName = displayName,
            Tags = new List<string> { RelayTag },
            SignInAudience = "AzureADMyOrg",
            RequiredResourceAccess = new List<RequiredResourceAccess>
            {
                new()
                {
                    ResourceAppId = GraphAppId,
                    ResourceAccess = new List<ResourceAccess>
                    {
                        new() { Id = Guid.Parse(MailSendRoleId),          Type = "Role" },
                        new() { Id = Guid.Parse(MailReadWriteRoleId),      Type = "Role" },
                        new() { Id = Guid.Parse(FilesReadWriteAllRoleId), Type = "Role" },
                        new() { Id = Guid.Parse(SitesReadWriteAllRoleId), Type = "Role" }
                    }
                }
            }
        };

        var created = await _adminClient.Applications.PostAsync(app, cancellationToken: ct)
            ?? throw new InvalidOperationException("App registration creation returned null.");

        _logger.Success($"App registered — client ID: {created.AppId}");

        // Create service principal so we can grant consent
        _logger.Info("Creating service principal…");
        var sp = await _adminClient.ServicePrincipals.PostAsync(
            new ServicePrincipal { AppId = created.AppId }, cancellationToken: ct)
            ?? throw new InvalidOperationException("Service principal creation returned null.");

        _logger.Success("Service principal created.");
        await GrantAdminConsentAsync(sp.Id!, ct);

        var secret = await GenerateSecretAsync(created.Id!, displayName, ct);

        return new AppRegistrationInfo
        {
            ObjectId = created.Id!,
            AppId = created.AppId!,
            DisplayName = created.DisplayName!,
            ServicePrincipalId = sp.Id!,
            CreatedDateTime = created.CreatedDateTime?.DateTime,
            SecretExpiry = secret.expiry,
            ClientSecret = secret.value
        };
    }

    public async Task<AppRegistrationInfo> RegenerateSecretAsync(
        AppRegistrationInfo info, CancellationToken ct = default)
    {
        _logger.Info($"Regenerating client secret for '{info.DisplayName}'…");
        var secret = await GenerateSecretAsync(info.ObjectId, info.DisplayName, ct);
        return new AppRegistrationInfo
        {
            ObjectId = info.ObjectId,
            AppId = info.AppId,
            DisplayName = info.DisplayName,
            ServicePrincipalId = info.ServicePrincipalId,
            CreatedDateTime = info.CreatedDateTime,
            SecretExpiry = secret.expiry,
            ClientSecret = secret.value
        };
    }

    public async Task DeleteAsync(string objectId, CancellationToken ct = default)
    {
        _logger.Info("Deleting app registration…");
        await _adminClient.Applications[objectId].DeleteAsync(cancellationToken: ct);
        _logger.Success("App registration deleted.");
    }

    // ── private helpers ─────────────────────────────────────────────────────

    private async Task GrantAdminConsentAsync(string spId, CancellationToken ct)
    {
        _logger.Info("Granting admin consent for Mail.Send, Mail.ReadWrite, Files.ReadWrite.All, Sites.ReadWrite.All…");
        var graphSpId = await GetGraphServicePrincipalIdAsync(ct);

        foreach (var roleId in AllRelayRoleIds)
        {
            await _adminClient.ServicePrincipals[spId].AppRoleAssignments.PostAsync(
                new AppRoleAssignment
                {
                    PrincipalId = Guid.Parse(spId),
                    ResourceId  = Guid.Parse(graphSpId),
                    AppRoleId   = Guid.Parse(roleId)
                }, cancellationToken: ct);
        }

        _logger.Success("Admin consent granted.");
    }

    /// <summary>
    /// Adds Files.ReadWrite.All and Sites.ReadWrite.All to an existing relay app SP.
    /// Skips roles that are already assigned to avoid 409 errors.
    /// </summary>
    public async Task UpdatePermissionsAsync(string appSpId, CancellationToken ct = default)
    {
        _logger.Info("Checking existing permissions…");
        var graphSpId = await GetGraphServicePrincipalIdAsync(ct);

        var existing = await _adminClient.ServicePrincipals[appSpId].AppRoleAssignments
            .GetAsync(cancellationToken: ct);
        var existingRoleIds = existing?.Value?
            .Select(r => r.AppRoleId?.ToString().ToUpperInvariant())
            .ToHashSet() ?? new HashSet<string?>();

        var toAdd = AllRelayRoleIds
            .Where(r => !existingRoleIds.Contains(r.ToUpperInvariant()))
            .ToList();

        if (toAdd.Count == 0)
        {
            _logger.Success("All required permissions are already granted.");
            return;
        }

        _logger.Info($"Adding {toAdd.Count} missing permission(s)…");
        foreach (var roleId in toAdd)
        {
            await _adminClient.ServicePrincipals[appSpId].AppRoleAssignments.PostAsync(
                new AppRoleAssignment
                {
                    PrincipalId = Guid.Parse(appSpId),
                    ResourceId  = Guid.Parse(graphSpId),
                    AppRoleId   = Guid.Parse(roleId)
                }, cancellationToken: ct);
            _logger.Success($"Granted: {roleId}");
        }

        _logger.Success("Permission update complete.");
    }

    private async Task<string> GetGraphServicePrincipalIdAsync(CancellationToken ct)
    {
        var graphSps = await _adminClient.ServicePrincipals.GetAsync(req =>
        {
            req.QueryParameters.Filter = $"appId eq '{GraphAppId}'";
            req.QueryParameters.Select = new[] { "id" };
        }, ct);

        return graphSps?.Value?.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException(
                "Could not locate the Microsoft Graph service principal in this tenant.");
    }

    private async Task<(string value, DateTime? expiry)> GenerateSecretAsync(
        string appObjectId, string hint, CancellationToken ct)
    {
        var cred = await _adminClient.Applications[appObjectId].AddPassword.PostAsync(
            new AddPasswordPostRequestBody
            {
                PasswordCredential = new PasswordCredential
                {
                    DisplayName = $"365Relay-{DateTime.UtcNow:yyyyMMdd}",
                    EndDateTime = DateTimeOffset.UtcNow.AddYears(2)
                }
            }, cancellationToken: ct) ?? throw new InvalidOperationException("Secret generation returned null.");

        _logger.Success($"Client secret created (expires {cred.EndDateTime:yyyy-MM-dd})");
        return (cred.SecretText!, cred.EndDateTime?.DateTime);
    }

    private async Task<AppRegistrationInfo> MapAsync(Application app, CancellationToken ct)
    {
        string spId = "";
        try
        {
            var sps = await _adminClient.ServicePrincipals.GetAsync(req =>
            {
                req.QueryParameters.Filter = $"appId eq '{app.AppId}'";
                req.QueryParameters.Select = new[] { "id" };
            }, ct);
            spId = sps?.Value?.FirstOrDefault()?.Id ?? "";
        }
        catch { }

        var latestSecret = app.PasswordCredentials?
            .OrderByDescending(p => p.EndDateTime)
            .FirstOrDefault();

        return new AppRegistrationInfo
        {
            ObjectId = app.Id ?? "",
            AppId = app.AppId ?? "",
            DisplayName = app.DisplayName ?? "",
            ServicePrincipalId = spId,
            CreatedDateTime = app.CreatedDateTime?.DateTime,
            SecretExpiry = latestSecret?.EndDateTime?.DateTime
        };
    }
}
