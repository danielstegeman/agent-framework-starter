// references/orchestrator-spans.cs
//
// Observability pattern for a deterministic workflow orchestrator.
// Shows the three-level span hierarchy you should produce for an agent run:
//
//   orchestrator.run                        ← this file
//     orchestrator.gather-context           ← this file
//     orchestrator.plan                     ← this file
//       gen_ai.chat  (model call)           ← emitted by UseOpenTelemetry()
//         execute_tool  (tool call)         ← emitted by UseOpenTelemetry()
//     orchestrator.implement  attempt=1     ← this file (per iteration)
//       gen_ai.chat  (model call)           ← emitted by UseOpenTelemetry()
//         execute_tool                      ← emitted by UseOpenTelemetry()
//
// The gen_ai.chat and execute_tool spans are automatic once .UseOpenTelemetry()
// is wired into the IChatClient builder — see builder-and-tools.cs. This file
// only shows the orchestration-level spans you add yourself.
//
// Extends the CQRS pattern from orchestrator-cqrs.cs. The ActivitySource name
// must match what is registered in your TelemetryRegistration — see
// otel-azuremonitor.cs (OrchestrationSourceName constant).

using System.Diagnostics;
using Microsoft.Agents.AI;
using Paramore.Brighter;
using Paramore.Brighter.Extensions.DependencyInjection;

// ── ActivitySource ────────────────────────────────────────────────────────────
// One static instance per assembly. The name must match the value passed to
// .AddSource(OrchestrationSourceName) in TelemetryRegistration.

public static class Telemetry
{
    // Must match TelemetryRegistration.OrchestrationSourceName.
    public static readonly ActivitySource Source = new("Agent.Orchestration");
}

// ── Command & Handler ─────────────────────────────────────────────────────────

public sealed record ReviewWorkItemCommand(int WorkItemId) : IRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

public sealed class ReviewWorkItemHandler : RequestHandlerAsync<ReviewWorkItemCommand>
{
    private readonly AIAgent _planAgent;
    private readonly AIAgent _reviewAgent;

    public ReviewWorkItemHandler(
        [FromKeyedServices("plan")] AIAgent planAgent,
        [FromKeyedServices("review")] AIAgent reviewAgent)
    {
        _planAgent = planAgent;
        _reviewAgent = reviewAgent;
    }

    public override async Task<ReviewWorkItemCommand> HandleAsync(
        ReviewWorkItemCommand cmd,
        CancellationToken ct = default)
    {
        // Root span for the whole orchestration run. Tags identify the work item
        // so you can filter in App Insights / Aspire dashboard without log diving.
        using var runActivity = Telemetry.Source.StartActivity("orchestrator.run");
        runActivity?.SetTag("agent.work_item_id", cmd.WorkItemId);

        var context = await GatherContextAsync(cmd.WorkItemId, ct).ConfigureAwait(false);
        var plan = await CreatePlanAsync(context, ct).ConfigureAwait(false);
        await ReviewAsync(plan, ct).ConfigureAwait(false);

        return await base.HandleAsync(cmd, ct);
    }

    private async Task<string> GatherContextAsync(int workItemId, CancellationToken ct)
    {
        // Child span for the context-gathering phase. No LLM calls here — purely
        // deterministic I/O. Tagging the work item id makes it easy to correlate
        // with the parent span in the trace view.
        using var activity = Telemetry.Source.StartActivity("orchestrator.gather-context");

        // ... fetch work item, related PRs, build logs, etc.
        await Task.Delay(0, ct);  // placeholder
        return $"Context for work item {workItemId}";
    }

    private async Task<string> CreatePlanAsync(string context, CancellationToken ct)
    {
        // Child span for the planning phase. The LLM call emitted by UseOpenTelemetry()
        // will appear as a child of this span automatically via Activity.Current.
        using var activity = Telemetry.Source.StartActivity("orchestrator.plan");

        var response = await _planAgent
            .RunAsync($"Plan a review based on: {context}", cancellationToken: ct)
            .ConfigureAwait(false);

        return response.Text;
    }

    private async Task ReviewAsync(string plan, CancellationToken ct)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // One span per iteration. agent.attempt lets you slice token usage
            // and duration per iteration in your metrics dashboard.
            using var activity = Telemetry.Source.StartActivity("orchestrator.implement");
            activity?.SetTag("agent.attempt", attempt);

            try
            {
                var prompt = attempt == 1
                    ? plan
                    : "The previous attempt did not complete. Refine and retry.";

                var response = await _reviewAgent
                    .RunAsync(prompt, cancellationToken: ct)
                    .ConfigureAwait(false);

                // Success — span ends here with no error status.
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && ex is not OperationCanceledException)
            {
                // Record the error on the span so it surfaces as a failed span in
                // the trace view, then let the loop retry.
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
        }
    }
}

// Wiring (in your host):
//   services.AddBrighter()
//       .AutoFromAssemblies(typeof(ReviewWorkItemHandler).Assembly);
//   services.AddKeyedSingleton<AIAgent>("plan",   (sp,_) => BuildPlanAgent(sp));
//   services.AddKeyedSingleton<AIAgent>("review", (sp,_) => BuildReviewAgent(sp));
//
// Dispatch:
//   await commandProcessor.SendAsync(new ReviewWorkItemCommand(12345));
