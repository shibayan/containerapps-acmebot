using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Azure.Core;
using Azure.ResourceManager.App.Models;

using Newtonsoft.Json;

namespace Azure.ResourceManager.App;

public class ContainerAppsManagementClient
{
    public ContainerAppsManagementClient(string subscriptionId, Uri endpoint, TokenCredential tokenCredential)
    {
        _subscriptionId = subscriptionId;

        _httpClient = new HttpClient(new AzureAuthHttpHandler(tokenCredential))
        {
            BaseAddress = endpoint
        };
    }

    private readonly string _subscriptionId;
    private readonly HttpClient _httpClient;

    private const string ApiVersion = "api-version=2022-03-01";

    public async Task<IReadOnlyList<ManagedEnvironmentResource>> GetManagedEnvironmentsAsync()
    {
        var json = await _httpClient.GetStringAsync($"/subscriptions/{_subscriptionId}/providers/Microsoft.App/managedEnvironments?{ApiVersion}");

        return JsonConvert.DeserializeObject<ArmResourceCollection<ManagedEnvironmentResource>>(json).Value;
    }

    public async Task<ManagedEnvironmentResource> GetManagedEnvironmentAsync(string managedEnvironmentId)
    {
        var json = await _httpClient.GetStringAsync($"{managedEnvironmentId}?{ApiVersion}");

        return JsonConvert.DeserializeObject<ManagedEnvironmentResource>(json);
    }

    public async Task<IReadOnlyList<ContainerAppResource>> GetContainerAppsAsync(string managedEnvironmentId)
    {
        var payload = new
        {
            query = $"where type =~ 'microsoft.app/containerapps' | where properties.managedEnvironmentId =~ '{managedEnvironmentId}' | project containerApp=pack('id', id, 'name', name, 'resourceGroup', resourceGroup, 'type', type, 'location', location, 'tags', tags, 'properties', properties)",
            subscriptions = new[]
            {
                _subscriptionId
            }
        };

        var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("/providers/Microsoft.ResourceGraph/resources?api-version=2021-03-01", content);

        var json = await response.Content.ReadAsStringAsync();

        return JsonConvert.DeserializeObject<ResourceGraphResult>(json).Data.Select(x => x["containerApp"].ToObject<ContainerAppResource>()).ToArray();
    }

    public async Task<ContainerAppResource> GetContainerAppAsync(string containerAppId)
    {
        var json = await _httpClient.GetStringAsync($"{containerAppId}?{ApiVersion}");

        return JsonConvert.DeserializeObject<ContainerAppResource>(json);
    }

    public async Task UpdateContainerAppAsync(ContainerAppResource containerApp)
    {
        var content = new StringContent(JsonConvert.SerializeObject(containerApp), Encoding.UTF8, "application/json");

        var response = await _httpClient.PatchAsync($"{containerApp.Id}?{ApiVersion}", content);

        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<CertificateResource>> GetCertificatesAsync(string managedEnvironmentId)
    {
        var json = await _httpClient.GetStringAsync($"{managedEnvironmentId}/certificates?{ApiVersion}");

        return JsonConvert.DeserializeObject<ArmResourceCollection<CertificateResource>>(json).Value;
    }

    public async Task<CertificateResource> CreateOrUpdateCertificateAsync(string managedEnvironmentId, string name, CertificateResource certificate)
    {
        var content = new StringContent(JsonConvert.SerializeObject(certificate), Encoding.UTF8, "application/json");

        var response = await _httpClient.PutAsync($"{managedEnvironmentId}/certificates/{name}?{ApiVersion}", content);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();

        return JsonConvert.DeserializeObject<CertificateResource>(json);
    }

    public async Task<CertificateResource> UpdateCertificateTagsAsync(CertificateResource certificate, IDictionary<string, string> tags)
    {
        var content = new StringContent(JsonConvert.SerializeObject(new { tags }), Encoding.UTF8, "application/json");

        var response = await _httpClient.PatchAsync($"{certificate.Id}?{ApiVersion}", content);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();

        return JsonConvert.DeserializeObject<CertificateResource>(json);
    }

    public async Task<CustomHostNameAnalysisResult> ListCustomHostNameAnalysisAsync(string containerAppId, string dnsName)
    {
        var response = await _httpClient.PostAsync($"{containerAppId}/listCustomHostNameAnalysis?{ApiVersion}&customHostname={WebUtility.UrlEncode(dnsName)}", null);

        var json = await response.Content.ReadAsStringAsync();

        return JsonConvert.DeserializeObject<CustomHostNameAnalysisResult>(json);
    }

    private class AzureAuthHttpHandler : DelegatingHandler
    {
        public AzureAuthHttpHandler(TokenCredential tokenCredential)
            : base(new HttpClientHandler())
        {
            _tokenCredential = tokenCredential;
        }

        private readonly TokenCredential _tokenCredential;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = await _tokenCredential.GetTokenAsync(new TokenRequestContext(new[] { "https://management.azure.com" }), cancellationToken);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
