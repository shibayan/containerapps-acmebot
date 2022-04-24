using System.Linq;
using System.Threading.Tasks;

using Azure.ResourceManager.App.Models;
using Azure.WebJobs.Extensions.HttpApi;

using ContainerApps.Acmebot.Internal;
using ContainerApps.Acmebot.Models;

using DurableTask.TypedProxy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace ContainerApps.Acmebot.Functions;

public class AddCertificate : HttpFunctionBase
{
    public AddCertificate(IHttpContextAccessor httpContextAccessor)
        : base(httpContextAccessor)
    {
    }

    [FunctionName($"{nameof(AddCertificate)}_{nameof(Orchestrator)}")]
    public async Task Orchestrator([OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
    {
        var request = context.GetInput<AddCertificateRequest>();

        var activity = context.CreateActivityProxy<ISharedActivity>();

        var asciiDnsNames = request.DnsNames.Select(Punycode.Encode).ToArray();

        // 証明書を発行し Azure にアップロード
        var certificate = await context.CallSubOrchestratorAsync<CertificateResource>(nameof(SharedOrchestrator.IssueCertificate), (request.ManagedEnvironmentId, asciiDnsNames));

        // Container App と DNS にカスタムドメイン設定自体を追加する
        if (request.BindToContainerApp)
        {
            await context.CallSubOrchestratorAsync(nameof(SharedOrchestrator.BindToContainerApp), (request.ContainerAppId, certificate.Id, asciiDnsNames));
        }

        // 証明書の更新が完了後に Webhook を送信する
        await activity.SendCompletedEvent((request.ManagedEnvironmentId, certificate.Data.ExpirationDate.Value, asciiDnsNames));
    }

    [FunctionName($"{nameof(AddCertificate)}_{nameof(HttpStart)}")]
    public async Task<IActionResult> HttpStart(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/certificate")] AddCertificateRequest request,
        [DurableClient] IDurableClient starter,
        ILogger log)
    {
        if (!User.IsAppAuthorized())
        {
            return Unauthorized();
        }

        if (!TryValidateModel(request))
        {
            return ValidationProblem(ModelState);
        }

        // Function input comes from the request content.
        var instanceId = await starter.StartNewAsync($"{nameof(AddCertificate)}_{nameof(Orchestrator)}", request);

        log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

        return AcceptedAtFunction($"{nameof(GetInstanceState)}_{nameof(GetInstanceState.HttpStart)}", new { instanceId }, null);
    }
}
