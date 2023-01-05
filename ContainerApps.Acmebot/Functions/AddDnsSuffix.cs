using System.Threading.Tasks;

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

public class AddDnsSuffix : HttpFunctionBase
{
    public AddDnsSuffix(IHttpContextAccessor httpContextAccessor)
        : base(httpContextAccessor)
    {
    }

    [FunctionName($"{nameof(AddDnsSuffix)}_{nameof(Orchestrator)}")]
    public async Task Orchestrator([OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
    {
        var request = context.GetInput<AddDnsSuffixRequest>();

        var activity = context.CreateActivityProxy<ISharedActivity>();

        var asciiDnsSuffix = Punycode.Encode(request.DnsSuffix);

        // DNS サフィックス用のワイルドカード証明書を作成する
        var asciiDnsNames = new[] { $"*.{asciiDnsSuffix}" };

        // ACME で証明書を発行する
        var (pfxBlob, password) = await context.CallSubOrchestratorAsync<(byte[], string)>(nameof(SharedOrchestrator.IssueCertificate), asciiDnsNames);

        // 検証用の DNS レコードを作成
        await activity.CreateDnsSuffixVerification((request.ManagedEnvironmentId, asciiDnsSuffix));

        // DNS サフィックスを追加する
        await activity.UploadDnsSuffix((request.ManagedEnvironmentId, asciiDnsSuffix, pfxBlob, password));
    }

    [FunctionName($"{nameof(AddDnsSuffix)}_{nameof(HttpStart)}")]
    public async Task<IActionResult> HttpStart(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/dns-suffix")] AddDnsSuffixRequest request,
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
        var instanceId = await starter.StartNewAsync($"{nameof(AddDnsSuffix)}_{nameof(Orchestrator)}", request);

        log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

        return AcceptedAtFunction($"{nameof(GetInstanceState)}_{nameof(GetInstanceState.HttpStart)}", new { instanceId }, null);
    }
}
