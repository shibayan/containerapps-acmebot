using System.Collections.Generic;
using System.Threading.Tasks;

using Azure.ResourceManager.Dns;
using Azure.ResourceManager.Dns.Models;

namespace ContainerApps.Acmebot.Internal;

internal static class AzureSdkExtensions
{
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
