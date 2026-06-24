using System.Text.RegularExpressions;
using MimeKit;
using Relay365.Core.Config;
using Relay365.Core.Smtp;

namespace Relay365.Core.Routing;

/// <summary>
/// Pure, synchronous routing decision engine.
/// Priority chain:
///   1. Unified Rules list  (top-to-bottom, first match wins)
///   2. Legacy SenderRoutes (exact From: match — email relay only)
///   3. GlobalMode default  (EmailRelay | FileStorage→catchall | Hybrid→NoMatchBehavior)
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

        // Parse MIME headers once — needed for From: and Subject: regex modes
        string mimeFrom = "";
        string mimeSubject = "";
        try
        {
            using var ms = new MemoryStream(received.RawData);
            var parser = new MimeParser(ms, MimeFormat.Default);
            var headers = parser.ParseHeaders();

            if (headers["From"] is string fromHdr)
                mimeFrom = MailboxAddress.TryParse(fromHdr, out var mb) ? mb.Address : fromHdr;

            mimeSubject = headers["Subject"] ?? "";
        }
        catch { /* malformed headers — continue with envelope values */ }

        // ── 1. Unified routing rules ──────────────────────────────────────────
        foreach (var rule in cfg.Rules)
        {
            if (!rule.Enabled) continue;

            var match = TryMatchRule(rule, received, mimeFrom, mimeSubject);
            if (match is not null)
                return BuildDecision(rule, match, cfg);
        }

        // ── 2. Legacy SenderRoutes (From:-keyed email relay) ─────────────────
        if (cfg.SenderRoutes.Count > 0)
        {
            var key = FindSenderRouteKey(cfg, mimeFrom, received.EnvelopeFrom);
            if (key != null)
                return new RouteDecision
                {
                    Type        = FileDestinationType.EmailRelay,
                    RelayVia    = cfg.SenderRoutes[key],
                    MatchSource = $"SenderRoute:{key}"
                };
        }

        // ── 3. Global mode default ───────────────────────────────────────────
        return cfg.GlobalMode switch
        {
            RelayMode.EmailRelay => new RouteDecision
            {
                Type        = FileDestinationType.EmailRelay,
                MatchSource = "GlobalDefault:EmailRelay"
            },
            RelayMode.FileStorage => BuildCatchAllDecision(cfg, "FileStorage"),
            RelayMode.Hybrid => cfg.NoMatchBehavior switch
            {
                NoMatchBehavior.Relay => new RouteDecision
                {
                    Type        = FileDestinationType.EmailRelay,
                    MatchSource = "GlobalDefault:HybridFallback"
                },
                NoMatchBehavior.Reject => RouteDecision.Reject(),
                _ => BuildCatchAllDecision(cfg, "Hybrid")
            },
            _ => new RouteDecision { Type = FileDestinationType.EmailRelay, MatchSource = "Default" }
        };
    }

    // ── Match result ──────────────────────────────────────────────────────────

    private sealed record RuleMatch(
        string MatchedToAddress,
        string CapturedSuffix,
        string ResolvedUser,
        Dictionary<string, string> RegexCaptures);

    // ── Rule matching dispatch ────────────────────────────────────────────────

    private static RuleMatch? TryMatchRule(
        RoutingRule rule, ReceivedEmail received, string mimeFrom, string mimeSubject)
    {
        return rule.Mode switch
        {
            MatchMode.DomainSuffix => TryMatchSuffix(rule, received),
            MatchMode.ExactTo      => TryMatchExactTo(rule, received),
            MatchMode.RegexTo      => TryMatchRegex(rule, received.EnvelopeTo, "To"),
            MatchMode.RegexFrom    => TryMatchRegexSingle(rule, mimeFrom, received.EnvelopeTo, "From"),
            MatchMode.RegexSubject => TryMatchRegexSingle(rule, mimeSubject, received.EnvelopeTo, "Subject"),
            _                      => null
        };
    }

    // ── Exact To: match ───────────────────────────────────────────────────────

    private static RuleMatch? TryMatchExactTo(RoutingRule rule, ReceivedEmail received)
    {
        var addr = received.EnvelopeTo.FirstOrDefault(a =>
            string.Equals(a, rule.Pattern, StringComparison.OrdinalIgnoreCase));

        return addr is null ? null : new RuleMatch(
            MatchedToAddress: addr,
            CapturedSuffix:   "",
            ResolvedUser:     addr,
            RegexCaptures:    new());
    }

    // ── Domain-suffix match ───────────────────────────────────────────────────

    private static RuleMatch? TryMatchSuffix(RoutingRule rule, ReceivedEmail received)
    {
        foreach (var toAddr in received.EnvelopeTo)
        {
            var m = TrySuffixAddress(toAddr, rule);
            if (m is not null) return m;
        }
        return null;
    }

    /// <summary>
    /// Wildcard (Suffix blank/"*"):
    ///   BaseDomain required. Matches any address whose domain = {oneSegment}.{baseDomain}.
    ///   CapturedSuffix = that segment; ResolvedUser = localpart@baseDomain.
    ///
    /// Specific Suffix + BaseDomain:
    ///   domain = {suffix}.{baseDomain} (or intermediate sub-segments allowed).
    ///
    /// Specific Suffix, blank BaseDomain:
    ///   domain starts with "{suffix}." — matches any TLD.
    ///   ResolvedUser = localpart@(domain-after-first-dot).
    /// </summary>
    private static RuleMatch? TrySuffixAddress(string toAddr, RoutingRule rule)
    {
        var atIdx = toAddr.IndexOf('@');
        if (atIdx < 1) return null;

        var localPart  = toAddr[..atIdx];
        var domain     = toAddr[(atIdx + 1)..].ToLowerInvariant();
        var isWildcard = string.IsNullOrWhiteSpace(rule.Suffix) || rule.Suffix.Trim() == "*";
        var baseDomain = rule.BaseDomain.Trim('.').ToLowerInvariant();

        if (isWildcard)
        {
            if (string.IsNullOrWhiteSpace(baseDomain)) return null;
            if (!domain.EndsWith("." + baseDomain, StringComparison.Ordinal)) return null;

            var prefix = domain[..^(baseDomain.Length + 1)];
            if (string.IsNullOrWhiteSpace(prefix) || prefix.Contains('.')) return null;

            return new RuleMatch(
                MatchedToAddress: toAddr,
                CapturedSuffix:   prefix,
                ResolvedUser:     $"{localPart}@{baseDomain}",
                RegexCaptures:    new());
        }
        else
        {
            var suffix = rule.Suffix.Trim('.').ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(baseDomain))
            {
                bool matches = domain == $"{suffix}.{baseDomain}"
                    || (domain.StartsWith($"{suffix}.") && domain.EndsWith($".{baseDomain}"));
                if (!matches) return null;

                return new RuleMatch(
                    MatchedToAddress: toAddr,
                    CapturedSuffix:   suffix,
                    ResolvedUser:     $"{localPart}@{baseDomain}",
                    RegexCaptures:    new());
            }
            else
            {
                if (!domain.StartsWith($"{suffix}.")) return null;

                var afterSuffix = domain[(suffix.Length + 1)..];
                return new RuleMatch(
                    MatchedToAddress: toAddr,
                    CapturedSuffix:   suffix,
                    ResolvedUser:     $"{localPart}@{afterSuffix}",
                    RegexCaptures:    new());
            }
        }
    }

    // ── Regex: To: (tries each envelope To: address) ──────────────────────────

    private static RuleMatch? TryMatchRegex(
        RoutingRule rule, IEnumerable<string> toAddrs, string fieldName)
    {
        var opts = BuildRegexOptions(rule);
        Regex re;
        try { re = new Regex(rule.Pattern, opts, TimeSpan.FromMilliseconds(250)); }
        catch { return null; }

        foreach (var addr in toAddrs)
        {
            var m = re.Match(addr);
            if (!m.Success) continue;

            return new RuleMatch(
                MatchedToAddress: addr,
                CapturedSuffix:   "",
                ResolvedUser:     addr,
                RegexCaptures:    ExtractCaptures(m));
        }
        return null;
    }

    // ── Regex: single value (From:, Subject:) ────────────────────────────────

    private static RuleMatch? TryMatchRegexSingle(
        RoutingRule rule, string value, IEnumerable<string> toAddrs, string fieldName)
    {
        if (string.IsNullOrEmpty(value)) return null;

        var opts = BuildRegexOptions(rule);
        Regex re;
        try { re = new Regex(rule.Pattern, opts, TimeSpan.FromMilliseconds(250)); }
        catch { return null; }

        var m = re.Match(value);
        if (!m.Success) return null;

        var firstTo = toAddrs.FirstOrDefault() ?? "";
        return new RuleMatch(
            MatchedToAddress: firstTo,
            CapturedSuffix:   "",
            ResolvedUser:     firstTo,
            RegexCaptures:    ExtractCaptures(m));
    }

    private static RegexOptions BuildRegexOptions(RoutingRule rule)
    {
        var opts = RegexOptions.None;
        if (rule.CaseInsensitive) opts |= RegexOptions.IgnoreCase;
        return opts;
    }

    private static Dictionary<string, string> ExtractCaptures(Match m)
    {
        var captures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Named groups (skip built-in "0" group)
        foreach (Group g in m.Groups)
        {
            if (!int.TryParse(g.Name, out _) && g.Success)
                captures[g.Name] = g.Value;
        }

        // Numbered groups as "match1", "match2", etc.
        for (int i = 1; i < m.Groups.Count; i++)
        {
            if (m.Groups[i].Success)
                captures[$"match{i}"] = m.Groups[i].Value;
        }

        return captures;
    }

    // ── Decision builder (unified) ────────────────────────────────────────────

    private static RouteDecision BuildDecision(RoutingRule rule, RuleMatch match, RelayConfig cfg)
    {
        var modeLabel = rule.Mode.ToString();

        // Delivery address: explicit override > suffix strip > null (unchanged)
        string? deliveryTo = !string.IsNullOrWhiteSpace(rule.DeliverToOverride)
            ? rule.DeliverToOverride
            : (rule.Mode == MatchMode.DomainSuffix && rule.StripSuffixFromTo)
                ? match.ResolvedUser
                : null;

        if (rule.DestinationType == FileDestinationType.EmailRelay)
            return new RouteDecision
            {
                Type              = FileDestinationType.EmailRelay,
                RelayVia          = rule.RelayVia,
                MatchedToAddress  = match.MatchedToAddress,
                DeliveryToAddress = deliveryTo,
                RewriteToHeader   = rule.RewriteToHeader,
                RegexCaptures     = match.RegexCaptures,
                MatchSource       = $"{modeLabel}:{rule.Pattern}{rule.Suffix}.{rule.BaseDomain}"
            };

        if (rule.DestinationType == FileDestinationType.SmarthostRelay)
            return new RouteDecision
            {
                Type              = FileDestinationType.SmarthostRelay,
                MatchedToAddress  = match.MatchedToAddress,
                ToBaseDomain      = rule.BaseDomain,
                DeliveryToAddress = deliveryTo,
                RewriteToHeader   = rule.RewriteToHeader,
                RegexCaptures     = match.RegexCaptures,
                SmarthostOverride = rule.UseGlobalSmarthost ? null : new SmarthostConfig(
                    Host: rule.SmarthostOverrideHost, Port: rule.SmarthostOverridePort,
                    Tls: rule.SmarthostOverrideTls, Username: rule.SmarthostOverrideUsername,
                    Password: rule.SmarthostOverridePassword,
                    UseOriginalFrom: true, FallbackSenderEmail: cfg.FallbackSenderEmail),
                MatchSource = $"{modeLabel}:{rule.Pattern}{rule.Suffix}.{rule.BaseDomain}[Smarthost]"
            };

        // File storage (OneDrive / SharePoint)
        // Blank UPN → use the best available resolved address for all match modes.
        // DomainSuffix: ResolvedUser = localpart@baseDomain (suffix stripped).
        // All others:   MatchedToAddress = the envelope To: that triggered the match.
        var oneDriveUser = !string.IsNullOrWhiteSpace(rule.OneDriveUser)
            ? rule.OneDriveUser
            : (!string.IsNullOrWhiteSpace(match.ResolvedUser)
                ? match.ResolvedUser
                : match.MatchedToAddress);

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
            SaveEmbeddedImages   = rule.SaveEmbeddedImages ?? cfg.DefaultSaveEmbeddedImages,
            MatchedToAddress     = match.MatchedToAddress,
            ToBaseDomain         = rule.BaseDomain,
            CapturedSuffix       = match.CapturedSuffix,
            DeliveryToAddress    = deliveryTo,
            RewriteToHeader      = rule.RewriteToHeader,
            RegexCaptures        = match.RegexCaptures,
            FilenameTemplate     = rule.FilenameTemplate ?? cfg.DefaultFilenameTemplate,
            SubjectDelimiter     = rule.SubjectDelimiter ?? cfg.DefaultSubjectDelimiter,
            FilenameSpaceReplacement = rule.FilenameSpaceReplacement ?? cfg.FilenameSpaceReplacement,
            MatchSource          = $"{modeLabel}:{rule.Pattern}{rule.Suffix}.{rule.BaseDomain}→{match.ResolvedUser}"
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

    // ── SenderRoutes helper ───────────────────────────────────────────────────

    private static string? FindSenderRouteKey(RelayConfig cfg, string mimeFrom, string envelopeFrom)
    {
        if (!string.IsNullOrWhiteSpace(mimeFrom) && cfg.SenderRoutes.ContainsKey(mimeFrom))
            return mimeFrom;
        if (!string.IsNullOrWhiteSpace(envelopeFrom) && cfg.SenderRoutes.ContainsKey(envelopeFrom))
            return envelopeFrom;
        return null;
    }
}
