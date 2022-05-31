using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;

using ACMESharp.Protocol;

using Azure.ResourceManager.Applications.Containers;

using ContainerApps.Acmebot.Models;

using DurableTask.TypedProxy;

namespace ContainerApps.Acmebot.Functions;

public interface ISharedActivity
{
    Task<IReadOnlyList<ManagedEnvironmentData>> GetManagedEnvironments(object input = null);

    Task<IReadOnlyList<ContainerAppData>> GetContainerApps(string managedEnvironmentId);

    Task<IReadOnlyList<ContainerAppCertificateData>> GetExpiringCertificates((string, DateTime) input);

    Task<IReadOnlyList<string>> GetZones(object input = null);

    Task<OrderDetails> Order(IReadOnlyList<string> dnsNames);

    Task Dns01Precondition(IReadOnlyList<string> dnsNames);

    Task<IReadOnlyList<AcmeChallengeResult>> Dns01Authorization(IReadOnlyList<string> authorizationUrls);

    [RetryOptions("00:00:10", 12, HandlerType = typeof(ExceptionRetryStrategy<RetriableActivityException>))]
    Task CheckDnsChallenge(IReadOnlyList<AcmeChallengeResult> challengeResults);

    Task AnswerChallenges(IReadOnlyList<AcmeChallengeResult> challengeResults);

    [RetryOptions("00:00:05", 12, HandlerType = typeof(ExceptionRetryStrategy<RetriableActivityException>))]
    Task CheckIsReady((OrderDetails, IReadOnlyList<AcmeChallengeResult>) input);

    Task<(OrderDetails, RSAParameters)> FinalizeOrder((IReadOnlyList<string>, OrderDetails) input);

    [RetryOptions("00:00:05", 12, HandlerType = typeof(ExceptionRetryStrategy<RetriableActivityException>))]
    Task<OrderDetails> CheckIsValid(OrderDetails orderDetails);

    Task<ContainerAppCertificateData> UploadCertificate((string, IReadOnlyList<string>, OrderDetails, RSAParameters) input);

    Task CleanupDnsChallenge(IReadOnlyList<AcmeChallengeResult> challengeResults);

    Task CreateDomainVerification((string, IReadOnlyList<string>) input);

    [RetryOptions("00:00:10", 12, HandlerType = typeof(ExceptionRetryStrategy<RetriableActivityException>))]
    Task ValidateDomain((string, IReadOnlyList<string>) input);

    Task BindDomains((string, string, IReadOnlyList<string>) input);

    Task SendCompletedEvent((string, DateTimeOffset, IReadOnlyList<string>) input);
}
