using Newtonsoft.Json;

namespace Azure.ResourceManager.App.Models;

public class CustomHostNameAnalysisResult
{
    [JsonProperty("isHostnameAlreadyVerified")]
    public bool IsHostnameAlreadyVerified { get; set; }

    [JsonProperty("customDomainVerificationTest")]
    public string CustomDomainVerificationTest { get; set; }

    [JsonProperty("customDomainVerificationFailureInfo")]
    public CustomDomainVerificationFailureInfo CustomDomainVerificationFailureInfo { get; set; }

    [JsonProperty("hasConflictOnManagedEnvironment")]
    public bool HasConflictOnManagedEnvironment { get; set; }
}

public class CustomDomainVerificationFailureInfo
{
    [JsonProperty("code")]
    public string Code { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }
}
