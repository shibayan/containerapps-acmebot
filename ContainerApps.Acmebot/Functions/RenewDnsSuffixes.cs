using System;
using System.Threading;
using System.Threading.Tasks;

using DurableTask.TypedProxy;

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;

namespace ContainerApps.Acmebot.Functions;

public class RenewDnsSuffixes
{
    [FunctionName($"{nameof(RenewDnsSuffixes)}_{nameof(Orchestrator)}")]
    public async Task Orchestrator([OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
    {
        var activity = context.CreateActivityProxy<ISharedActivity>();

        // スロットリング対策として 600 秒以内でジッターを追加する
        var jitter = (uint)context.NewGuid().GetHashCode() % 600;

        await context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(jitter), CancellationToken.None);

        // Container Apps Environment 単位で証明書の更新を行う
        var managedEnvironments = await activity.GetExpiringManagedEnvironments(context.CurrentUtcDateTime);

        foreach (var managedEnvironment in managedEnvironments)
        {
            log.LogInformation($"Managed environment = {managedEnvironment.Id}");

            log.LogInformation($"Renew certificate = {managedEnvironment.DnsSuffix}, {managedEnvironment.ExpireOn}");

            // DNS サフィックス用のワイルドカード証明書を作成する
            var asciiDnsNames = new[] { $"*.{managedEnvironment.DnsSuffix}" };

            // ACME で証明書を発行する
            var (pfxBlob, password) = await context.CallSubOrchestratorWithRetryAsync<(byte[], string)>(nameof(SharedOrchestrator.IssueCertificate), _retryOptions, asciiDnsNames);

            // 検証用の DNS レコードを作成
            await activity.CreateDnsSuffixVerification((managedEnvironment.Id, managedEnvironment.DnsSuffix));

            // DNS サフィックスを更新する
            await activity.BindDnsSuffix((managedEnvironment.Id, managedEnvironment.DnsSuffix, pfxBlob, password));
        }
    }

    [FunctionName(nameof(RenewDnsSuffixes) + "_" + nameof(Timer))]
    public async Task Timer([TimerTrigger("0 0 0 * * *")] TimerInfo timer, [DurableClient] IDurableClient starter, ILogger log)
    {
        // Function input comes from the request content.
        var instanceId = await starter.StartNewAsync($"{nameof(RenewDnsSuffixes)}_{nameof(Orchestrator)}");

        log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
    }

    private readonly RetryOptions _retryOptions = new(TimeSpan.FromHours(3), 2)
    {
        Handle = ex => ex.InnerException?.InnerException is RetriableOrchestratorException
    };
}
