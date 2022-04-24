using Azure.ResourceManager.App.Models;

namespace ContainerApps.Acmebot.Internal;

internal static class CertificateExtensions
{
    public static bool TagsFilter(this CertificateResource certificate, string issuer, string endpoint)
    {
        var tags = certificate.Tags;

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
