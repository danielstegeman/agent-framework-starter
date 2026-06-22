// references/otel-azuremonitor.cs
//
// Telemetry wiring for a code-first agent. Captures:
//   - LLM calls with GenAI semantic conventions (gen_ai.usage.input_tokens,
//     gen_ai.usage.output_tokens, gen_ai.request.model, gen_ai.system)
//   - Message content (prompt / completion) when EnableSensitiveData = true
//   - Tool call spans as children of the LLM span — hierarchy comes for free
//   - Custom orchestration spans (see orchestrator-spans.cs)
//   - HTTP calls (Azure OpenAI, downstream APIs)
//   - Logs as OTel log records
//   - Token-usage metrics: gen_ai.client.token.usage counter,
//     gen_ai.client.operation.duration histogram
//
// Exporters: Azure Monitor (App Insights) when a connection string is
// configured; OTLP otherwise (useful for Aspire dashboard and local Jaeger).
//
// IMPORTANT: This file wires the OTel SDK and registers activity sources.
// It does NOT enable LLM span emission on its own. You MUST also call
// .UseOpenTelemetry() on the IChatClient builder in each agent registration —
// see builder-and-tools.cs for the pattern.

using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

public static class TelemetryRegistration
{
    /// <summary>
    /// ActivitySource name for custom orchestration spans. Register an
    /// <see cref="System.Diagnostics.ActivitySource"/> with this name in your
    /// orchestrator and use it to emit phase spans — see orchestrator-spans.cs.
    /// </summary>
    public const string OrchestrationSourceName = "Agent.Orchestration";

    public static IServiceCollection AddAgentTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var appInsightsConn = configuration["ApplicationInsights:ConnectionString"]
            ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
        var serviceName = configuration["OpenTelemetry:ServiceName"]
            ?? Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")
            ?? "agent";

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddHttpClientInstrumentation()
                    // Microsoft.Extensions.AI emits spans when .UseOpenTelemetry() is
                    // called on the IChatClient builder. Subscribing here ensures those
                    // spans are picked up by the configured exporter.
                    .AddSource("Microsoft.Extensions.AI")
                    .AddSource("Microsoft.Agents.AI")
                    // Your orchestration phases — emitted by the ActivitySource you
                    // create in the orchestrator (see orchestrator-spans.cs).
                    .AddSource(OrchestrationSourceName);

                if (!string.IsNullOrWhiteSpace(appInsightsConn))
                    tracing.AddAzureMonitorTraceExporter(o => o.ConnectionString = appInsightsConn);
                else
                    tracing.AddOtlpExporter();   // honours OTEL_EXPORTER_OTLP_ENDPOINT
            })
            .WithMetrics(metrics =>
            {
                // Subscribes to the gen_ai.client.token.usage counter and
                // gen_ai.client.operation.duration histogram emitted by
                // Microsoft.Extensions.AI's OpenTelemetryChatClient.
                metrics.AddMeter("Microsoft.Extensions.AI");

                if (!string.IsNullOrWhiteSpace(appInsightsConn))
                    metrics.AddAzureMonitorMetricExporter(o => o.ConnectionString = appInsightsConn);
                else
                    metrics.AddOtlpExporter();
            })
            .WithLogging(logging =>
            {
                if (!string.IsNullOrWhiteSpace(appInsightsConn))
                    logging.AddAzureMonitorLogExporter(o => o.ConnectionString = appInsightsConn);
                else
                    logging.AddOtlpExporter();
            });

        return services;
    }
}
