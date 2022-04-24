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

public class GetEnvironments : HttpFunctionBase
{
    public GetEnvironments(IHttpContextAccessor httpContextAccessor)
        : base(httpContextAccessor)
    {
    }

    [FunctionName($"{nameof(GetEnvironments)}_{nameof(Orchestrator)}")]
    public async Task<IReadOnlyList<ManagedEnvironmentItem>> Orchestrator([OrchestrationTrigger] IDurableOrchestrationContext context)
    {
        var activity = context.CreateActivityProxy<ISharedActivity>();

        try
        {
            var managedEnvironments = await activity.GetManagedEnvironments();

            return managedEnvironments.Select(x => new ManagedEnvironmentItem
            {
                Id = x.Id,
                Name = x.Name,
                ResourceGroup = x.GetResourceGroup()
            }).ToArray();
        }
        catch
        {
            return Array.Empty<ManagedEnvironmentItem>();
        }
    }

    [FunctionName($"{nameof(GetEnvironments)}_{nameof(HttpStart)}")]
    public async Task<IActionResult> HttpStart(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/environments")] HttpRequest req,
        [DurableClient] IDurableClient starter,
        ILogger log)
    {
        if (!User.IsAppAuthorized())
        {
            return Unauthorized();
        }

        // Function input comes from the request content.
        var instanceId = await starter.StartNewAsync($"{nameof(GetEnvironments)}_{nameof(Orchestrator)}");

        log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

        return await starter.WaitForCompletionOrCreateCheckStatusResponseAsync(req, instanceId, TimeSpan.FromMinutes(1), returnInternalServerErrorOnFailure: true);
    }
}
