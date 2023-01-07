using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

using Newtonsoft.Json;

namespace ContainerApps.Acmebot.Models;

public class AddDnsSuffixRequest : IValidatableObject
{
    [JsonProperty("dnsSuffix")]
    [Required]
    public string DnsSuffix { get; set; }

    [JsonProperty("managedEnvironmentId")]
    [Required]
    public string ManagedEnvironmentId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DnsSuffix.StartsWith("*"))
        {
            yield return new ValidationResult($"The {nameof(DnsSuffix)} is invalid.", new[] { nameof(DnsSuffix) });
        }
    }
}
