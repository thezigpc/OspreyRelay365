using Relay365.Core.Config;

namespace Relay365.Core.Ftp;

/// <summary>
/// Maps an FTP session's (username, virtual working directory) pair to a routing rule.
///
/// Match priority within the enabled rules:
///   1. VirtualPath prefix matches AND Username matches exactly  — longest path wins
///   2. VirtualPath prefix matches AND Username is empty (wildcard) — longest path wins
/// </summary>
public class FtpFileRouter
{
    private readonly ConfigManager _config;

    public FtpFileRouter(ConfigManager config) => _config = config;

    public FtpRouteDecision Resolve(string username, string virtualDirectory)
    {
        var rules = _config.Config.FtpRules.Where(r => r.Enabled).ToList();

        FtpRoutingRule? best = null;
        var bestLen     = -1;
        var bestHasUser = false;

        var dir = NormalizePath(virtualDirectory);

        foreach (var rule in rules)
        {
            var rulePath = NormalizePath(rule.VirtualPath);

            // The directory must start with the rule's path.
            // "/" matches everything; longer paths are more specific.
            bool pathMatches = dir.Equals(rulePath, StringComparison.OrdinalIgnoreCase)
                || dir.StartsWith(rulePath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)
                || rulePath == "/";

            if (!pathMatches) continue;

            var hasUser = !string.IsNullOrWhiteSpace(rule.Username)
                && rule.Username.Equals(username, StringComparison.OrdinalIgnoreCase);
            var isWild  = string.IsNullOrWhiteSpace(rule.Username);

            if (!hasUser && !isWild) continue;

            var len = rulePath.Length;

            // User-specific beats wildcard; among ties, longest path wins.
            if (best == null
                || (hasUser && !bestHasUser)
                || (hasUser == bestHasUser && len > bestLen))
            {
                best        = rule;
                bestLen     = len;
                bestHasUser = hasUser;
            }
        }

        if (best == null)
            return FtpRouteDecision.Unrouted($"no rule for user='{username}' path='{virtualDirectory}'");

        var oneDriveUser = string.IsNullOrWhiteSpace(best.OneDriveUser) ? null : best.OneDriveUser;
        var driveId      = string.IsNullOrWhiteSpace(best.LibraryDriveId) ? null : best.LibraryDriveId;

        return new FtpRouteDecision
        {
            DestinationType  = best.DestinationType,
            OneDriveUser     = oneDriveUser,
            DriveId          = driveId,
            FolderPath       = best.FolderPath,
            FilenameTemplate = best.FilenameTemplate,
            MatchSource      = $"{(best.Name.Length > 0 ? best.Name : best.Id)}@{best.VirtualPath}"
        };
    }

    private static string NormalizePath(string p)
    {
        p = p.Trim().TrimEnd('/');
        return string.IsNullOrEmpty(p) ? "/" : p.StartsWith('/') ? p : "/" + p;
    }
}
