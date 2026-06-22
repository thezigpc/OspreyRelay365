using System.Text.Json.Serialization;

namespace Relay365.Core.Config;

// ── Global mode ───────────────────────────────────────────────────────────────
public enum RelayMode
{
    EmailRelay,   // all mail relayed through 365
    FileStorage,  // all mail stored to OneDrive/SharePoint
    Hybrid        // per-rule routing; no-match behaviour set by NoMatchBehavior
}

public enum NoMatchBehavior
{
    Relay,      // fall through to email relay
    Unrouted,   // save to unrouted folder
    Reject      // return SMTP 550
}

// ── File storage options ──────────────────────────────────────────────────────
public enum FileDestinationType { EmailRelay, OneDrive, SharePoint, SmarthostRelay }

/// <summary>
/// Resolved smarthost connection parameters passed through RouteDecision so MessageProcessor
/// can deliver without re-reading config. Null SmarthostOverride on a SmarthostRelay decision
/// means "use the global smarthost settings from RelayConfig".
/// </summary>
public record SmarthostConfig(
    string Host, int Port, SmarthostTls Tls,
    string Username, string Password,
    bool UseOriginalFrom, string FallbackSenderEmail);

public enum FileConflictBehavior { Rename, Replace, Fail }

public enum SaveWhat
{
    AttachmentsOnly,
    AttachmentsAndBody,
    FullEml
}

public enum NoAttachmentBehavior { Skip, SaveAsEml }

public enum FromSenderHandling
{
    Ignore,
    AsSubfolder,  // create subfolder named after sender address
    AsMetadata,   // tag SharePoint list-item columns (warns if column missing)
    Both
}

// ── Unrouted handling ─────────────────────────────────────────────────────────
public enum UnroutedAction
{
    LocalFolder,            // save .eml + sidecar .json to local folder (always the fallback)
    OneDriveRedirect,       // upload to configured OneDrive path; falls back to local on failure
    SharePointRedirect,     // upload to configured SharePoint library; falls back to local on failure
    EmailAsAttachment       // send as attachment via Graph; falls back to local on failure
}

// ── Rule models ───────────────────────────────────────────────────────────────

/// <summary>
/// Suffix-based routing.
/// When Suffix is blank or "*", acts as a wildcard: matches any single subdomain segment
/// and captures it as %suffix% in path/filename templates.
/// When Suffix is set, matches only that exact segment.
/// BaseDomain is optional; blank = match any domain containing the suffix segment.
/// For SharePoint destinations the path template is applied to a shared library
/// (use %suffix%, %toupn%, %from% etc. to build per-email folder structure).
/// </summary>
public class SuffixRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Suffix { get; set; } = "";       // blank/"*" = wildcard capture
    public string BaseDomain { get; set; } = "";   // blank = any domain; populates %tobasedomain%
    public bool Enabled { get; set; } = true;

    public FileDestinationType DestinationType { get; set; } = FileDestinationType.OneDrive;

    // OneDrive: explicit UPN; blank = resolve from matched address
    public string OneDriveUser { get; set; } = "";

    // SharePoint
    public string SiteUrl { get; set; } = "";
    public string SiteId { get; set; } = "";
    public string LibraryName { get; set; } = "";
    public string LibraryDriveId { get; set; } = "";

    // Common to OneDrive + SharePoint — supports path variables
    public string FolderPath { get; set; } = "/%toupn%";

    // Per-rule overrides — null means use the global default
    public bool? UsePerEmailSubfolder { get; set; }
    public SaveWhat? SaveWhat { get; set; }
    public NoAttachmentBehavior? NoAttachmentBehavior { get; set; }
    public FromSenderHandling FromSenderHandling { get; set; } = FromSenderHandling.Ignore;
    public string? FilenameTemplate { get; set; }          // null = use global default
    public string? SubjectDelimiter { get; set; }          // null = use global default
    public string? FilenameSpaceReplacement { get; set; }  // null = use global default

    // Smarthost routing — only used when DestinationType = SmarthostRelay
    public bool UseGlobalSmarthost { get; set; } = true;
    public string SmarthostOverrideHost { get; set; } = "";
    public int SmarthostOverridePort { get; set; } = 587;
    public SmarthostTls SmarthostOverrideTls { get; set; } = SmarthostTls.StartTls;
    public string SmarthostOverrideUsername { get; set; } = "";
    public string SmarthostOverridePasswordEncrypted { get; set; } = "";
    [JsonIgnore] public string SmarthostOverridePassword { get; set; } = "";

    // V2 placeholder — subject-line sub-routing within this suffix rule
    // public List<SubjectRoutingRule> SubjectRules { get; set; } = new();
}

