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

// ── Match mode ────────────────────────────────────────────────────────────────

/// <summary>
/// Determines how a RoutingRule matches an incoming email.
/// </summary>
public enum MatchMode
{
    DomainSuffix,   // subdomain-segment match on To: (was SuffixRule)
    ExactTo,        // case-insensitive exact match on To: envelope address (was RecipientFileRule)
    RegexTo,        // regex match on To: envelope address
    RegexFrom,      // regex match on From: envelope address
    RegexSubject    // regex match on Subject: header
}

// ── Unified rule model ────────────────────────────────────────────────────────

/// <summary>
/// A single routing rule. Replaces the separate SuffixRule and RecipientFileRule types.
/// Match behaviour is determined by Mode; destination fields are common to all modes.
/// </summary>
public class RoutingRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public bool Enabled { get; set; } = true;

    // ── Match ─────────────────────────────────────────────────────────────────
    public MatchMode Mode { get; set; } = MatchMode.DomainSuffix;

    // DomainSuffix: subdomain segment + optional base domain
    public string Suffix { get; set; } = "";       // blank/"*" = wildcard capture
    public string BaseDomain { get; set; } = "";   // blank = any domain

    // ExactTo: exact address (case-insensitive). RegexTo/From/Subject: the regex pattern.
    public string Pattern { get; set; } = "";

    // Regex modes only
    public bool CaseInsensitive { get; set; } = true;

    // ── Destination ───────────────────────────────────────────────────────────
    public FileDestinationType DestinationType { get; set; } = FileDestinationType.EmailRelay;

    // EmailRelay: optional override mailbox — empty = passthrough
    public string RelayVia { get; set; } = "";

    // OneDrive: explicit user UPN — empty = resolve from matched To: address
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
    public bool? SaveEmbeddedImages { get; set; }   // null = use global default
    public string? FilenameTemplate { get; set; }
    public string? SubjectDelimiter { get; set; }
    public string? FilenameSpaceReplacement { get; set; }

    // Smarthost — only used when DestinationType = SmarthostRelay
    public bool UseGlobalSmarthost { get; set; } = true;
    public string SmarthostOverrideHost { get; set; } = "";
    public int SmarthostOverridePort { get; set; } = 587;
    public SmarthostTls SmarthostOverrideTls { get; set; } = SmarthostTls.StartTls;
    public string SmarthostOverrideUsername { get; set; } = "";
    public string SmarthostOverridePasswordEncrypted { get; set; } = "";
    [JsonIgnore] public string SmarthostOverridePassword { get; set; } = "";

    // Delivery address control — EmailRelay and SmarthostRelay destinations
    public bool StripSuffixFromTo { get; set; } = false;  // DomainSuffix only: strip suffix segment
    public string DeliverToOverride { get; set; } = "";    // explicit delivery address; overrides strip
    public bool RewriteToHeader { get; set; } = false;     // SmarthostRelay only: rewrite embedded To: header
}

// ── Legacy rule models (migration read only — not used by new code) ────────────

/// <summary>Retained for deserialising config files written by v0.1.3 and earlier.</summary>
public class SuffixRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Suffix { get; set; } = "";
    public string BaseDomain { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public FileDestinationType DestinationType { get; set; } = FileDestinationType.OneDrive;
    public string OneDriveUser { get; set; } = "";
    public string SiteUrl { get; set; } = "";
    public string SiteId { get; set; } = "";
    public string LibraryName { get; set; } = "";
    public string LibraryDriveId { get; set; } = "";
    public string FolderPath { get; set; } = "/%toupn%";
    public bool? UsePerEmailSubfolder { get; set; }
    public SaveWhat? SaveWhat { get; set; }
    public NoAttachmentBehavior? NoAttachmentBehavior { get; set; }
    public FromSenderHandling FromSenderHandling { get; set; } = FromSenderHandling.Ignore;
    public string? FilenameTemplate { get; set; }
    public string? SubjectDelimiter { get; set; }
    public string? FilenameSpaceReplacement { get; set; }
    public bool UseGlobalSmarthost { get; set; } = true;
    public string SmarthostOverrideHost { get; set; } = "";
    public int SmarthostOverridePort { get; set; } = 587;
    public SmarthostTls SmarthostOverrideTls { get; set; } = SmarthostTls.StartTls;
    public string SmarthostOverrideUsername { get; set; } = "";
    public string SmarthostOverridePasswordEncrypted { get; set; } = "";
    [JsonIgnore] public string SmarthostOverridePassword { get; set; } = "";
    public bool StripSuffixFromTo { get; set; } = false;
    public string DeliverToOverride { get; set; } = "";
    public bool RewriteToHeader { get; set; } = false;
}

/// <summary>Retained for deserialising config files written by v0.1.3 and earlier.</summary>
public class RecipientFileRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string ToAddress { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public FileDestinationType DestinationType { get; set; } = FileDestinationType.EmailRelay;
    public string RelayVia { get; set; } = "";
    public string OneDriveUser { get; set; } = "";
    public string SiteUrl { get; set; } = "";
    public string SiteId { get; set; } = "";
    public string LibraryName { get; set; } = "";
    public string LibraryDriveId { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public bool? UsePerEmailSubfolder { get; set; }
    public SaveWhat? SaveWhat { get; set; }
    public NoAttachmentBehavior? NoAttachmentBehavior { get; set; }
    public FromSenderHandling FromSenderHandling { get; set; } = FromSenderHandling.Ignore;
    public string? FilenameTemplate { get; set; }
    public string? SubjectDelimiter { get; set; }
    public string? FilenameSpaceReplacement { get; set; }
    public bool UseGlobalSmarthost { get; set; } = true;
    public string SmarthostOverrideHost { get; set; } = "";
    public int SmarthostOverridePort { get; set; } = 587;
    public SmarthostTls SmarthostOverrideTls { get; set; } = SmarthostTls.StartTls;
    public string SmarthostOverrideUsername { get; set; } = "";
    public string SmarthostOverridePasswordEncrypted { get; set; } = "";
    [JsonIgnore] public string SmarthostOverridePassword { get; set; } = "";
    public string DeliverToOverride { get; set; } = "";
    public bool RewriteToHeader { get; set; } = false;
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
    public bool SaveEmbeddedImages { get; init; } = false;

    // Path/filename variable context
    public string MatchedToAddress { get; init; } = "";   // To: address that matched
    public string FilenameTemplate { get; init; } = "";
    public string SubjectDelimiter { get; init; } = " ";
    public string FilenameSpaceReplacement { get; init; } = "";
    public string ToBaseDomain { get; init; } = "";       // DomainSuffix only
    public string CapturedSuffix { get; init; } = "";     // DomainSuffix only

    // Regex capture groups — named groups keyed by name, numbered groups keyed as "match1", "match2" etc.
    public Dictionary<string, string> RegexCaptures { get; init; } = new();

    // Smarthost — null = use global config; populated for SmarthostRelay rules with override
    public SmarthostConfig? SmarthostOverride { get; init; }

    // Delivery address override
    public string? DeliveryToAddress { get; init; }
    public bool RewriteToHeader { get; init; }

    // Diagnostics
    public string MatchSource { get; init; } = "";

    public static RouteDecision Unrouted(string reason = "") =>
        new() { IsUnrouted = true, MatchSource = $"Unrouted:{reason}" };

    public static RouteDecision Reject() =>
        new() { IsReject = true, MatchSource = "Reject" };
}
