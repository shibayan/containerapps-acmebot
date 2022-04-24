using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Azure.WebJobs.Extensions.HttpApi;

using ContainerApps.Acmebot.Internal;

using DurableTask.TypedProxy;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace ContainerApps.Acmebot.Functions;

public class GetZones : HttpFunctionBase
{
    public GetZones(IHttpContextAccessor httpContextAccessor)
        : base(httpContextAccessor)
    {
    }

    [FunctionName($"{nameof(GetZones)}_{nameof(Orchestrator)}")]
    public Task<IReadOnlyList<string>> Orchestrator([OrchestrationTrigger] IDurableOrchestrationContext context)
    {
        var activity = context.CreateActivityProxy<ISharedActivity>();

        return activity.GetZones();
    }

    [FunctionName($"{nameof(GetZones)}_{nameof(HttpStart)}")]
    public async Task<IActionResult> HttpStart(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/zones")] HttpRequest req,
        [DurableClient] IDurableClient starter,
        ILogger log)
    {
        if (!User.IsAppAuthorized())
        {
            return Unauthorized();
        }

        // Function input comes from the request content.
        var instanceId = await starter.StartNewAsync($"{nameof(GetZones)}_{nameof(Orchestrator)}");

        log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

        return await starter.WaitForCompletionOrCreateCheckStatusResponseAsync(req, instanceId, TimeSpan.FromMinutes(1), returnInternalServerErrorOnFailure: true);
    }
}
