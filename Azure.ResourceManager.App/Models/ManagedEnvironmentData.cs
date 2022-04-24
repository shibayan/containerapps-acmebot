using Newtonsoft.Json;

namespace Azure.ResourceManager.App.Models;

public class ManagedEnvironmentData
{
    [JsonProperty("provisioningState", NullValueHandling = NullValueHandling.Ignore)]
    public string ProvisioningState { get; set; }

    [JsonProperty("defaultDomain", NullValueHandling = NullValueHandling.Ignore)]
    public string DefaultDomain { get; set; }

    [JsonProperty("staticIp", NullValueHandling = NullValueHandling.Ignore)]
    public string StaticIp { get; set; }
}
