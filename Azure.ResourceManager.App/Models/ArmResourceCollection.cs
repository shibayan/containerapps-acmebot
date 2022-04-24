using System.Collections.Generic;

using Newtonsoft.Json;

namespace Azure.ResourceManager.App.Models;

public class ArmResourceCollection<T>
{
    [JsonProperty("value")]
    public IReadOnlyList<T> Value { get; set; }
}
