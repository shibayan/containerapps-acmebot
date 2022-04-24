using System;
using System.Collections.Generic;

using Newtonsoft.Json;

namespace Azure.ResourceManager.App.Models;

public abstract class ArmResource<T>
{
    [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
    public string Id { get; set; }

    [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
    public string Name { get; set; }

    [JsonProperty("location", NullValueHandling = NullValueHandling.Ignore)]
    public string Location { get; set; }

    [JsonProperty("tags", NullValueHandling = NullValueHandling.Ignore)]
    public IDictionary<string, string> Tags { get; set; }

    [JsonProperty("properties", NullValueHandling = NullValueHandling.Ignore)]
    public T Data { get; set; }

    public string GetResourceGroup() => Id.Split('/', StringSplitOptions.RemoveEmptyEntries)[3];
}
