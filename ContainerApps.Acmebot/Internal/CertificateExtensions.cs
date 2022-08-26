using Azure.ResourceManager.AppContainers;

namespace ContainerApps.Acmebot.Internal;

internal static class CertificateExtensions
{
    public static bool TagsFilter(this ContainerAppCertificateData containerAppCertificate, string issuer, string endpoint)
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

        if (!tags.TryGetValue("Endpoint", out var tagEndpoint) || tagEndpoint != endpoint)
        {
            return false;
        }

        if (!tags.ContainsKey("DnsNames"))
        {
            return false;
        }

        return true;
    }
}
