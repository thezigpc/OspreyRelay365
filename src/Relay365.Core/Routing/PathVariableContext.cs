namespace Relay365.Core.Routing;

/// <summary>
/// All runtime values available for variable substitution in folder paths and filename templates.
/// Populated by GraphFileStorer from the received email and route decision.
/// </summary>
public record PathVariableContext
{
    // ── From: side ────────────────────────────────────────────────────────────
    public string From       { get; init; } = "";   // full address
    public string FromUpn    { get; init; } = "";   // local part before @
    public string FromDomain { get; init; } = "";   // domain after @

    // ── To: side ─────────────────────────────────────────────────────────────
    public string To             { get; init; } = "";   // full matched To: address
    public string ToUpn          { get; init; } = "";   // local part before @
    public string ToDomain       { get; init; } = "";   // full domain after @
    public string ToBaseDomain   { get; init; } = "";   // BaseDomain from DomainSuffix rule; empty otherwise

    // ── Suffix routing ────────────────────────────────────────────────────────
    public string Suffix { get; init; } = "";   // matched/captured suffix segment

    // ── Email metadata ────────────────────────────────────────────────────────
    public string Subject  { get; init; } = "";   // raw subject (sanitized for paths)
    public string Date     { get; init; } = "";   // YYYY-MM-DD
    public string DateTime { get; init; } = "";   // YYYY-MM-DD_HHmmss

    // ── Regex capture groups ──────────────────────────────────────────────────
    // Named groups: key = group name. Numbered groups: key = "match1", "match2", etc.
    public Dictionary<string, string> RegexCaptures { get; init; } = new();

    // ── FTP context (populated by FtpSession; empty for email routes) ─────────
    public string Username { get; init; } = "";   // FTP login username → %username%
    public string FtpPath  { get; init; } = "";   // virtual working directory at STOR → %ftppath%
}
