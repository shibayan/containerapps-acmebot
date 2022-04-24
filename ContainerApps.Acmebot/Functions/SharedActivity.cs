using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

using ACMESharp.Authorizations;
using ACMESharp.Crypto;
using ACMESharp.Protocol;
using ACMESharp.Protocol.Resources;

using Azure.ResourceManager.App;
using Azure.ResourceManager.App.Models;
using Azure.ResourceManager.Dns;
using Azure.ResourceManager.Dns.Models;

using ContainerApps.Acmebot.Internal;
using ContainerApps.Acmebot.Models;
using ContainerApps.Acmebot.Options;

using DnsClient;

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Newtonsoft.Json;

namespace ContainerApps.Acmebot.Functions;

public class SharedActivity : ISharedActivity
{
    public SharedActivity(AcmeProtocolClientFactory acmeProtocolClientFactory, LookupClient lookupClient,
                          ContainerAppsManagementClient containerAppsManagementClient, DnsManagementClient dnsManagementClient,
                          WebhookInvoker webhookInvoker, IOptions<AcmebotOptions> options, ILogger<SharedActivity> logger)
    {
        _acmeProtocolClientFactory = acmeProtocolClientFactory;
        _lookupClient = lookupClient;
        _containerAppsManagementClient = containerAppsManagementClient;
        _dnsManagementClient = dnsManagementClient;
        _webhookInvoker = webhookInvoker;
        _options = options.Value;
        _logger = logger;
    }

    private readonly AcmeProtocolClientFactory _acmeProtocolClientFactory;
    private readonly LookupClient _lookupClient;
    private readonly ContainerAppsManagementClient _containerAppsManagementClient;
    private readonly DnsManagementClient _dnsManagementClient;
    private readonly WebhookInvoker _webhookInvoker;
    private readonly AcmebotOptions _options;
    private readonly ILogger<SharedActivity> _logger;

    private const string IssuerName = "Acmebot";

    [FunctionName(nameof(GetManagedEnvironments))]
    public Task<IReadOnlyList<ManagedEnvironmentResource>> GetManagedEnvironments([ActivityTrigger] object input = null)
    {
        return _containerAppsManagementClient.GetManagedEnvironmentsAsync();
    }

    [FunctionName(nameof(GetContainerApps))]
    public Task<IReadOnlyList<ContainerAppResource>> GetContainerApps([ActivityTrigger] string managedEnvironmentId)
    {
        return _containerAppsManagementClient.GetContainerAppsAsync(managedEnvironmentId);
    }

    [FunctionName(nameof(GetExpiringCertificates))]
    public async Task<IReadOnlyList<CertificateResource>> GetExpiringCertificates([ActivityTrigger] (string, DateTime) input)
    {
        var (managedEnvironmentId, currentDateTime) = input;

        var certificates = await _containerAppsManagementClient.GetCertificatesAsync(managedEnvironmentId);

        return certificates.Where(x => x.TagsFilter(IssuerName, _options.Endpoint))
                           .Where(x => (x.Data.ExpirationDate.Value - currentDateTime).TotalDays <= _options.RenewBeforeExpiry)
                           .ToArray();
    }

