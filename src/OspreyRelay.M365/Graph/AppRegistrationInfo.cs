namespace OspreyRelay.M365.Graph;

public class AppRegistrationInfo
{
    public string ObjectId { get; set; } = "";
    public string AppId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ServicePrincipalId { get; set; } = "";
    public DateTime? CreatedDateTime { get; set; }
    public DateTime? SecretExpiry { get; set; }

    /// <summary>Populated only after Create or RegenerateSecret — never stored.</summary>
    public string ClientSecret { get; set; } = "";
}
