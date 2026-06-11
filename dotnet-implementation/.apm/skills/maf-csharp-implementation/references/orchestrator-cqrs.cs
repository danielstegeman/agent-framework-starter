// references/orchestrator-cqrs.cs
//
// Workflow orchestration pattern for multi-step agent flows.
// Uses Paramore.Brighter for in-process command dispatch — keeps the
// orchestration code separate from the host (console / web / function).
//
// When to reach for this:
//   - Multiple agents need to hand off in a deterministic sequence.
//   - You want the workflow definition in one place, not spread across host code.
//   - You want CQRS-style commands you can later route to a queue.
//
// When NOT to reach for this:
//   - Single agent, single tool, single prompt -> just call agent.RunAsync.
//   - Free-form multi-turn chat -> ChatClientAgent handles it.

using Microsoft.Agents.AI;
using Paramore.Brighter;
using Paramore.Brighter.Extensions.DependencyInjection;

public sealed record ReviewWorkItemCommand(int WorkItemId) : IRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Activity? Span { get; set; }
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
        var plan = await _planAgent.RunAsync(
            $"Plan a review for work item {cmd.WorkItemId}.", cancellationToken: ct);

        await _reviewAgent.RunAsync(plan.Text, cancellationToken: ct);

        return await base.HandleAsync(cmd, ct);
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
