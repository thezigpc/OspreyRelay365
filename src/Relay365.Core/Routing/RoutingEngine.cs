using MimeKit;
using Relay365.Core.Config;
using Relay365.Core.Smtp;

namespace Relay365.Core.Routing;

/// <summary>
/// Pure, synchronous routing decision engine.
/// Priority chain:
///   1. Explicit FileRules   (exact To: match)
///   2. SuffixRules          (domain-pattern match on To:)
///   3. Legacy SenderRoutes  (exact From: match — email relay only)
///   4. GlobalMode default   (EmailRelay | FileStorage→catchall | Hybrid→NoMatchBehavior)
/// </summary>
public class RoutingEngine
{
    private readonly ConfigManager _configManager;

    public RoutingEngine(ConfigManager configManager)
    {
        _configManager = configManager;
    }

    public RouteDecision Resolve(ReceivedEmail received)
    {
        var cfg = _configManager.Config;

        string mimeFrom = "";
        try
        {
            using var ms = new MemoryStream(received.RawData);
            var parser = new MimeParser(ms, MimeFormat.Default);
            var headers = parser.ParseHeaders();
            mimeFrom = headers["From"] is string fromHdr
                ? MailboxAddress.TryParse(fromHdr, out var mb) ? mb.Address : ""
                : "";
        }
        catch { /* malformed headers — continue with envelope From */ }

        // ── 1. Explicit recipient file rules ─────────────────────────────────
        foreach (var toAddr in received.EnvelopeTo)
        {
            var rule = cfg.FileRules.FirstOrDefault(r =>
                r.Enabled &&
                string.Equals(r.ToAddress, toAddr, StringComparison.OrdinalIgnoreCase));

            if (rule != null)
                return BuildExplicitDecision(rule, toAddr, cfg);
        }

        // ── 2. Suffix rules ──────────────────────────────────────────────────
        foreach (var toAddr in received.EnvelopeTo)
        {
            foreach (var rule in cfg.SuffixRules.Where(s => s.Enabled))
            {
                var match = TryMatchSuffix(toAddr, rule);
                if (match is not null)
                    return BuildSuffixDecision(rule, match, cfg);
            }
        }

        // ── 3. Legacy SenderRoutes (From:-keyed email relay) ─────────────────
        if (cfg.SenderRoutes.Count > 0)
        {
            var key = FindSenderRouteKey(cfg, mimeFrom, received.EnvelopeFrom);
            if (key != null)
                return new RouteDecision
                {
                    Type = FileDestinationType.EmailRelay,
                    RelayVia = cfg.SenderRoutes[key],
                    MatchSource = $"SenderRoute:{key}"
                };
        }

        // ── 4. Global mode default ───────────────────────────────────────────
        return cfg.GlobalMode switch
        {
            RelayMode.EmailRelay => new RouteDecision
            {
                Type = FileDestinationType.EmailRelay,
                MatchSource = "GlobalDefault:EmailRelay"
            },
            RelayMode.FileStorage => BuildCatchAllDecision(cfg, "FileStorage"),
            RelayMode.Hybrid => cfg.NoMatchBehavior switch
            {
                NoMatchBehavior.Relay => new RouteDecision
                {
                    Type = FileDestinationType.EmailRelay,
                    MatchSource = "GlobalDefault:HybridFallback"
                },
                NoMatchBehavior.Reject => RouteDecision.Reject(),
                _ => BuildCatchAllDecision(cfg, "Hybrid")
            },
            _ => new RouteDecision { Type = FileDestinationType.EmailRelay, MatchSource = "Default" }
        };
    }

    // ── Global catch-all ──────────────────────────────────────────────────────

    private static RouteDecision BuildCatchAllDecision(RelayConfig cfg, string modeLabel)
    {
        if (!string.IsNullOrWhiteSpace(cfg.GlobalCatchAllOneDriveUser))
        {
            return new RouteDecision
            {
                Type                 = FileDestinationType.OneDrive,
                OneDriveUser         = cfg.GlobalCatchAllOneDriveUser,
                FolderPath           = string.IsNullOrWhiteSpace(cfg.GlobalCatchAllFolderPath)
                                       ? "/EmailRelay" : cfg.GlobalCatchAllFolderPath,
                UsePerEmailSubfolder = cfg.DefaultUsePerEmailSubfolder,
                SaveWhat             = cfg.DefaultSaveWhat,
                NoAttachmentBehavior = cfg.DefaultNoAttachmentBehavior,
                FromSenderHandling   = cfg.DefaultFromSenderHandling,
                FilenameTemplate     = cfg.DefaultFilenameTemplate,
                SubjectDelimiter     = cfg.DefaultSubjectDelimiter,
                FilenameSpaceReplacement = cfg.FilenameSpaceReplacement,
                MatchSource          = $"GlobalCatchAll:{modeLabel}"
            };
        }
        return RouteDecision.Unrouted($"NoRule:{modeLabel}");
    }