    [FunctionName(nameof(GetZones))]
    public async Task<IReadOnlyList<string>> GetZones([ActivityTrigger] object input = null)
    {
        try
        {
            var zones = await _dnsManagementClient.Zones.ListAllAsync();

            return zones.Select(x => x.Name).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    [FunctionName(nameof(Order))]
    public async Task<OrderDetails> Order([ActivityTrigger] IReadOnlyList<string> dnsNames)
    {
        var acmeProtocolClient = await _acmeProtocolClientFactory.CreateClientAsync();

        return await acmeProtocolClient.CreateOrderAsync(dnsNames);
    }

    [FunctionName(nameof(Dns01Precondition))]
    public async Task Dns01Precondition([ActivityTrigger] IReadOnlyList<string> dnsNames)
    {
        // Azure DNS が存在するか確認
        var zones = await _dnsManagementClient.Zones.ListAllAsync();

        var foundZones = new HashSet<Zone>();
        var zoneNotFoundDnsNames = new List<string>();

        foreach (var dnsName in dnsNames)
        {
            var zone = zones.Where(x => string.Equals(dnsName, x.Name, StringComparison.OrdinalIgnoreCase) || dnsName.EndsWith($".{x.Name}", StringComparison.OrdinalIgnoreCase))
                            .MaxBy(x => x.Name.Length);

            // マッチする DNS zone が見つからない場合はエラー
            if (zone == null)
            {
                zoneNotFoundDnsNames.Add(dnsName);
                continue;
            }

            foundZones.Add(zone);
        }

        if (zoneNotFoundDnsNames.Count > 0)
        {
            throw new PreconditionException($"DNS zone(s) are not found. DnsNames = {string.Join(",", zoneNotFoundDnsNames)}");
        }

        // DNS zone に移譲されている Name servers が正しいか検証
        foreach (var zone in foundZones)
        {
            // DNS provider が Name servers を返していなければスキップ
            if (zone.NameServers == null || zone.NameServers.Count == 0)
            {
                continue;
            }

            // DNS provider が Name servers を返している場合は NS レコードを確認
            var queryResult = await _lookupClient.QueryAsync(zone.Name, QueryType.NS);

            // 最後の . が付いている場合があるので削除して統一
            var expectedNameServers = zone.NameServers
                                          .Select(x => x.TrimEnd('.'))
                                          .ToArray();

            var actualNameServers = queryResult.Answers
                                               .OfType<DnsClient.Protocol.NsRecord>()
                                               .Select(x => x.NSDName.Value.TrimEnd('.'))
                                               .ToArray();

            // 処理対象の DNS zone から取得した NS と実際に引いた NS の値が一つも一致しない場合はエラー
            if (!actualNameServers.Intersect(expectedNameServers, StringComparer.OrdinalIgnoreCase).Any())
            {
                throw new PreconditionException($"The delegated name server is not correct. DNS zone = {zone.Name}, Expected = {string.Join(",", expectedNameServers)}, Actual = {string.Join(",", actualNameServers)}");
            }
        }
    }

    [FunctionName(nameof(Dns01Authorization))]
    public async Task<IReadOnlyList<AcmeChallengeResult>> Dns01Authorization([ActivityTrigger] IReadOnlyList<string> authorizationUrls)
    {
        var acmeProtocolClient = await _acmeProtocolClientFactory.CreateClientAsync();

        var challengeResults = new List<AcmeChallengeResult>();

        foreach (var authorizationUrl in authorizationUrls)
        {
            // Authorization の詳細を取得
            var authorization = await acmeProtocolClient.GetAuthorizationDetailsAsync(authorizationUrl);

            // DNS-01 Challenge の情報を拾う
            var challenge = authorization.Challenges.FirstOrDefault(x => x.Type == "dns-01");

            if (challenge == null)
            {
                throw new InvalidOperationException("Simultaneous use of HTTP-01 and DNS-01 for authentication is not allowed.");
            }

            var challengeValidationDetails = AuthorizationDecoder.ResolveChallengeForDns01(authorization, challenge, acmeProtocolClient.Signer);

            // Challenge の情報を保存する
            challengeResults.Add(new AcmeChallengeResult
            {
                Url = challenge.Url,
                DnsRecordName = challengeValidationDetails.DnsRecordName,
                DnsRecordValue = challengeValidationDetails.DnsRecordValue
            });
        }

        // Azure DNS zone の一覧を取得する
        var zones = await _dnsManagementClient.Zones.ListAllAsync();

        // DNS-01 の検証レコード名毎に Azure DNS に TXT レコードを作成
        foreach (var lookup in challengeResults.ToLookup(x => x.DnsRecordName))
        {
            var dnsRecordName = lookup.Key;

            var zone = zones.Where(x => dnsRecordName.EndsWith($".{x.Name}", StringComparison.OrdinalIgnoreCase))
                            .MaxBy(x => x.Name.Length);

            var resourceGroup = ExtractResourceGroup(zone.Id);

            // Challenge の詳細から Azure DNS 向けにレコード名を作成
            var acmeDnsRecordName = dnsRecordName.Replace($".{zone.Name}", "", StringComparison.OrdinalIgnoreCase);

            // 既存の TXT レコードがあれば取得する
            var recordSet = await _dnsManagementClient.RecordSets.GetOrDefaultAsync(resourceGroup, zone.Name, acmeDnsRecordName, RecordType.TXT) ?? new RecordSet();

            // TXT レコードに TTL と値をセットする
            recordSet.TTL = 60;

            foreach (var value in lookup)
            {
                recordSet.TxtRecords.Add(new TxtRecord { Value = { value.DnsRecordValue } });
            }

            await _dnsManagementClient.RecordSets.CreateOrUpdateAsync(resourceGroup, zone.Name, acmeDnsRecordName, RecordType.TXT, recordSet);
        }

        return challengeResults;
    }

    [FunctionName(nameof(CheckDnsChallenge))]
    public async Task CheckDnsChallenge([ActivityTrigger] IReadOnlyList<AcmeChallengeResult> challengeResults)
    {
        foreach (var challengeResult in challengeResults)
        {
            IDnsQueryResponse queryResult;

            try
            {
                // 実際に ACME の TXT レコードを引いて確認する
                queryResult = await _lookupClient.QueryAsync(challengeResult.DnsRecordName, QueryType.TXT);
            }
            catch (DnsResponseException ex)
            {
                // 一時的な DNS エラーの可能性があるためリトライ
                throw new RetriableActivityException($"{challengeResult.DnsRecordName} bad response. Message: \"{ex.DnsError}\"", ex);
            }

            var txtRecords = queryResult.Answers
                                        .OfType<DnsClient.Protocol.TxtRecord>()
                                        .ToArray();

            // レコードが存在しなかった場合はエラー
            if (txtRecords.Length == 0)
            {
                throw new RetriableActivityException($"{challengeResult.DnsRecordName} did not resolve.");
            }

            // レコードに今回のチャレンジが含まれていない場合もエラー
            if (!txtRecords.Any(x => x.Text.Contains(challengeResult.DnsRecordValue)))
            {
                throw new RetriableActivityException($"{challengeResult.DnsRecordName} is not correct. Expected: \"{challengeResult.DnsRecordValue}\", Actual: \"{string.Join(",", txtRecords.SelectMany(x => x.Text))}\"");
            }
        }
    }

    [FunctionName(nameof(AnswerChallenges))]
    public async Task AnswerChallenges([ActivityTrigger] IReadOnlyList<AcmeChallengeResult> challengeResults)
    {
        var acmeProtocolClient = await _acmeProtocolClientFactory.CreateClientAsync();

        // Answer の準備が出来たことを通知
        foreach (var challenge in challengeResults)
        {
            await acmeProtocolClient.AnswerChallengeAsync(challenge.Url);
        }
    }

    [FunctionName(nameof(CheckIsReady))]
    public async Task CheckIsReady([ActivityTrigger] (OrderDetails, IReadOnlyList<AcmeChallengeResult>) input)
    {
        var (orderDetails, challengeResults) = input;

        var acmeProtocolClient = await _acmeProtocolClientFactory.CreateClientAsync();

        orderDetails = await acmeProtocolClient.GetOrderDetailsAsync(orderDetails.OrderUrl, orderDetails);

        if (orderDetails.Payload.Status is "pending" or "processing")
        {
            // pending か processing の場合はリトライする
            throw new RetriableActivityException($"ACME validation status is {orderDetails.Payload.Status}. It will retry automatically.");
        }

        if (orderDetails.Payload.Status == "invalid")
        {
            var problems = new List<Problem>();

            foreach (var challengeResult in challengeResults)
            {
                var challenge = await acmeProtocolClient.GetChallengeDetailsAsync(challengeResult.Url);

                if (challenge.Status != "invalid" || challenge.Error is null)
                {
                    continue;
                }

                _logger.LogError($"ACME domain validation error: {JsonConvert.SerializeObject(challenge.Error)}");

                problems.Add(challenge.Error);
            }

            // 全てのエラーが connection か dns 関係の場合は Orchestrator からリトライさせる
            if (problems.All(x => x.Type is "urn:ietf:params:acme:error:connection" or "urn:ietf:params:acme:error:dns"))
            {
                throw new RetriableOrchestratorException("ACME validation status is invalid, but retriable error. It will retry automatically.");
            }

            // invalid の場合は最初から実行が必要なので失敗させる
            throw new InvalidOperationException($"ACME validation status is invalid. Required retry at first.\nLastError = {JsonConvert.SerializeObject(problems.Last())}");
        }
    }

    [FunctionName(nameof(FinalizeOrder))]
    public async Task<(OrderDetails, RSAParameters)> FinalizeOrder([ActivityTrigger] (IReadOnlyList<string>, OrderDetails) input)
    {
        var (dnsNames, orderDetails) = input;

        // App Service に ECDSA 証明書をアップロードするとエラーになるので一時的に RSA に
        var rsa = RSA.Create(2048);
        var csr = CryptoHelper.Rsa.GenerateCsr(dnsNames, rsa);

        // Order の最終処理を実行し、証明書を作成
        var acmeProtocolClient = await _acmeProtocolClientFactory.CreateClientAsync();

        orderDetails = await acmeProtocolClient.FinalizeOrderAsync(orderDetails.Payload.Finalize, csr);

        return (orderDetails, rsa.ExportParameters(true));
    }

    [FunctionName(nameof(CheckIsValid))]
    public async Task<OrderDetails> CheckIsValid([ActivityTrigger] OrderDetails orderDetails)
    {
        var acmeProtocolClient = await _acmeProtocolClientFactory.CreateClientAsync();

        orderDetails = await acmeProtocolClient.GetOrderDetailsAsync(orderDetails.OrderUrl, orderDetails);

        if (orderDetails.Payload.Status is "pending" or "processing")
        {
            // pending か processing の場合はリトライする
            throw new RetriableActivityException($"Finalize request is {orderDetails.Payload.Status}. It will retry automatically.");
        }

        if (orderDetails.Payload.Status == "invalid")
        {
            // invalid の場合は最初から実行が必要なので失敗させる
            throw new InvalidOperationException("Finalize request is invalid. Required retry at first.");
        }

        return orderDetails;
    }

    [FunctionName(nameof(UploadCertificate))]
    public async Task<CertificateResource> UploadCertificate([ActivityTrigger] (string, IReadOnlyList<string>, OrderDetails, RSAParameters) input)
    {
        var (id, dnsNames, orderDetails, rsaParameters) = input;

        var acmeProtocolClient = await _acmeProtocolClientFactory.CreateClientAsync();

        // 証明書をバイト配列としてダウンロード
        var x509Certificates = await acmeProtocolClient.GetOrderCertificateAsync(orderDetails, _options.PreferredChain);

        // 秘密鍵を含んだ形で X509Certificate2 を作成
        var rsa = RSA.Create(rsaParameters);

        x509Certificates[0] = x509Certificates[0].CopyWithPrivateKey(rsa);

        // PFX 形式としてエクスポート
        var pfxBlob = x509Certificates.Export(X509ContentType.Pfx, "P@ssw0rd");

        var certificateName = dnsNames[0].Replace("*", "wildcard").Replace(".", "-");

        var managedEnvironment = await _containerAppsManagementClient.GetManagedEnvironmentAsync(id);

        // 作成時にリソースタグが追加できないので先に証明書リソースを作成する
        var certificate = await _containerAppsManagementClient.CreateOrUpdateCertificateAsync(id, certificateName, new CertificateResource
        {
            Location = managedEnvironment.Location,
            Data = new CertificateData
            {
                Password = "P@ssw0rd",
                Value = Convert.ToBase64String(pfxBlob)
            }
        });

        // 証明書リソースの作成後にタグのみ追加する
        return await _containerAppsManagementClient.UpdateCertificateTagsAsync(certificate, new Dictionary<string, string>
        {
            { "Issuer", IssuerName },
            { "Endpoint", _options.Endpoint },
            { "DnsNames", string.Join(",", dnsNames) }
        });
    }

    [FunctionName(nameof(CleanupDnsChallenge))]
    public async Task CleanupDnsChallenge([ActivityTrigger] IReadOnlyList<AcmeChallengeResult> challengeResults)
    {
        // Azure DNS zone の一覧を取得する
        var zones = await _dnsManagementClient.Zones.ListAllAsync();

        // DNS-01 の検証レコード名毎に Azure DNS から TXT レコードを削除
        foreach (var lookup in challengeResults.ToLookup(x => x.DnsRecordName))
        {
            var dnsRecordName = lookup.Key;

            var zone = zones.Where(x => dnsRecordName.EndsWith($".{x.Name}", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(x => x.Name.Length)
                            .First();

            var resourceGroup = ExtractResourceGroup(zone.Id);

            // Challenge の詳細から Azure DNS 向けにレコード名を作成
            var acmeDnsRecordName = dnsRecordName.Replace($".{zone.Name}", "", StringComparison.OrdinalIgnoreCase);

            await _dnsManagementClient.RecordSets.DeleteAsync(resourceGroup, zone.Name, acmeDnsRecordName, RecordType.TXT);
        }
    }

    [FunctionName(nameof(CreateDomainVerification))]
    public async Task CreateDomainVerification([ActivityTrigger] (string, IReadOnlyList<string>) input)
    {
        var (containerAppId, dnsNames) = input;

        var containerApp = await _containerAppsManagementClient.GetContainerAppAsync(containerAppId);

        // Azure DNS zone の一覧を取得する
        var zones = await _dnsManagementClient.Zones.ListAllAsync();

        foreach (var dnsName in dnsNames)
        {
            var zone = zones.Where(x => string.Equals(dnsName, x.Name, StringComparison.OrdinalIgnoreCase) || dnsName.EndsWith($".{x.Name}", StringComparison.OrdinalIgnoreCase))
                            .MaxBy(x => x.Name.Length);

            var resourceGroup = ExtractResourceGroup(zone.Id);

            // Container Apps のカスタムドメイン所有チェック用 TXT レコードを作成
            var recordSet = new RecordSet { TTL = 3600 };

            recordSet.TxtRecords.Add(new TxtRecord { Value = { containerApp.Data.CustomDomainVerificationId } });

            // 検証用に使う TXT レコード名を組み立てる、ワイルドカードの場合は 1 つずらす
            var varificationDnsName = $"asuid.{dnsName.Replace("*.", "")}";

            // Azure DNS は相対的なレコード名が必要なのでゾーン名を削除
            var relativeDnsName = varificationDnsName.Replace($".{zone.Name}", "", StringComparison.OrdinalIgnoreCase);

            await _dnsManagementClient.RecordSets.CreateOrUpdateAsync(resourceGroup, zone.Name, relativeDnsName, RecordType.TXT, recordSet);
        }
    }

    [FunctionName(nameof(ValidateDomain))]
    public async Task ValidateDomain([ActivityTrigger] (string, IReadOnlyList<string>) input)
    {
        var (containerAppId, dnsNames) = input;

        foreach (var dnsName in dnsNames)
        {
            var result = await _containerAppsManagementClient.ListCustomHostNameAnalysisAsync(containerAppId, dnsName);

            if (result.CustomDomainVerificationTest != "Passed")
            {
                throw new RetriableActivityException(result.CustomDomainVerificationFailureInfo.Message);
            }
        }
    }

    [FunctionName(nameof(BindDomains))]
    public async Task BindDomains([ActivityTrigger] (string, string, IReadOnlyList<string>) input)
    {
        var (containerAppId, certificateId, dnsNames) = input;

        var containerApp = await _containerAppsManagementClient.GetContainerAppAsync(containerAppId);

        var customDomains = containerApp.Data.Configuration.Ingress.CustomDomains ?? Enumerable.Empty<CustomDomain>();

        var newCustomDomains = dnsNames.Select(x => new CustomDomain { Name = x, BindingType = "SniEnabled", CertificateId = certificateId });

        containerApp.Data.Configuration.Ingress.CustomDomains = customDomains.Union(newCustomDomains).ToArray();

        await _containerAppsManagementClient.UpdateContainerAppAsync(containerApp);
    }

    [FunctionName(nameof(SendCompletedEvent))]
    public Task SendCompletedEvent([ActivityTrigger] (string, DateTimeOffset, IReadOnlyList<string>) input)
    {
        var (managedEnvironmentId, expirationDate, dnsNames) = input;

        var managedEnvironmentName = ExtractResourceName(managedEnvironmentId);

        return _webhookInvoker.SendCompletedEventAsync(managedEnvironmentName, expirationDate, dnsNames);
    }

    private static string ExtractResourceGroup(string resourceId)
    {
        var values = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return values[3];
    }

    private static string ExtractResourceName(string resourceId)
    {
        var values = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return values[7];
    }
}