/// <summary>
/// Explicit recipient rule: exact match on a To: envelope address.
/// Takes priority over suffix rules and SenderRoutes.
/// </summary>
public class RecipientFileRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string ToAddress { get; set; } = "";    // exact match (case-insensitive)
    public bool Enabled { get; set; } = true;

    public FileDestinationType DestinationType { get; set; } = FileDestinationType.EmailRelay;

    // EmailRelay: optional override mailbox — empty = passthrough
    public string RelayVia { get; set; } = "";

    // OneDrive: explicit user UPN — empty = resolve from address
    public string OneDriveUser { get; set; } = "";

    // SharePoint
    public string SiteUrl { get; set; } = "";
    public string SiteId { get; set; } = "";
    public string LibraryName { get; set; } = "";
    public string LibraryDriveId { get; set; } = "";

    // Common to OneDrive + SharePoint — supports path variables
    public string FolderPath { get; set; } = "";

    // Per-rule overrides — null means use the global default
    public bool? UsePerEmailSubfolder { get; set; }
    public SaveWhat? SaveWhat { get; set; }
    public NoAttachmentBehavior? NoAttachmentBehavior { get; set; }
    public FromSenderHandling FromSenderHandling { get; set; } = FromSenderHandling.Ignore;
    public string? FilenameTemplate { get; set; }          // null = use global default
    public string? SubjectDelimiter { get; set; }          // null = use global default
    public string? FilenameSpaceReplacement { get; set; }  // null = use global default

    // Smarthost routing — only used when DestinationType = SmarthostRelay
    public bool UseGlobalSmarthost { get; set; } = true;
    public string SmarthostOverrideHost { get; set; } = "";
    public int SmarthostOverridePort { get; set; } = 587;
    public SmarthostTls SmarthostOverrideTls { get; set; } = SmarthostTls.StartTls;
    public string SmarthostOverrideUsername { get; set; } = "";
    public string SmarthostOverridePasswordEncrypted { get; set; } = "";
    [JsonIgnore] public string SmarthostOverridePassword { get; set; } = "";

    // V2 placeholder — subject-line sub-routing
    // public List<SubjectRoutingRule> SubjectRules { get; set; } = new();
}

// ── Runtime routing result (not persisted) ────────────────────────────────────

/// <summary>
/// The decision returned by RoutingEngine.Resolve().
/// MessageProcessor dispatches based on Type / IsUnrouted / IsReject.
/// </summary>
public class RouteDecision
{
    public FileDestinationType Type { get; init; } = FileDestinationType.EmailRelay;
    public bool IsUnrouted { get; init; }
    public bool IsReject { get; init; }

    // Email relay
    public string RelayVia { get; init; } = "";    // specific mailbox; empty = passthrough

    // File storage (OneDrive or SharePoint)
    public string? OneDriveUser { get; init; }     // UPN; null if SharePoint or pre-resolved
    public string? DriveId { get; init; }           // pre-resolved drive ID; null = resolve at store time
    public string FolderPath { get; init; } = "";  // may contain %variable% tokens
    public bool UsePerEmailSubfolder { get; init; }
    public SaveWhat SaveWhat { get; init; } = SaveWhat.AttachmentsOnly;
    public NoAttachmentBehavior NoAttachmentBehavior { get; init; } = NoAttachmentBehavior.SaveAsEml;
    public FromSenderHandling FromSenderHandling { get; init; } = FromSenderHandling.Ignore;

    // Path/filename variable context
    public string MatchedToAddress { get; init; } = "";   // exact To: address that matched
    public string FilenameTemplate { get; init; } = "";   // resolved: rule override or global default
    public string SubjectDelimiter { get; init; } = " ";  // resolved: rule override or global default
    public string FilenameSpaceReplacement { get; init; } = ""; // resolved: rule override or global default
    public string ToBaseDomain { get; init; } = "";       // SuffixRule.BaseDomain; empty for non-suffix routes

    // Smarthost — null = use global config from RelayConfig; populated only for SmarthostRelay
    public SmarthostConfig? SmarthostOverride { get; init; }

    // Diagnostics
    public string MatchSource { get; init; } = "";

    public static RouteDecision Unrouted(string reason = "") =>
        new() { IsUnrouted = true, MatchSource = $"Unrouted:{reason}" };

    public static RouteDecision Reject() =>
        new() { IsReject = true, MatchSource = "Reject" };
}
