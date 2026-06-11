// references/otel-azuremonitor.cs
//
// Telemetry wiring for a code-first agent. Tracks:
//   - Agent runs (span per RunAsync invocation)
//   - Tool invocations (span per AIFunction call)
//   - HTTP calls (Azure OpenAI, downstream APIs)
//   - Logs as OTel log records
//
// Exporters: Azure Monitor (App Insights) when a connection string is
// configured; OTLP otherwise (useful for Aspire dashboard and local Jaeger).

using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

public static class TelemetryRegistration
{
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
                    .AddSource("Microsoft.Extensions.AI")
                    .AddSource("Microsoft.Agents.AI")
                    .AddSource("Agent.Tools");   // your own ActivitySource for tool spans

                if (!string.IsNullOrWhiteSpace(appInsightsConn))
                {
                    tracing.AddAzureMonitorTraceExporter(o => o.ConnectionString = appInsightsConn);
                }
                else
                {
                    tracing.AddOtlpExporter();   // honours OTEL_EXPORTER_OTLP_ENDPOINT
                }
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

// Tool-call audit pattern: a middleware that wraps every AIFunction invocation
// in an Activity. Add it via ChatClientAgentOptions.Use(...) when building
// the agent, or via a delegating IChatClient.
