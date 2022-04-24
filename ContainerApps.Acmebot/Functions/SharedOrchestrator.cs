using System.Threading;
using System.Threading.Tasks;

using Azure.ResourceManager.App.Models;

using DurableTask.TypedProxy;

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;

namespace ContainerApps.Acmebot.Functions;

public class SharedOrchestrator
{
    [FunctionName(nameof(IssueCertificate))]
    public async Task<CertificateResource> IssueCertificate([OrchestrationTrigger] IDurableOrchestrationContext context)
    {
        var (managedEnvironmentId, dnsNames) = context.GetInput<(string, string[])>();

        var activity = context.CreateActivityProxy<ISharedActivity>();

        // 前提条件をチェック
        await activity.Dns01Precondition(dnsNames);

        // 新しく ACME Order を作成する
        var orderDetails = await activity.Order(dnsNames);

        // 既に確認済みの場合は Challenge をスキップする
        if (orderDetails.Payload.Status != "ready")
        {
            // ACME Challenge を実行
            var challengeResults = await activity.Dns01Authorization(orderDetails.Payload.Authorizations);

            // DNS レコードの変更が伝搬するまで 10 秒遅延させる
            await context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(10), CancellationToken.None);

            // Azure DNS で正しくレコードが引けるか確認
            await activity.CheckDnsChallenge(challengeResults);

            // ACME Answer を実行
            await activity.AnswerChallenges(challengeResults);

            // Order のステータスが ready になるまで 60 秒待機
            await activity.CheckIsReady((orderDetails, challengeResults));

            // 作成した DNS レコードを削除
            await activity.CleanupDnsChallenge(challengeResults);
        }

        // CSR を作成し Finalize を実行
        var (finalize, rsaParameters) = await activity.FinalizeOrder((dnsNames, orderDetails));

        // Finalize の時点でステータスが valid の時点はスキップ
        if (finalize.Payload.Status != "valid")
        {
            // Finalize 後のステータスが valid になるまで 60 秒待機
            finalize = await activity.CheckIsValid(finalize);
        }

        // 証明書をダウンロードし Container Apps Environment へアップロード
        var certificate = await activity.UploadCertificate((managedEnvironmentId, dnsNames, finalize, rsaParameters));

        return certificate;
    }

    [FunctionName(nameof(BindToContainerApp))]
    public async Task BindToContainerApp([OrchestrationTrigger] IDurableOrchestrationContext context)
    {
        var (containerAppId, certificateId, dnsNames) = context.GetInput<(string, string, string[])>();

        var activity = context.CreateActivityProxy<ISharedActivity>();

        // ドメイン所有確認用のレコードを作成する
        await activity.CreateDomainVerification((containerAppId, dnsNames));

        // DNS レコードの変更が伝搬するまで 10 秒遅延させる
        await context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(10), CancellationToken.None);

        // 検証が通るまで待機
        await activity.ValidateDomain((containerAppId, dnsNames));

        // Container App にカスタムドメイン設定を追加
        await activity.BindDomains((containerAppId, certificateId, dnsNames));
    }
}
