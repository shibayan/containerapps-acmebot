using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

using Newtonsoft.Json;

namespace ContainerApps.Acmebot.Models;

public class AddCertificateRequest : IValidatableObject
{
    [JsonProperty("dnsNames")]
    public string[] DnsNames { get; set; }

    [JsonProperty("managedEnvironmentId")]
    [Required]
    public string ManagedEnvironmentId { get; set; }

    [JsonProperty("bindToCustomDnsSuffix")]
    public bool BindToCustomDnsSuffix { get; set; }

    [JsonProperty("bindToContainerApp")]
    public bool BindToContainerApp { get; set; }

    [JsonProperty("containerAppId")]
    public string ContainerAppId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DnsNames is null || DnsNames.Length == 0)
        {
            yield return new ValidationResult($"The {nameof(DnsNames)} is required.", new[] { nameof(DnsNames) });
        }

        if (BindToCustomDnsSuffix && (DnsNames is null || DnsNames.Length != 1 || !DnsNames[0].StartsWith("*")))
        {
            yield return new ValidationResult("A single wildcard certificate is required.", new[] { nameof(DnsNames) });
        }

        if (BindToContainerApp && string.IsNullOrEmpty(ContainerAppId))
        {
            yield return new ValidationResult($"The {nameof(ContainerAppId)} is required.", new[] { nameof(ContainerAppId) });
        }
    }
}
