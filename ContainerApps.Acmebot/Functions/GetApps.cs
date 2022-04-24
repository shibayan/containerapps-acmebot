using System;
using System.Collections.Generic;
using System.Linq;
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

public class GetApps : HttpFunctionBase
{
    public GetApps(IHttpContextAccessor httpContextAccessor)
        : base(httpContextAccessor)
    {
    }

    [FunctionName($"{nameof(GetApps)}_{nameof(Orchestrator)}")]
    public async Task<IReadOnlyList<ContainerAppItem>> Orchestrator([OrchestrationTrigger] IDurableOrchestrationContext context)
    {
        var managedEnvironmentId = context.GetInput<string>();

        var activity = context.CreateActivityProxy<ISharedActivity>();

        try
        {
            var containerApps = await activity.GetContainerApps(managedEnvironmentId);

            return containerApps.Select(x => new ContainerAppItem { Id = x.Id, Name = x.Name }).ToArray();
        }
        catch
        {
            return Array.Empty<ContainerAppItem>();
        }
    }

    [FunctionName($"{nameof(GetApps)}_{nameof(HttpStart)}")]
    public async Task<IActionResult> HttpStart(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/apps")] HttpRequest req,
        [DurableClient] IDurableClient starter,
        ILogger log)
    {
        if (!User.IsAppAuthorized())
        {
            return Unauthorized();
        }

        // Function input comes from the request content.
        var instanceId = await starter.StartNewAsync($"{nameof(GetApps)}_{nameof(Orchestrator)}", null, req.Query["id"].ToString());

        log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

        return await starter.WaitForCompletionOrCreateCheckStatusResponseAsync(req, instanceId, TimeSpan.FromMinutes(1), returnInternalServerErrorOnFailure: true);
    }
}