    // ── Suffix matching ───────────────────────────────────────────────────────

    private sealed record SuffixMatch(
        string ResolvedUser,    // UPN to use for OneDrive (localpart@baseDomain or explicit)
        string CapturedSuffix,  // segment captured from the address (populates %suffix%)
        string MatchedToAddress // the original To: address that matched
    );

    /// <summary>
    /// Returns a SuffixMatch when toAddr matches the rule, or null if not.
    ///
    /// Wildcard rule (Suffix blank/"*"):
    ///   BaseDomain must be set. Matches any address whose domain ends with .{baseDomain}
    ///   and has exactly one additional subdomain segment (the captured suffix).
    ///   e.g. BaseDomain=company.com matches invoices@files.company.com,
    ///        but NOT invoices@deep.files.company.com.
    ///
    /// Specific Suffix, specific BaseDomain:
    ///   domain must equal {suffix}.{baseDomain} or intermediate sub-segments allowed.
    ///
    /// Specific Suffix, blank BaseDomain:
    ///   domain must start with {suffix}. — matches any TLD.
    ///   resolvedUser = localpart@(domain after first dot).
    /// </summary>
    private static SuffixMatch? TryMatchSuffix(string toAddr, SuffixRule rule)
    {
        var atIdx = toAddr.IndexOf('@');
        if (atIdx < 1) return null;

        var localPart  = toAddr[..atIdx];
        var domain     = toAddr[(atIdx + 1)..].ToLowerInvariant();
        var isWildcard = string.IsNullOrWhiteSpace(rule.Suffix) || rule.Suffix.Trim() == "*";
        var baseDomain = rule.BaseDomain.Trim('.').ToLowerInvariant();

        if (isWildcard)
        {
            // Wildcard requires a base domain to avoid matching everything
            if (string.IsNullOrWhiteSpace(baseDomain)) return null;

            // domain must end with .{baseDomain} and have exactly one extra segment
            if (!domain.EndsWith("." + baseDomain, StringComparison.Ordinal)) return null;

            var prefix = domain[..^(baseDomain.Length + 1)]; // strip .baseDomain
            if (string.IsNullOrWhiteSpace(prefix) || prefix.Contains('.')) return null;

            return new SuffixMatch(
                ResolvedUser:    $"{localPart}@{baseDomain}",
                CapturedSuffix:  prefix,
                MatchedToAddress: toAddr);
        }
        else
        {
            var suffix = rule.Suffix.Trim('.').ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(baseDomain))
            {
                // Specific suffix + base domain
                bool matches = domain == $"{suffix}.{baseDomain}"
                    || (domain.StartsWith($"{suffix}.") && domain.EndsWith($".{baseDomain}"));
                if (!matches) return null;

                return new SuffixMatch(
                    ResolvedUser:    $"{localPart}@{baseDomain}",
                    CapturedSuffix:  suffix,
                    MatchedToAddress: toAddr);
            }
            else
            {
                // Specific suffix, no base domain — match any TLD
                if (!domain.StartsWith($"{suffix}.")) return null;

                var afterSuffix = domain[(suffix.Length + 1)..]; // everything after "suffix."
                return new SuffixMatch(
                    ResolvedUser:    $"{localPart}@{afterSuffix}",
                    CapturedSuffix:  suffix,
                    MatchedToAddress: toAddr);
            }
        }
    }

    // ── Decision builders ─────────────────────────────────────────────────────

    private static RouteDecision BuildExplicitDecision(
        RecipientFileRule rule, string toAddr, RelayConfig cfg)
    {
        if (rule.DestinationType == FileDestinationType.EmailRelay)
            return new RouteDecision
            {
                Type             = FileDestinationType.EmailRelay,
                RelayVia         = rule.RelayVia,
                MatchedToAddress = toAddr,
                MatchSource      = $"ExplicitRule:{rule.ToAddress}"
            };

        if (rule.DestinationType == FileDestinationType.SmarthostRelay)
            return new RouteDecision
            {
                Type             = FileDestinationType.SmarthostRelay,
                MatchedToAddress = toAddr,
                SmarthostOverride = rule.UseGlobalSmarthost ? null : new SmarthostConfig(
                    Host: rule.SmarthostOverrideHost, Port: rule.SmarthostOverridePort,
                    Tls: rule.SmarthostOverrideTls, Username: rule.SmarthostOverrideUsername,
                    Password: rule.SmarthostOverridePassword,
                    UseOriginalFrom: true, FallbackSenderEmail: cfg.FallbackSenderEmail),
                MatchSource = $"ExplicitRule:{rule.ToAddress}"
            };

        return new RouteDecision
        {
            Type                 = rule.DestinationType,
            OneDriveUser         = string.IsNullOrWhiteSpace(rule.OneDriveUser) ? null : rule.OneDriveUser,
            DriveId              = string.IsNullOrWhiteSpace(rule.LibraryDriveId) ? null : rule.LibraryDriveId,
            FolderPath           = rule.FolderPath,
            UsePerEmailSubfolder = rule.UsePerEmailSubfolder ?? cfg.DefaultUsePerEmailSubfolder,
            SaveWhat             = rule.SaveWhat ?? cfg.DefaultSaveWhat,
            NoAttachmentBehavior = rule.NoAttachmentBehavior ?? cfg.DefaultNoAttachmentBehavior,
            FromSenderHandling   = rule.FromSenderHandling,
            MatchedToAddress     = toAddr,
            FilenameTemplate     = rule.FilenameTemplate ?? cfg.DefaultFilenameTemplate,
            SubjectDelimiter     = rule.SubjectDelimiter ?? cfg.DefaultSubjectDelimiter,
            FilenameSpaceReplacement = rule.FilenameSpaceReplacement ?? cfg.FilenameSpaceReplacement,
            MatchSource          = $"ExplicitRule:{rule.ToAddress}"
        };
    }

    private static RouteDecision BuildSuffixDecision(
        SuffixRule rule, SuffixMatch match, RelayConfig cfg)
    {
        if (rule.DestinationType == FileDestinationType.SmarthostRelay)
            return new RouteDecision
            {
                Type             = FileDestinationType.SmarthostRelay,
                MatchedToAddress = match.MatchedToAddress,
                ToBaseDomain     = rule.BaseDomain,
                SmarthostOverride = rule.UseGlobalSmarthost ? null : new SmarthostConfig(
                    Host: rule.SmarthostOverrideHost, Port: rule.SmarthostOverridePort,
                    Tls: rule.SmarthostOverrideTls, Username: rule.SmarthostOverrideUsername,
                    Password: rule.SmarthostOverridePassword,
                    UseOriginalFrom: true, FallbackSenderEmail: cfg.FallbackSenderEmail),
                MatchSource = $"SuffixRule:{rule.Suffix}.{rule.BaseDomain}[Smarthost]"
            };

        // OneDrive user: explicit override on rule, else resolved from address
        var oneDriveUser = !string.IsNullOrWhiteSpace(rule.OneDriveUser)
            ? rule.OneDriveUser
            : match.ResolvedUser;

        return new RouteDecision
        {
            Type                 = rule.DestinationType,
            OneDriveUser         = oneDriveUser,
            DriveId              = string.IsNullOrWhiteSpace(rule.LibraryDriveId) ? null : rule.LibraryDriveId,
            FolderPath           = rule.FolderPath,
            UsePerEmailSubfolder = rule.UsePerEmailSubfolder ?? cfg.DefaultUsePerEmailSubfolder,
            SaveWhat             = rule.SaveWhat ?? cfg.DefaultSaveWhat,
            NoAttachmentBehavior = rule.NoAttachmentBehavior ?? cfg.DefaultNoAttachmentBehavior,
            FromSenderHandling   = rule.FromSenderHandling,
            MatchedToAddress     = match.MatchedToAddress,
            ToBaseDomain         = rule.BaseDomain,  // %tobasedomain% populated from config only
            FilenameTemplate     = rule.FilenameTemplate ?? cfg.DefaultFilenameTemplate,
            SubjectDelimiter     = rule.SubjectDelimiter ?? cfg.DefaultSubjectDelimiter,
            FilenameSpaceReplacement = rule.FilenameSpaceReplacement ?? cfg.FilenameSpaceReplacement,
            MatchSource          = $"SuffixRule:{rule.Suffix}.{rule.BaseDomain}→{match.ResolvedUser}"
        };
    }

    private static string? FindSenderRouteKey(RelayConfig cfg, string mimeFrom, string envelopeFrom)
    {
        if (!string.IsNullOrWhiteSpace(mimeFrom) && cfg.SenderRoutes.ContainsKey(mimeFrom))
            return mimeFrom;
        if (!string.IsNullOrWhiteSpace(envelopeFrom) && cfg.SenderRoutes.ContainsKey(envelopeFrom))
            return envelopeFrom;
        return null;
    }
}
