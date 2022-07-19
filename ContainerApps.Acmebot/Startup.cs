using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;

using ContainerApps.Acmebot.Internal;
using ContainerApps.Acmebot.Options;

using DnsClient;

using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

[assembly: FunctionsStartup(typeof(ContainerApps.Acmebot.Startup))]

namespace ContainerApps.Acmebot;

public class Startup : FunctionsStartup
{
    public override void Configure(IFunctionsHostBuilder builder)
    {
        // Add Options
        var context = builder.GetContext();

        builder.Services.AddOptions<AcmebotOptions>()
               .Bind(context.Configuration.GetSection("Acmebot"))
               .ValidateDataAnnotations();

        // Add Services
        builder.Services.Replace(ServiceDescriptor.Transient(typeof(IOptionsFactory<>), typeof(OptionsFactory<>)));

        builder.Services.AddHttpClient();

        builder.Services.AddSingleton<ITelemetryInitializer, ApplicationVersionInitializer<Startup>>();

        builder.Services.AddSingleton(new LookupClient(new LookupClientOptions(NameServer.GooglePublicDns, NameServer.GooglePublicDns2)
        {
            UseCache = false,
            UseRandomNameServer = true
        }));

        builder.Services.AddSingleton<TokenCredential>(provider =>
        {
            var environment = provider.GetRequiredService<AzureEnvironment>();

            return new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                AuthorityHost = environment.ActiveDirectory
            });
        });

        builder.Services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AcmebotOptions>>();

            return AzureEnvironment.Get(options.Value.Environment);
        });

        builder.Services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AcmebotOptions>>();
            var environment = provider.GetRequiredService<AzureEnvironment>();
            var credential = provider.GetRequiredService<TokenCredential>();

            return new ArmClient(credential, options.Value.SubscriptionId, new ArmClientOptions { Environment = environment.ResourceManager });
        });

        builder.Services.AddSingleton<AcmeProtocolClientFactory>();

        builder.Services.AddSingleton<WebhookInvoker>();
        builder.Services.AddSingleton<ILifeCycleNotificationHelper, WebhookLifeCycleNotification>();
    }
}
