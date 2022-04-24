using System;

using Newtonsoft.Json;

namespace Azure.ResourceManager.App.Models;

public class ContainerAppData
{
    [JsonProperty("provisioningState", NullValueHandling = NullValueHandling.Ignore)]
    public string ProvisioningState { get; set; }

    [JsonProperty("managedEnvironmentId", NullValueHandling = NullValueHandling.Ignore)]
    public string ManagedEnvironmentId { get; set; }

    [JsonProperty("customDomainVerificationId", NullValueHandling = NullValueHandling.Ignore)]
    public string CustomDomainVerificationId { get; set; }

    [JsonProperty("configuration", NullValueHandling = NullValueHandling.Ignore)]
    public Configuration Configuration { get; set; }
}

public class Configuration
{
    [JsonProperty("ingress")]
    public Ingress Ingress { get; set; }
}

public class Ingress
{
    [JsonProperty("fqdn", NullValueHandling = NullValueHandling.Ignore)]
    public string Fqdn { get; set; }

    [JsonProperty("customDomains", NullValueHandling = NullValueHandling.Ignore)]
    public CustomDomain[] CustomDomains { get; set; }
}

public class CustomDomain : IEquatable<CustomDomain>
{
    [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
    public string Name { get; set; }

    [JsonProperty("certificateId", NullValueHandling = NullValueHandling.Ignore)]
    public string CertificateId { get; set; }

    [JsonProperty("bindingType", NullValueHandling = NullValueHandling.Ignore)]
    public string BindingType { get; set; }

    public bool Equals(CustomDomain other)
    {
        if (other is null)
        {
            return false;
        }

        return Name == other.Name;
    }

    public override bool Equals(object obj) => Equals(obj as CustomDomain);

    public override int GetHashCode() => Name.GetHashCode();
}
