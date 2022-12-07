using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using ACMESharp.Authorizations;
using ACMESharp.Crypto;
using ACMESharp.Protocol;
using ACMESharp.Protocol.Resources;

using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;
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
                          ArmClient armClient, WebhookInvoker webhookInvoker,
                          IOptions<AcmebotOptions> options, ILogger<SharedActivity> logger)
    {
        _acmeProtocolClientFactory = acmeProtocolClientFactory;
        _lookupClient = lookupClient;
        _armClient = armClient;
        _webhookInvoker = webhookInvoker;
        _options = options.Value;
        _logger = logger;
    }

    private readonly AcmeProtocolClientFactory _acmeProtocolClientFactory;
    private readonly LookupClient _lookupClient;
    private readonly ArmClient _armClient;
    private readonly WebhookInvoker _webhookInvoker;
    private readonly AcmebotOptions _options;
    private readonly ILogger<SharedActivity> _logger;

    private const string IssuerName = "Acmebot";

    [FunctionName(nameof(GetManagedEnvironments))]
    public async Task<IReadOnlyList<ManagedEnvironmentItem>> GetManagedEnvironments([ActivityTrigger] object input = null)
    {
        var subscription = await _armClient.GetDefaultSubscriptionAsync();

        var managedEnvironments = new List<ManagedEnvironmentItem>();

        await foreach (var managedEnvironment in subscription.GetManagedEnvironmentsAsync())
        {
            managedEnvironments.Add(new ManagedEnvironmentItem
            {
                Id = managedEnvironment.Id,
                Name = managedEnvironment.Data.Name,
                ResourceGroup = managedEnvironment.Id.ResourceGroupName
            });
        }

        return managedEnvironments;
    }

    [FunctionName(nameof(GetContainerApps))]
    public async Task<IReadOnlyList<ContainerAppItem>> GetContainerApps([ActivityTrigger] string managedEnvironmentId)
    {
        var subscription = await _armClient.GetDefaultSubscriptionAsync();

        var containerApps = new List<ContainerAppItem>();

        await foreach (var containerApp in subscription.GetContainerAppsAsync())
        {
            if (containerApp.Data.ManagedEnvironmentId != managedEnvironmentId)
            {
                continue;
            }

            containerApps.Add(new ContainerAppItem
            {
                Id = containerApp.Id,
                Name = containerApp.Data.Name
            });
        }

        return containerApps;
    }

    [FunctionName(nameof(GetExpiringCertificates))]
    public async Task<IReadOnlyList<ContainerAppCertificateItem>> GetExpiringCertificates([ActivityTrigger] (string, DateTime) input)
    {
        var (managedEnvironmentId, currentDateTime) = input;

        var managedEnvironment = _armClient.GetManagedEnvironmentResource(new ResourceIdentifier(managedEnvironmentId));

        var containerAppCertificates = new List<ContainerAppCertificateItem>();

        await foreach (var containerAppCertificate in managedEnvironment.GetContainerAppCertificates())
        {
            if (!containerAppCertificate.Data.TagsFilter(IssuerName, _options.Endpoint))
            {
                continue;
            }

            if ((containerAppCertificate.Data.Properties.ExpirationOn.Value - currentDateTime).TotalDays > _options.RenewBeforeExpiry)
            {
                continue;
            }

            containerAppCertificates.Add(new ContainerAppCertificateItem
            {
                Id = containerAppCertificate.Id,
                Name = containerAppCertificate.Data.Name,
                ExpirationOn = containerAppCertificate.Data.Properties.ExpirationOn.Value,
                Tags = containerAppCertificate.Data.Tags
            });
        }

        return containerAppCertificates;
    }

    [FunctionName(nameof(GetZones))]
    public async Task<IReadOnlyList<string>> GetZones([ActivityTrigger] object input = null)
    {
        try
        {
            var subscription = await _armClient.GetDefaultSubscriptionAsync();

            var zones = await subscription.ListAllDnsZonesAsync();

            return zones.Select(x => x.Data.Name).ToArray();
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
        var subscription = await _armClient.GetDefaultSubscriptionAsync();

        var dnsZones = await subscription.ListAllDnsZonesAsync();

        var foundDnsZones = new HashSet<DnsZoneData>();
        var zoneNotFoundDnsNames = new List<string>();

        foreach (var dnsName in dnsNames)
        {
            var dnsZone = dnsZones.Where(x => string.Equals(dnsName, x.Data.Name, StringComparison.OrdinalIgnoreCase) || dnsName.EndsWith($".{x.Data.Name}", StringComparison.OrdinalIgnoreCase))
                                  .MaxBy(x => x.Data.Name.Length);

            // マッチする DNS zone が見つからない場合はエラー
            if (dnsZone == null)
            {
                zoneNotFoundDnsNames.Add(dnsName);
                continue;
            }

            foundDnsZones.Add(dnsZone.Data);
        }

        if (zoneNotFoundDnsNames.Count > 0)
        {
            throw new PreconditionException($"DNS zone(s) are not found. DnsNames = {string.Join(",", zoneNotFoundDnsNames)}");
        }

        // DNS zone に移譲されている Name servers が正しいか検証
        foreach (var zone in foundDnsZones)
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
        var subscription = await _armClient.GetDefaultSubscriptionAsync();

        var dnsZones = await subscription.ListAllDnsZonesAsync();

        // DNS-01 の検証レコード名毎に Azure DNS に TXT レコードを作成
        foreach (var lookup in challengeResults.ToLookup(x => x.DnsRecordName))
        {
            var dnsRecordName = lookup.Key;

            var dnsZone = dnsZones.Where(x => dnsRecordName.EndsWith($".{x.Data.Name}", StringComparison.OrdinalIgnoreCase))
                                  .MaxBy(x => x.Data.Name.Length);

            // Challenge の詳細から Azure DNS 向けにレコード名を作成
            var acmeDnsRecordName = dnsRecordName.Replace($".{dnsZone.Data.Name}", "", StringComparison.OrdinalIgnoreCase);

            // TXT レコードに TTL と値をセットする
            var recordSets = dnsZone.GetDnsTxtRecords();

            var recordSet = new DnsTxtRecordData
            {
                TtlInSeconds = 60
            };

            foreach (var value in lookup)
            {
                recordSet.DnsTxtRecords.Add(new DnsTxtRecordInfo { Values = { value.DnsRecordValue } });
            }

            await recordSets.CreateOrUpdateAsync(WaitUntil.Completed, acmeDnsRecordName, recordSet);
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
        using var rsa = RSA.Create(2048);
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

        return orderDetails.Payload.Status switch
        {
            "pending" or "processing" => throw new RetriableActivityException($"Finalize request is {orderDetails.Payload.Status}. It will retry automatically."),
            "invalid" => throw new InvalidOperationException("Finalize request is invalid. Required retry at first."),
            _ => orderDetails
        };
    }

    [FunctionName(nameof(UploadCertificate))]
    public async Task<ContainerAppCertificateItem> UploadCertificate([ActivityTrigger] (string, IReadOnlyList<string>, OrderDetails, RSAParameters) input)
    {
        var (id, dnsNames, orderDetails, rsaParameters) = input;

        var acmeProtocolClient = await _acmeProtocolClientFactory.CreateClientAsync();

        // 証明書をバイト配列としてダウンロード
        var x509Certificates = await acmeProtocolClient.GetOrderCertificateAsync(orderDetails, _options.PreferredChain);

        // 秘密鍵を含んだ形で X509Certificate2 を作成
        using var rsa = RSA.Create(rsaParameters);

        x509Certificates[0] = x509Certificates[0].CopyWithPrivateKey(rsa);

        // 一時パスワードを生成
        var password = Guid.NewGuid().ToString();

        // PFX 形式としてエクスポート
        var pfxBlob = x509Certificates.Export(X509ContentType.Pfx, password);

        var certificateName = dnsNames[0].Replace("*", "wildcard").Replace(".", "-");

        // Managed Environment の情報を ARM から取得
        ManagedEnvironmentResource managedEnvironment = await _armClient.GetManagedEnvironmentResource(new ResourceIdentifier(id)).GetAsync();

        var containerAppCertificates = managedEnvironment.GetContainerAppCertificates();

        // 作成時にリソースタグが追加できないので先に証明書リソースを作成する
        var containerAppCertificate = await containerAppCertificates.CreateOrUpdateAsync(WaitUntil.Completed, certificateName, new ContainerAppCertificateData(managedEnvironment.Data.Location)
        {
            Properties = new CertificateProperties
            {
                Password = password,
                Value = pfxBlob
            }
        });

        // 証明書リソースの作成後にタグのみ追加する
        ContainerAppCertificateResource containerAppCertificateWithTags = await containerAppCertificate.Value.SetTagsAsync(new Dictionary<string, string>
        {
            { "Issuer", IssuerName },
            { "Endpoint", _options.Endpoint },
            { "DnsNames", string.Join(",", dnsNames) }
        });

        return new ContainerAppCertificateItem
        {
            Id = containerAppCertificateWithTags.Id,
            Name = containerAppCertificateWithTags.Data.Name,
            ExpirationOn = containerAppCertificateWithTags.Data.Properties.ExpirationOn.Value,
            Tags = containerAppCertificateWithTags.Data.Tags
        };
    }

    [FunctionName(nameof(CleanupDnsChallenge))]
    public async Task CleanupDnsChallenge([ActivityTrigger] IReadOnlyList<AcmeChallengeResult> challengeResults)
    {
        // Azure DNS zone の一覧を取得する
        var subscription = await _armClient.GetDefaultSubscriptionAsync();

        var dnsZones = await subscription.ListAllDnsZonesAsync();

        // DNS-01 の検証レコード名毎に Azure DNS から TXT レコードを削除
        foreach (var lookup in challengeResults.ToLookup(x => x.DnsRecordName))
        {
            var dnsRecordName = lookup.Key;

            var dnsZone = dnsZones.Where(x => dnsRecordName.EndsWith($".{x.Data.Name}", StringComparison.OrdinalIgnoreCase))
                                  .OrderByDescending(x => x.Data.Name.Length)
                                  .First();

            // Challenge の詳細から Azure DNS 向けにレコード名を作成
            var acmeDnsRecordName = dnsRecordName.Replace($".{dnsZone.Data.Name}", "", StringComparison.OrdinalIgnoreCase);

            DnsTxtRecordResource recordSet = await dnsZone.GetDnsTxtRecordAsync(acmeDnsRecordName);

            await recordSet.DeleteAsync(WaitUntil.Completed);
        }
    }

    [FunctionName(nameof(CreateDomainVerification))]
    public async Task CreateDomainVerification([ActivityTrigger] (string, IReadOnlyList<string>) input)
    {
        var (containerAppId, dnsNames) = input;

        ContainerAppResource containerApp = await _armClient.GetContainerAppResource(new ResourceIdentifier(containerAppId)).GetAsync();

        // Azure DNS zone の一覧を取得する
        var subscription = await _armClient.GetDefaultSubscriptionAsync();

        var dnsZones = await subscription.ListAllDnsZonesAsync();

        foreach (var dnsName in dnsNames)
        {
            var dnsZone = dnsZones.Where(x => string.Equals(dnsName, x.Data.Name, StringComparison.OrdinalIgnoreCase) || dnsName.EndsWith($".{x.Data.Name}", StringComparison.OrdinalIgnoreCase))
                                  .MaxBy(x => x.Data.Name.Length);

            // Container Apps のカスタムドメイン所有チェック用 TXT レコードを作成
            var recordSet = new DnsTxtRecordData
            {
                TtlInSeconds = 3600
            };

            recordSet.DnsTxtRecords.Add(new DnsTxtRecordInfo { Values = { containerApp.Data.CustomDomainVerificationId } });

            // 検証用に使う TXT レコード名を組み立てる、ワイルドカードの場合は 1 つずらす
            var varificationDnsName = $"asuid.{dnsName.Replace("*.", "")}";

            // Azure DNS は相対的なレコード名が必要なのでゾーン名を削除
            var relativeDnsName = varificationDnsName.Replace($".{dnsZone.Data.Name}", "", StringComparison.OrdinalIgnoreCase);

            var recordSets = dnsZone.GetDnsTxtRecords();

            await recordSets.CreateOrUpdateAsync(WaitUntil.Completed, relativeDnsName, recordSet);
        }
    }

    [FunctionName(nameof(ValidateDomain))]
    public async Task ValidateDomain([ActivityTrigger] (string, IReadOnlyList<string>) input)
    {
        var (containerAppId, dnsNames) = input;

        var containerApp = _armClient.GetContainerAppResource(new ResourceIdentifier(containerAppId));

        foreach (var dnsName in dnsNames)
        {
            var response = await containerApp.GetCustomHostNameAnalysisAsync(dnsName);

            var result = response.GetRawResponse().Content.ToObjectFromJson<CustomHostNameAnalysisResult>();

            if (result.CustomDomainVerificationTest != "Passed")
            {
                throw new RetriableActivityException(result.CustomDomainVerificationFailureInfo["message"]?.ToString());
            }
        }
    }

    [FunctionName(nameof(BindDomains))]
    public async Task BindDomains([ActivityTrigger] (string, string, IReadOnlyList<string>) input)
    {
        var (containerAppId, certificateId, dnsNames) = input;

        ContainerAppResource containerApp = await _armClient.GetContainerAppResource(new ResourceIdentifier(containerAppId)).GetAsync();

        var ingress = containerApp.Data.Configuration.Ingress;

        foreach (var dnsName in dnsNames)
        {
            if (ingress.CustomDomains.All(x => x.Name != dnsName))
            {
                ingress.CustomDomains.Add(new CustomDomain(dnsName, certificateId) { BindingType = BindingType.SniEnabled });
            }
        }

        var newContainerAppData = new ContainerAppData(containerApp.Data.Location)
        {
            Configuration = new ContainerAppConfiguration
            {
                Ingress = ingress
            }
        };

        await containerApp.UpdateAsync(WaitUntil.Completed, newContainerAppData);
    }

    [FunctionName(nameof(SendCompletedEvent))]
    public Task SendCompletedEvent([ActivityTrigger] (string, DateTimeOffset, IReadOnlyList<string>) input)
    {
        var (managedEnvironmentId, expirationDate, dnsNames) = input;

        var managedEnvironmentName = new ResourceIdentifier(managedEnvironmentId).Name;

        return _webhookInvoker.SendCompletedEventAsync(managedEnvironmentName, expirationDate, dnsNames);
    }

    public class CustomHostNameAnalysisResult
    {
        [JsonPropertyName("customDomainVerificationTest")]
        public string CustomDomainVerificationTest { get; set; }

        [JsonPropertyName("customDomainVerificationFailureInfo")]
        public JsonObject CustomDomainVerificationFailureInfo { get; set; }
    }

}
