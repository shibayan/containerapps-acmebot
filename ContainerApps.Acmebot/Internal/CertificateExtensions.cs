using System;

using Azure.ResourceManager.AppContainers;

namespace ContainerApps.Acmebot.Internal;

internal static class CertificateExtensions
{
    public static bool TagsFilter(this ContainerAppCertificateData containerAppCertificate, string issuer, Uri endpoint)
    {
        var tags = containerAppCertificate.Tags;

        if (tags is null)
        {
            return false;
        }

        if (!tags.TryGetValue("Issuer", out var tagIssuer) || tagIssuer != issuer)
        {
            return false;
        }

        if (!tags.TryGetValue("Endpoint", out var tagEndpoint) || NormalizeEndpoint(tagEndpoint) != endpoint.Host)
        {
            return false;
        }

        if (!tags.ContainsKey("DnsNames"))
        {
            return false;
        }

        return true;
    }

    public static bool TagsFilter(this ContainerAppManagedEnvironmentData managedEnvironmentData, string issuer, Uri endpoint)
    {
        var tags = managedEnvironmentData.Tags;

        if (tags is null)
        {
            return false;
        }

        if (!tags.TryGetValue("Issuer", out var tagIssuer) || tagIssuer != issuer)
        {
            return false;
        }

        if (!tags.TryGetValue("Endpoint", out var tagEndpoint) || NormalizeEndpoint(tagEndpoint) != endpoint.Host)
        {
            return false;
        }

        return true;
    }

    private static string NormalizeEndpoint(string endpoint) => Uri.TryCreate(endpoint, UriKind.Absolute, out var legacyEndpoint) ? legacyEndpoint.Host : endpoint;
}
