// references/builder-and-tools.cs
//
// Minimal example of wiring Microsoft Agent Framework directly using
// IServiceCollection. Demonstrates:
//   1. Tool discovery via public methods decorated with [Description].
//   2. Registering the tool class in DI so it can be resolved per-scope.
//   3. Building a ChatClientAgent backed by the Azure AI Foundry model inference API.
//   4. No custom wrapper around AIAgent. Use the SDK directly.

using System.ComponentModel;
using Azure.AI.Inference;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

public sealed class WeatherTools
{
    [Description("Get the current temperature in celsius for a city.")]
    public Task<double> GetTemperature(
        [Description("City name, e.g. 'Amsterdam'.")] string city,
        CancellationToken ct = default)
    {
        // Real implementation would call an API. Keep tool methods thin —
        // they're the boundary to the outside world, not business logic.
        return Task.FromResult(18.5);
    }
}

public sealed class AzureAIFoundryOptions
{
    // Azure AI Model Inference API endpoint.
    // Format: https://<account>.services.ai.azure.com/models
    // Provision via foundry-model-deployment; output: modelsEndpoint.
    public required string Endpoint { get; init; }

    // Name of the model deployment in the Foundry account (e.g. "gpt-4o").
    // Provision via foundry-model-deployment; output: deploymentName.
    public required string DeploymentName { get; init; }
}

public static class AgentRegistration
{
    public static IServiceCollection AddWeatherAgent(this IServiceCollection services)
    {
        services.AddScoped<WeatherTools>();

        services.AddSingleton<AIAgent>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AzureAIFoundryOptions>>().Value;

            // ChatCompletionsClient targets the Azure AI Model Inference API.
            // Works with any model deployed in the Foundry account — OpenAI GPT,
            // Microsoft Phi, Meta Llama, etc. — without changing this code.
            // DefaultAzureCredential uses 'az login' locally and managed identity
            // in Azure. No API keys stored anywhere.
            var chatClient = new ChatCompletionsClient(
                    new Uri(opts.Endpoint),
                    new DefaultAzureCredential())
                .AsChatClient(opts.DeploymentName);

            // Pull tool methods from the registered class via reflection.
            var tools = sp.GetRequiredService<WeatherTools>();
            var aiTools = typeof(WeatherTools)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(m => m.GetCustomAttributes(typeof(DescriptionAttribute), false).Any())
                .Select(m => AIFunctionFactory.Create(m, tools))
                .Cast<AITool>()
                .ToList();

            var options = new ChatClientAgentOptions
            {
                Name = "weather-agent",
                Instructions = "You are a concise weather assistant.",
                Tools = aiTools,
            };

            return new ChatClientAgent(chatClient, options);
        });

        return services;
    }
}
