// references/builder-and-tools.cs
//
// Canonical vertical-slice wiring for Microsoft Agent Framework.
//
// FILE LOCATION: src/<Solution>/Agents/WeatherAgent/WeatherAgentExtensions.cs
//
// Demonstrates:
//   1. Slice-level DI extension — one per agent, lives inside Agents/<AgentName>/.
//   2. Tool discovery via public methods decorated with [Description].
//   3. Correct MAF 1.10+ ChatClientAgent constructor (instructions + name + tools
//      are constructor parameters, not ChatClientAgentOptions properties).
//   4. Project-level composer that calls each slice — host wiring never changes.

using System.ComponentModel;
using Azure.AI.Inference;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// ---------------------------------------------------------------------------
// Tool class — lives in the separate <Solution>.Tools.<Provider> project.
// Shown here for completeness; the agent project takes a project reference.
// ---------------------------------------------------------------------------

public sealed class WeatherTools
{
    [Description("Get the current temperature in celsius for a city.")]
    public Task<double> GetTemperature(
        [Description("City name, e.g. 'Amsterdam'.")] string city,
        CancellationToken ct = default)
    {
        // Real implementation calls an API. Keep tool methods thin —
        // they're the boundary to the outside world, not business logic.
        return Task.FromResult(18.5);
    }
}

// ---------------------------------------------------------------------------
// Options — one class per external dependency, bound per slice.
// ---------------------------------------------------------------------------

[System.ComponentModel.DataAnnotations.Required]
public sealed class AzureAIFoundryOptions
{
    // Azure AI Model Inference API endpoint.
    // Format: https://<account>.services.ai.azure.com/models
    // Provision via foundry-model-deployment; output: modelsEndpoint.
    [System.ComponentModel.DataAnnotations.Required]
    public required string Endpoint { get; init; }

    // Name of the model deployment in the Foundry account (e.g. "gpt-4o").
    // Provision via foundry-model-deployment; output: deploymentName.
    [System.ComponentModel.DataAnnotations.Required]
    public required string DeploymentName { get; init; }
}

// ---------------------------------------------------------------------------
// SLICE-LEVEL extension
// FILE: src/<Solution>/Agents/WeatherAgent/WeatherAgentExtensions.cs
//
// Registers everything this one agent needs. The host never calls this
// directly — the project-level composer does.
// ---------------------------------------------------------------------------

public static class WeatherAgentExtensions
{
    public static IServiceCollection AddWeatherAgent(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<AzureAIFoundryOptions>()
            .Bind(config.GetSection("AzureAIFoundry"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<WeatherTools>();

        services.AddSingleton<AIAgent>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AzureAIFoundryOptions>>().Value;

            // ChatCompletionsClient targets the Azure AI Model Inference API.
            // Works with any model deployed in the Foundry account — OpenAI GPT,
            // Microsoft Phi, Meta Llama, etc. — without changing this code.
            // DefaultAzureCredential uses 'az login' locally and managed identity
            // in Azure. No API keys stored anywhere.
            // UseOpenTelemetry() activates GenAI semantic-convention spans:
            //   gen_ai.usage.input_tokens, gen_ai.usage.output_tokens, gen_ai.request.model
            // It also nests tool-call spans under the parent LLM span automatically.
            //
            // EnableSensitiveData controls whether prompt and completion content appear
            // as span attributes (gen_ai.input.messages / gen_ai.output.messages). This is
            // an architectural decision tied to the data the agent processes:
            //   - Workflow data is developer-accessible (code, logs, diffs) → true
            //   - Workflow data contains user PII or confidential business content → false
            //
            // OpaqueClientWrapper MUST be the outermost middleware (added last).
            // MAF's ChatClientAgent calls GetService<ChatCompletionsClient>() on the
            // IChatClient to obtain the raw Azure client, which bypasses all middleware.
            // The wrapper intercepts that call and returns null, forcing MAF to fall back
            // to the standard IChatClient interface so OpenTelemetryChatClient fires.
            var chatClient = new ChatCompletionsClient(
                    new Uri(opts.Endpoint),
                    new DefaultAzureCredential())
                .AsChatClient(opts.DeploymentName)
                .AsBuilder()
                .UseOpenTelemetry(configure: o => o.EnableSensitiveData = true)
                .Use(inner => new OpaqueClientWrapper(inner))
                .Build();

            // Pull tool methods from the registered class via reflection.
            var tools = sp.GetRequiredService<WeatherTools>();
            var aiTools = typeof(WeatherTools)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(m => m.GetCustomAttributes(typeof(DescriptionAttribute), false).Any())
                .Select(m => AIFunctionFactory.Create(m, tools))
                .Cast<AITool>()
                .ToList();

            var instructions = InstructionsLoader.LoadFromResource<AssemblyMarker>(
                "Agents.WeatherAgent.Instructions.WeatherAgent.md");

            // MAF 1.10+: instructions, name, and tools are constructor parameters.
            // There is no ChatClientAgentOptions.Instructions or .Tools property.
            return new ChatClientAgent(
                chatClient,
                instructions: instructions,
                name: "weather-agent",
                tools: aiTools);
        });

        return services;
    }
}

/// <summary>
/// Prevents callers from obtaining the underlying <see cref="ChatCompletionsClient"/>
/// via <see cref="IChatClient.GetService{T}"/>. MAF's <c>ChatClientAgent</c> calls
/// <c>GetService&lt;ChatCompletionsClient&gt;()</c> to bypass the middleware chain and call
/// Azure AI Inference directly. Blocking that call forces MAF to fall back to the standard
/// IChatClient interface, which goes through <see cref="OpenTelemetryChatClient"/>.
///
/// Place this as the outermost middleware (last in the builder chain).
/// </summary>
internal sealed class OpaqueClientWrapper(IChatClient inner) : DelegatingChatClient(inner)
{
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(ChatCompletionsClient))
            return null;
        return base.GetService(serviceType, serviceKey);
    }
}
