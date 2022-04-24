using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.ResourceManager.App.Models;

public class ResourceGraphResult
{
    [JsonProperty("totalRecords")]
    public int TotalRecords { get; set; }

    [JsonProperty("count")]
    public int Count { get; set; }

    [JsonProperty("data")]
    public JObject[] Data { get; set; }
}
