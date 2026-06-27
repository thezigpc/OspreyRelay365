using System.Text.Json.Serialization;

namespace OspreyRelay.Core.Config;

public class RelayConfig
{
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecretEncrypted { get; set; } = "";
    public string FallbackSenderEmail { get; set; } = "";
    public int RelayPort { get; set; } = 25;
    public string BindAddress { get; set; } = "0.0.0.0";
    public bool RequireSmtpAuth { get; set; } = false;
    public string SmtpUsername { get; set; } = "";
    public string SmtpPasswordEncrypted { get; set; } = "";
    public bool SaveToSentItems { get; set; } = false;
    public string AppRegistrationName { get; set; } = "";
    public string AppRegistrationObjectId { get; set; } = "";
    public int MaxMessageSizeMb { get; set; } = 25;

    /// <summary>Legacy From:→mailbox overrides (EmailRelay mode). New rules use FileRules instead.</summary>
    public Dictionary<string, string> SenderRoutes { get; set; } = new();

    // ── Mode & routing ────────────────────────────────────────────────────────
    public RelayMode GlobalMode { get; set; } = RelayMode.EmailRelay;
    public NoMatchBehavior NoMatchBehavior { get; set; } = NoMatchBehavior.Relay;

    // ── Global file-storage defaults (all overridable per rule) ───────────────
    public bool CreateMissingFolders { get; set; } = true;
    public FileConflictBehavior FileConflictBehavior { get; set; } = FileConflictBehavior.Rename;
    public SaveWhat DefaultSaveWhat { get; set; } = SaveWhat.AttachmentsOnly;
    public NoAttachmentBehavior DefaultNoAttachmentBehavior { get; set; } = NoAttachmentBehavior.SaveAsEml;
    public bool DefaultUsePerEmailSubfolder { get; set; } = false;
    public FromSenderHandling DefaultFromSenderHandling { get; set; } = FromSenderHandling.Ignore;
    public bool DefaultSaveEmbeddedImages { get; set; } = false;

    // ── Global catch-all file destination (when no rule matches in FileStorage/Hybrid) ──
    public string GlobalCatchAllOneDriveUser { get; set; } = "";
    public string GlobalCatchAllFolderPath { get; set; } = "/EmailRelay";

    // ── Global path/filename variable defaults ────────────────────────────────
    /// <summary>Template for renaming attachment files. Blank = keep original filename.</summary>
    public string DefaultFilenameTemplate { get; set; } = "";
    /// <summary>Character(s) used to split subject for %subject[n]% and %subject[*]%.</summary>
    public string DefaultSubjectDelimiter { get; set; } = " ";
    /// <summary>Replace spaces in final filename with this character. Blank = keep spaces.</summary>
    public string FilenameSpaceReplacement { get; set; } = "";

    // ── Unrouted handling ─────────────────────────────────────────────────────
    public UnroutedAction UnroutedAction { get; set; } = UnroutedAction.LocalFolder;
    public string UnroutedOneDriveUser { get; set; } = "";
    public string UnroutedOneDrivePath { get; set; } = "/Apps/FileRelay/Unrouted";
    public string UnroutedAlertEmail { get; set; } = "";

    // Unrouted → SharePoint
    public string UnroutedSharePointSiteUrl { get; set; } = "";
    public string UnroutedSharePointSiteId { get; set; } = "";
    public string UnroutedSharePointLibraryName { get; set; } = "";
    public string UnroutedSharePointDriveId { get; set; } = "";
    public string UnroutedSharePointFolderPath { get; set; } = "/Apps/FileRelay/Unrouted";

    // ── Local unrouted folder ─────────────────────────────────────────────────
    /// <summary>Override path for the local unrouted safety-net folder. Blank = default (config dir/unrouted).</summary>
    public string UnroutedLocalPath { get; set; } = "";
    /// <summary>Auto-delete local unrouted files older than this many days. 0 = never purge.</summary>
    public int UnroutedLocalRetentionDays { get; set; } = 30;

    // ── Routing rule tables ───────────────────────────────────────────────────
    /// <summary>Unified routing rules (v0.1.4+). Evaluated top-to-bottom; first match wins.</summary>
    public List<RoutingRule> Rules { get; set; } = new();

    /// <summary>Legacy — read only for migration from v0.1.3 and earlier configs.</summary>
    public List<SuffixRule> SuffixRules { get; set; } = new();
    /// <summary>Legacy — read only for migration from v0.1.3 and earlier configs.</summary>
    public List<RecipientFileRule> FileRules { get; set; } = new();

    // ── FTP bridge ────────────────────────────────────────────────────────────
    public bool FtpEnabled { get; set; } = false;
    /// <summary>When true, any username and password is accepted — no user list required.</summary>
    public bool FtpAcceptAnyLogin { get; set; } = false;
    public int FtpPort { get; set; } = 2121;
    public string FtpBindAddress { get; set; } = "0.0.0.0";
    /// <summary>When true, FTPS (explicit TLS via AUTH TLS) is offered. Requires a certificate.</summary>
    public bool FtpTlsEnabled { get; set; } = false;
    public string FtpCertificatePath { get; set; } = "";
    public string FtpCertificatePasswordEncrypted { get; set; } = "";
    [JsonIgnore] public string FtpCertificatePassword { get; set; } = "";
    public int FtpPassivePortMin { get; set; } = 50000;
    public int FtpPassivePortMax { get; set; } = 50100;
    public List<FtpUserConfig> FtpUsers { get; set; } = new();
    public List<FtpRoutingRule> FtpRules { get; set; } = new();

    // ── Diagnostics ───────────────────────────────────────────────────────────
    /// <summary>When true, verbose debug entries are written to relay-debug.log and shown in the UI.</summary>
    public bool DebugMode { get; set; } = false;

    // ── Setup wizard ──────────────────────────────────────────────────────────
    /// <summary>Client ID of the setup/admin app used for the programmatic configuration path.</summary>
    public string SetupClientId { get; set; } = "";

    // ── Smarthost failover ────────────────────────────────────────────────────
    public bool SmarthostEnabled { get; set; } = false;
    public string SmarthostHost { get; set; } = "";
    public int SmarthostPort { get; set; } = 587;
    public SmarthostTls SmarthostTls { get; set; } = SmarthostTls.StartTls;
    public string SmarthostUsername { get; set; } = "";
    public string SmarthostPasswordEncrypted { get; set; } = "";
    /// <summary>When true, preserves the original envelope-from when relaying via smarthost. When false, substitutes FallbackSenderEmail.</summary>
    public bool SmarthostUseOriginalFrom { get; set; } = true;

    [JsonIgnore]
    public string ClientSecret { get; set; } = "";

    [JsonIgnore]
    public string SmtpPassword { get; set; } = "";

    [JsonIgnore]
    public string SmarthostPassword { get; set; } = "";

    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);
}

public enum SmarthostTls { None, StartTls, SslTls }
