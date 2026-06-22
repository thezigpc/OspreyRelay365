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

    public void Load()
    {
        if (!File.Exists(ConfigPath))
        {
            Config = new RelayConfig();
            return;
        }

        var json = File.ReadAllText(ConfigPath);
        Config = JsonSerializer.Deserialize<RelayConfig>(json, JsonOptions) ?? new RelayConfig();

        if (!string.IsNullOrEmpty(Config.ClientSecretEncrypted))
            Config.ClientSecret = Decrypt(Config.ClientSecretEncrypted);

        if (!string.IsNullOrEmpty(Config.SmtpPasswordEncrypted))
            Config.SmtpPassword = Decrypt(Config.SmtpPasswordEncrypted);

        if (!string.IsNullOrEmpty(Config.SmarthostPasswordEncrypted))
            Config.SmarthostPassword = Decrypt(Config.SmarthostPasswordEncrypted);

        foreach (var rule in Config.SuffixRules)
            if (!string.IsNullOrEmpty(rule.SmarthostOverridePasswordEncrypted))
                rule.SmarthostOverridePassword = Decrypt(rule.SmarthostOverridePasswordEncrypted);

        foreach (var rule in Config.FileRules)
            if (!string.IsNullOrEmpty(rule.SmarthostOverridePasswordEncrypted))
                rule.SmarthostOverridePassword = Decrypt(rule.SmarthostOverridePasswordEncrypted);
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

        foreach (var rule in config.SuffixRules)
            if (!string.IsNullOrEmpty(rule.SmarthostOverridePassword))
                rule.SmarthostOverridePasswordEncrypted = Encrypt(rule.SmarthostOverridePassword);

        foreach (var rule in config.FileRules)
            if (!string.IsNullOrEmpty(rule.SmarthostOverridePassword))
                rule.SmarthostOverridePasswordEncrypted = Encrypt(rule.SmarthostOverridePassword);

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    public static string GetLogPath() => Path.Combine(ConfigDir, "relay.log");
    public static string GetConfigDir() => ConfigDir;

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
