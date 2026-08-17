// references/wiring.cs
//
// IServiceCollection wiring for the sandbox. Registers ONE ISandbox; the
// implementation is selected by configuration so local F5 uses Docker and the
// deployed app uses Azure Container Apps dynamic sessions. The SandboxTools
// class is registered like any other tool class and discovered into AIFunctions
// by the agent build-up (see maf-csharp-implementation builder-and-tools.cs).
//
// FILE LOCATION: src/<Solution>.Tools.Sandbox/ServiceCollectionExtensions.cs
//
// Config (appsettings.json / environment):
//   "Sandbox": {
//     "Runtime": "Local",                // "Local" (dev) | "Aca" (cloud)
//     "Aca":   { "PoolManagementEndpoint": "https://<pool>....azurecontainerapps.io" },
//     "Local": { "Image": "agent-session-executor:dev" },
//     "Tools": { "CommandTimeout": "00:01:00", "LogRawArguments": false }
//   }

using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sandbox.Aca;
using Sandbox.Local;

namespace Sandbox;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSandbox(
        this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection("Sandbox");
        var runtime = section["Runtime"] ?? "Local";

        services.AddOptions<SandboxToolsOptions>().Bind(section.GetSection("Tools"));

        if (string.Equals(runtime, "Aca", StringComparison.OrdinalIgnoreCase))
        {
            services.AddOptions<AcaSessionsOptions>()
                .Bind(section.GetSection("Aca"))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // One credential for the host. DefaultAzureCredential => managed
            // identity in Azure, 'az login' locally. The SESSION gets none.
            services.AddSingleton<Azure.Core.TokenCredential>(_ => new DefaultAzureCredential());
            services.AddHttpClient<ISandbox, AcaSessionsSandbox>();
        }
        else
        {
            services.AddOptions<LocalDockerOptions>().Bind(section.GetSection("Local"));
            // Singleton so the per-conversation container pool + cooldown persist
            // for the lifetime of the process during local dev.
            services.AddHttpClient();
            services.AddSingleton<ISandbox, LocalDockerSandbox>();
        }

        // Registered like any tool class; the agent reflects [Description] methods
        // into AIFunctions. IConversation is supplied per request (scoped).
        services.AddSingleton<SandboxTools>();
        return services;
    }
}
