using Newtonsoft.Json;

namespace ContainerApps.Acmebot.Models;

public class ContainerAppItem
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }
}
