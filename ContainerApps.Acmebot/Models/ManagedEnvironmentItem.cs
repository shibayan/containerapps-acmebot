using Newtonsoft.Json;

namespace ContainerApps.Acmebot.Models;

public class ManagedEnvironmentItem
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("resourceGroup")]
    public string ResourceGroup { get; set; }
}
