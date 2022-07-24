using System;
using System.Threading;
using System.Threading.Tasks;

using DurableTask.TypedProxy;

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;

namespace ContainerApps.Acmebot.Functions;

public class RenewCertificates
{
    [FunctionName($"{nameof(RenewCertificates)}_{nameof(Orchestrator)}")]
    public async Task Orchestrator([OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
    {
        var activity = context.CreateActivityProxy<ISharedActivity>();

        // スロットリング対策として 600 秒以内でジッターを追加する
        var jitter = (uint)context.NewGuid().GetHashCode() % 600;

        await context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(jitter), CancellationToken.None);

        // Container Apps Environment 単位で証明書の更新を行う
        var managedEnvironments = await activity.GetManagedEnvironments();

        foreach (var managedEnvironment in managedEnvironments)
        {
            log.LogInformation($"Managed environment = {managedEnvironment.Id}");

            // 期限切れまで 30 日以内の証明書を取得する
            var certificates = await activity.GetExpiringCertificates((managedEnvironment.Id, context.CurrentUtcDateTime));

            // 更新対象となる証明書がない場合は終わる
            if (certificates.Count == 0)
            {
                log.LogInformation("Certificates are not found");

                continue;
            }

            foreach (var certificate in certificates)
            {
                try
                {
                    // 証明書のリソースタグから SANs 情報を取得する
                    var dnsNames = certificate.Tags["DnsNames"].Split(',');

                    log.LogInformation($"Renew certificate = {certificate.Name},{certificate.ExpirationOn},{string.Join(",", dnsNames)}");

                    // 証明書の更新処理を開始
                    await context.CallSubOrchestratorWithRetryAsync(nameof(SharedOrchestrator.IssueCertificate), _retryOptions, (managedEnvironment.Id, dnsNames));

                    // 証明書の更新が完了後に Webhook を送信する
                    await activity.SendCompletedEvent((managedEnvironment.Id, certificate.ExpirationOn, dnsNames));
                }
                catch (Exception ex)
                {
                    // 失敗した場合はログに詳細を書き出して続きを実行する
                    log.LogError($"Failed sub orchestration with Certificate = {certificate.Id}");
                    log.LogError(ex.Message);
                }
            }
        }
    }

    [FunctionName(nameof(RenewCertificates) + "_" + nameof(Timer))]
    public async Task Timer([TimerTrigger("0 0 0 * * *")] TimerInfo timer, [DurableClient] IDurableClient starter, ILogger log)
    {
        // Function input comes from the request content.
        var instanceId = await starter.StartNewAsync($"{nameof(RenewCertificates)}_{nameof(Orchestrator)}");

        log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
    }

    private readonly RetryOptions _retryOptions = new(TimeSpan.FromHours(3), 2)
    {
        Handle = ex => ex.InnerException?.InnerException is RetriableOrchestratorException
    };
}
