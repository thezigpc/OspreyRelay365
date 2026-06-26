using System.Text.Json.Serialization;

namespace Relay365.Core.Config;

// ── FTP user credentials ──────────────────────────────────────────────────────

public class FtpUserConfig
{
    public string Username { get; set; } = "";
    public string PasswordEncrypted { get; set; } = "";
    [JsonIgnore] public string Password { get; set; } = "";
    /// <summary>When true, any password is accepted — suitable for devices that can't store credentials cleanly.</summary>
    public bool AcceptAnyPassword { get; set; } = false;
    /// <summary>Optional label shown in the UI (e.g. "Finance copier").</summary>
    public string Notes { get; set; } = "";
}

// ── FTP routing rules ─────────────────────────────────────────────────────────

/// <summary>
/// Maps an FTP virtual path prefix (and optionally a specific username)
/// to a OneDrive or SharePoint destination.
/// </summary>
public class FtpRoutingRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public bool Enabled { get; set; } = true;
    /// <summary>Friendly name shown in the rules list.</summary>
    public string Name { get; set; } = "";

    // ── Match ─────────────────────────────────────────────────────────────────
    /// <summary>FTP virtual directory that triggers this rule (prefix match). "/" matches everything.</summary>
    public string VirtualPath { get; set; } = "/";
    /// <summary>FTP username that must match. Empty = any user.</summary>
    public string Username { get; set; } = "";

    // ── Destination ───────────────────────────────────────────────────────────
    public FileDestinationType DestinationType { get; set; } = FileDestinationType.OneDrive;

    /// <summary>UPN of the OneDrive user. Empty = try to resolve from the FTP username.</summary>
    public string OneDriveUser { get; set; } = "";

    // SharePoint
    public string SiteUrl { get; set; } = "";
    public string SiteId { get; set; } = "";
    public string LibraryName { get; set; } = "";
    public string LibraryDriveId { get; set; } = "";

    /// <summary>
    /// Folder path in the drive. Supports variables: %username%, %date%, %datetime%, %ftppath%.
    /// </summary>
    public string FolderPath { get; set; } = "/FtpRelay/%username%";

    /// <summary>Filename template. Supports %filename%, %date%, %datetime%, %username%. Null = keep original.</summary>
    public string? FilenameTemplate { get; set; }
}

// ── Runtime routing result ────────────────────────────────────────────────────

/// <summary>
/// The decision returned by FtpFileRouter.Resolve().
/// FtpSession dispatches based on IsUnrouted and destination fields.
/// </summary>
public class FtpRouteDecision
{
    public FileDestinationType DestinationType { get; init; } = FileDestinationType.OneDrive;

    /// <summary>OneDrive user UPN; null for SharePoint destinations.</summary>
    public string? OneDriveUser { get; init; }

    /// <summary>Pre-resolved SharePoint drive ID; null for OneDrive (resolved at upload time).</summary>
    public string? DriveId { get; init; }

    public string FolderPath { get; init; } = "/FtpRelay";
    public string? FilenameTemplate { get; init; }

    public bool IsUnrouted { get; init; }
    public string MatchSource { get; init; } = "";

    public static FtpRouteDecision Unrouted(string reason = "") =>
        new() { IsUnrouted = true, MatchSource = string.IsNullOrEmpty(reason) ? "Unrouted" : $"Unrouted:{reason}" };
}
