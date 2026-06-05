// references/builder-and-tools.cs
//
// Minimal example of wiring Microsoft Agent Framework directly using
// IServiceCollection. Demonstrates:
//   1. Tool discovery via public methods decorated with [Description].
//   2. Registering the tool class in DI so it can be resolved per-scope.
//   3. Building a ChatClientAgent with AIFunctionFactory.
//   4. No custom wrapper around AIAgent. Use the SDK directly.

using System.ComponentModel;
using Azure.AI.OpenAI;
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

public sealed class AzureOpenAIOptions
{
    public required string Endpoint { get; init; }
    public required string DeploymentName { get; init; }
}

public static class AgentRegistration
{
    public static IServiceCollection AddWeatherAgent(this IServiceCollection services)
    {
        services.AddScoped<WeatherTools>();

        services.AddSingleton<AIAgent>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AzureOpenAIOptions>>().Value;

            var chatClient = new AzureOpenAIClient(
                    new Uri(opts.Endpoint),
                    new DefaultAzureCredential())
                .GetChatClient(opts.DeploymentName)
                .AsIChatClient();

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
