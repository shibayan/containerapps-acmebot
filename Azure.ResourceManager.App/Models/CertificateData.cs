using System;

using Newtonsoft.Json;

namespace Azure.ResourceManager.App.Models;

public class CertificateData
{
    [JsonProperty("subjectName", NullValueHandling = NullValueHandling.Ignore)]
    public string SubjectName { get; set; }

    [JsonProperty("issuer", NullValueHandling = NullValueHandling.Ignore)]
    public string Issuer { get; set; }

    [JsonProperty("issueDate", NullValueHandling = NullValueHandling.Ignore)]
    public DateTimeOffset? IssueDate { get; set; }

    [JsonProperty("expirationDate", NullValueHandling = NullValueHandling.Ignore)]
    public DateTimeOffset? ExpirationDate { get; set; }

    [JsonProperty("thumbprint", NullValueHandling = NullValueHandling.Ignore)]
    public string Thumbprint { get; set; }

    [JsonProperty("valid", NullValueHandling = NullValueHandling.Ignore)]
    public bool? Valid { get; set; }

    [JsonProperty("publicKeyHash", NullValueHandling = NullValueHandling.Ignore)]
    public string PublicKeyHash { get; set; }

    [JsonProperty("provisioningState", NullValueHandling = NullValueHandling.Ignore)]
    public string ProvisioningState { get; set; }

    [JsonProperty("password", NullValueHandling = NullValueHandling.Ignore)]
    public string Password { get; set; }

    [JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)]
    public string Value { get; set; }
}
