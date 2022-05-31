using System.Collections.Generic;
using System.Threading.Tasks;

using Azure.ResourceManager.Applications.Containers;
using Azure.ResourceManager.Dns;
using Azure.ResourceManager.Dns.Models;
using Azure.ResourceManager.Resources;

namespace ContainerApps.Acmebot.Internal;

internal static class AzureSdkExtensions
{
    public static async Task<IReadOnlyList<ManagedEnvironmentData>> ListAllManagedEnvironmentsAsync(this SubscriptionResource subscription)
    {
        var managedEnvironments = new List<ManagedEnvironmentData>();

        var result = subscription.GetManagedEnvironmentsAsync();

        await foreach (var managedEnvironment in result)
        {
            managedEnvironments.Add(managedEnvironment.Data);
        }

        return managedEnvironments;
    }

    public static async Task<IReadOnlyList<ContainerAppData>> ListAllContainerAppsAsync(this SubscriptionResource subscription)
    {
        var containerApps = new List<ContainerAppData>();

        var result = subscription.GetContainerAppsAsync();

        await foreach (var containerApp in result)
        {
            containerApps.Add(containerApp.Data);
        }

        return containerApps;
    }

    public static async Task<IReadOnlyList<ContainerAppCertificateData>> ListAllAsync(this ContainerAppCertificateCollection collection)
    {
        var containerAppCertificates = new List<ContainerAppCertificateData>();

        var result = collection.GetAllAsync();

        await foreach (var containerAppCertificate in result)
        {
            containerAppCertificates.Add(containerAppCertificate.Data);
        }

        return containerAppCertificates;
    }

    public static async Task<IReadOnlyList<Zone>> ListAllAsync(this ZonesOperations operations)
    {
        var zones = new List<Zone>();

        var result = operations.ListAsync();

        await foreach (var zone in result)
        {
            zones.Add(zone);
        }

        return zones;
    }

    public static async Task<RecordSet> GetOrDefaultAsync(this RecordSetsOperations operations, string resourceGroupName, string zoneName, string relativeRecordSetName, RecordType recordType)
    {
        try
        {
            return await operations.GetAsync(resourceGroupName, zoneName, relativeRecordSetName, recordType);
        }
        catch
        {
            return null;
        }
    }
}
