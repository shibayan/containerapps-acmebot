﻿using System;
using System.Collections.Generic;

using Newtonsoft.Json;

namespace ContainerApps.Acmebot.Models;

public class ContainerAppCertificateItem
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("expirationOn")]
    public DateTimeOffset ExpirationOn { get; set; }

    [JsonProperty("tags")]
    public IDictionary<string, string> Tags { get; set; }
}
