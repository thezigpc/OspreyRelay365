using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Relay365.Core.Config;

public class ConfigManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "365Relay");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public RelayConfig Config { get; private set; } = new();

    /// <summary>
    /// True when Load() automatically migrated legacy SuffixRules/FileRules into the unified Rules list.
    /// Cleared on next Load(). Used to trigger the one-time migration alert in the UI.
    /// </summary>
    public bool MigrationPerformed { get; private set; }

    public void Load()
    {
        MigrationPerformed = false;

        if (!File.Exists(ConfigPath))
        {
            Config = new RelayConfig();
            return;
        }

        var json = File.ReadAllText(ConfigPath);
        Config = JsonSerializer.Deserialize<RelayConfig>(json, JsonOptions) ?? new RelayConfig();

        // Decrypt secrets
        if (!string.IsNullOrEmpty(Config.ClientSecretEncrypted))
            Config.ClientSecret = Decrypt(Config.ClientSecretEncrypted);

        if (!string.IsNullOrEmpty(Config.SmtpPasswordEncrypted))
            Config.SmtpPassword = Decrypt(Config.SmtpPasswordEncrypted);

        if (!string.IsNullOrEmpty(Config.SmarthostPasswordEncrypted))
            Config.SmarthostPassword = Decrypt(Config.SmarthostPasswordEncrypted);

        if (!string.IsNullOrEmpty(Config.FtpCertificatePasswordEncrypted))
            Config.FtpCertificatePassword = Decrypt(Config.FtpCertificatePasswordEncrypted);

        foreach (var user in Config.FtpUsers)
            if (!string.IsNullOrEmpty(user.PasswordEncrypted))
                user.Password = Decrypt(user.PasswordEncrypted);

        foreach (var rule in Config.Rules)
            if (!string.IsNullOrEmpty(rule.SmarthostOverridePasswordEncrypted))
                rule.SmarthostOverridePassword = Decrypt(rule.SmarthostOverridePasswordEncrypted);

        // Legacy decryption — only needed if migration below runs
        foreach (var rule in Config.SuffixRules)
            if (!string.IsNullOrEmpty(rule.SmarthostOverridePasswordEncrypted))
                rule.SmarthostOverridePassword = Decrypt(rule.SmarthostOverridePasswordEncrypted);

        foreach (var rule in Config.FileRules)
            if (!string.IsNullOrEmpty(rule.SmarthostOverridePasswordEncrypted))
                rule.SmarthostOverridePassword = Decrypt(rule.SmarthostOverridePasswordEncrypted);

        // Migrate legacy rules if this is a pre-v0.1.4 config
        if ((Config.SuffixRules.Count > 0 || Config.FileRules.Count > 0) && Config.Rules.Count == 0)
        {
            MigrateFromLegacy(Config);
            MigrationPerformed = true;
            // Persist immediately so the old keys are cleared from the file
            Save(Config);
        }
    }

    public void Save(RelayConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        Config = config;

        if (!string.IsNullOrEmpty(config.ClientSecret))
            config.ClientSecretEncrypted = Encrypt(config.ClientSecret);

        if (!string.IsNullOrEmpty(config.SmtpPassword))
            config.SmtpPasswordEncrypted = Encrypt(config.SmtpPassword);

        if (!string.IsNullOrEmpty(config.SmarthostPassword))
            config.SmarthostPasswordEncrypted = Encrypt(config.SmarthostPassword);

        if (!string.IsNullOrEmpty(config.FtpCertificatePassword))
            config.FtpCertificatePasswordEncrypted = Encrypt(config.FtpCertificatePassword);

        foreach (var user in config.FtpUsers)
            if (!string.IsNullOrEmpty(user.Password))
                user.PasswordEncrypted = Encrypt(user.Password);

        foreach (var rule in config.Rules)
            if (!string.IsNullOrEmpty(rule.SmarthostOverridePassword))
                rule.SmarthostOverridePasswordEncrypted = Encrypt(rule.SmarthostOverridePassword);

        // Clear legacy lists so they aren't written back to the file
        config.SuffixRules = new();
        config.FileRules = new();

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    public static string GetLogPath() => Path.Combine(ConfigDir, "relay.log");
    public static string GetConfigDir() => ConfigDir;

    // ── Migration ─────────────────────────────────────────────────────────────

    private static void MigrateFromLegacy(RelayConfig config)
    {
        var rules = new List<RoutingRule>();

        // ExactTo rules first — they had implicit priority over suffix rules in the old UI
        foreach (var old in config.FileRules)
        {
            rules.Add(new RoutingRule
            {
                Id              = old.Id,
                Enabled         = old.Enabled,
                Mode            = MatchMode.ExactTo,
                Pattern         = old.ToAddress,
                DestinationType = old.DestinationType,
                RelayVia        = old.RelayVia,
                OneDriveUser    = old.OneDriveUser,
                SiteUrl         = old.SiteUrl,
                SiteId          = old.SiteId,
                LibraryName     = old.LibraryName,
                LibraryDriveId  = old.LibraryDriveId,
                FolderPath      = old.FolderPath,
                UsePerEmailSubfolder      = old.UsePerEmailSubfolder,
                SaveWhat                  = old.SaveWhat,
                NoAttachmentBehavior      = old.NoAttachmentBehavior,
                FromSenderHandling        = old.FromSenderHandling,
                FilenameTemplate          = old.FilenameTemplate,
                SubjectDelimiter          = old.SubjectDelimiter,
                FilenameSpaceReplacement  = old.FilenameSpaceReplacement,
                UseGlobalSmarthost        = old.UseGlobalSmarthost,
                SmarthostOverrideHost     = old.SmarthostOverrideHost,
                SmarthostOverridePort     = old.SmarthostOverridePort,
                SmarthostOverrideTls      = old.SmarthostOverrideTls,
                SmarthostOverrideUsername = old.SmarthostOverrideUsername,
                SmarthostOverridePassword = old.SmarthostOverridePassword,
                DeliverToOverride         = old.DeliverToOverride,
                RewriteToHeader           = old.RewriteToHeader,
            });
        }

        // DomainSuffix rules second
        foreach (var old in config.SuffixRules)
        {
            rules.Add(new RoutingRule
            {
                Id              = old.Id,
                Enabled         = old.Enabled,
                Mode            = MatchMode.DomainSuffix,
                Suffix          = old.Suffix,
                BaseDomain      = old.BaseDomain,
                DestinationType = old.DestinationType,
                OneDriveUser    = old.OneDriveUser,
                SiteUrl         = old.SiteUrl,
                SiteId          = old.SiteId,
                LibraryName     = old.LibraryName,
                LibraryDriveId  = old.LibraryDriveId,
                FolderPath      = old.FolderPath,
                UsePerEmailSubfolder      = old.UsePerEmailSubfolder,
                SaveWhat                  = old.SaveWhat,
                NoAttachmentBehavior      = old.NoAttachmentBehavior,
                FromSenderHandling        = old.FromSenderHandling,
                FilenameTemplate          = old.FilenameTemplate,
                SubjectDelimiter          = old.SubjectDelimiter,
                FilenameSpaceReplacement  = old.FilenameSpaceReplacement,
                UseGlobalSmarthost        = old.UseGlobalSmarthost,
                SmarthostOverrideHost     = old.SmarthostOverrideHost,
                SmarthostOverridePort     = old.SmarthostOverridePort,
                SmarthostOverrideTls      = old.SmarthostOverrideTls,
                SmarthostOverrideUsername = old.SmarthostOverrideUsername,
                SmarthostOverridePassword = old.SmarthostOverridePassword,
                StripSuffixFromTo         = old.StripSuffixFromTo,
                DeliverToOverride         = old.DeliverToOverride,
                RewriteToHeader           = old.RewriteToHeader,
            });
        }

        config.Rules = rules;
    }

    // ── Crypto ────────────────────────────────────────────────────────────────

    private static string Encrypt(string plainText)
    {
        var data = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.LocalMachine);
        return Convert.ToBase64String(encrypted);
    }

    private static string Decrypt(string cipherText)
    {
        try
        {
            var data = Convert.FromBase64String(cipherText);
            var decrypted = ProtectedData.Unprotect(data, null, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch { return ""; }
    }
}
