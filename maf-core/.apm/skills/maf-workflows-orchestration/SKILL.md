---
name: maf-workflows-orchestration
description: >
  Decision guidance and implementation pointers for Microsoft Agent Framework
  workflow orchestration in C#/.NET: graph-based WorkflowBuilder/executors,
  agents in workflows, the five orchestration patterns (sequential, concurrent,
  handoff, group-chat, magentic), human-in-the-loop, checkpointing and resume.
  Use this skill when the user asks "multi-agent orchestration", "how do I run
  agents in a workflow", "how should I use WorkflowBuilder", "what are workflow
  executors", "single agent vs graph workflow", "human approval in an agent
  workflow", "checkpoint and resume a workflow", or any equivalent question
  about coordinating Microsoft Agent Framework agents.
---

# Microsoft Agent Framework — Workflows and Orchestration

Use Microsoft Agent Framework workflows when the agent experience needs explicit, inspectable control flow instead of a single open-ended conversation.

Start from the official docs and samples; do not copy large reference implementations into the repo:

- [Workflow capabilities](https://learn.microsoft.com/en-us/agent-framework/workflows/)
- [Workflow concepts](https://learn.microsoft.com/en-us/agent-framework/concepts/workflows/)
- [WorkflowBuilder and execution](https://learn.microsoft.com/en-us/agent-framework/concepts/workflows/builder-and-execution)
- [Executors](https://learn.microsoft.com/en-us/agent-framework/concepts/workflows/executors)
- [Agents in workflows](https://learn.microsoft.com/en-us/agent-framework/workflows/agents-in-workflows)
- [.NET workflow samples](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/03-workflows)

## Decision guidance

| Need | Choose | Why |
|---|---|---|
| One persona, one conversation, optional tools, no fixed step order | **Single `AIAgent` / `ChatClientAgent`** | Keep the app simple; use normal session persistence and tool calls. |
| Repeatable business process with typed steps, branching, fan-out/fan-in, human gates, or resume | **Graph workflow** with `WorkflowBuilder` and executors | The graph makes control flow explicit and observable. |
| Multiple agents must collaborate in a common pattern | **Built-in orchestration** | Prefer the framework's orchestration patterns over a hand-rolled dispatcher. |
| Pure deterministic app workflow with no LLM coordination | **Ordinary application code / state machine** | Do not introduce MAF workflow APIs just to model non-agent business logic. |

## Graph workflows

In .NET, graph workflows are built with `WorkflowBuilder`. Executors own the typed unit of work; edges and conditions route messages between executors.

Tiny shape only:

```csharp
var workflow = new WorkflowBuilder()
    .AddExecutor(...)
    .AddEdge(...)
    .Build();
```

Use graph workflows when you need:

- A process that can be explained as nodes and edges.
- Deterministic routing around LLM-powered steps.
- Fan-out/fan-in or conditional branches.
- Shared state and workflow events.
- Checkpoints at workflow boundaries.

For code details, use the [builder/execution](https://learn.microsoft.com/en-us/agent-framework/concepts/workflows/builder-and-execution), [executors](https://learn.microsoft.com/en-us/agent-framework/concepts/workflows/executors), and [.NET samples](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/03-workflows) instead of embedding a full local template.

## Built-in orchestration patterns

Use the [orchestrations guide](https://learn.microsoft.com/en-us/agent-framework/workflows/orchestrations/) for current APIs. It covers five patterns:

| Pattern | Use when |
|---|---|
| **Sequential** | Agents must run one after another in a known order. |
| **Concurrent** | Independent agents can work in parallel before results are combined. |
| **Handoff** | The active agent should transfer control based on conversation context. |
| **Group-chat** | Agents should collaborate in a shared conversation. |
| **Magentic** | A manager agent should dynamically coordinate specialized agents. |

Prefer these built-ins before introducing a custom CQRS orchestrator. Use custom orchestration code only when the built-in patterns cannot express the domain-specific routing, persistence, or integration requirements.

## Agents in workflows

Agents can be steps inside a graph workflow. Use [Agents in workflows](https://learn.microsoft.com/en-us/agent-framework/workflows/agents-in-workflows) when a workflow needs a mix of deterministic executors and LLM-driven agent steps.

Good fits:

- Gather context deterministically, then ask an agent to reason over it.
- Route a structured result to review, write-back, or retry executors.
- Wrap an agent step with approval, audit, or checkpoint behavior.

## Human-in-the-loop

Use [Human-in-the-loop](https://learn.microsoft.com/en-us/agent-framework/workflows/human-in-the-loop) when the workflow must pause for approval, missing information, or a human decision.

Use HITL for:

- Approval before mutating external systems.
- Clarifying missing or ambiguous requirements.
- Review of high-impact generated content.

Keep the approval point explicit in the graph. Do not hide human approval inside a tool implementation where the workflow cannot observe or resume it.

## Checkpoints and resume

Use [Checkpoints and resuming](https://learn.microsoft.com/en-us/agent-framework/workflows/checkpoints) when workflows may be interrupted by approvals, host restarts, long-running work, or retries.

Design rules:

- Checkpoint at meaningful workflow boundaries, not after every line of code.
- Make external writes idempotent or record enough state to avoid duplicate effects after resume.
- Persist correlation IDs, actor identity, and approval decisions with the checkpoint state.

## Hand-off

- C# implementation shape and DI wiring -> `maf-csharp-implementation`.
- Tool surface decisions -> `maf-mcp-tools`.
- Hosting workflows as services -> hosting/Aspire/Azure skills in this package.

## Official Documentation

- [Workflow capabilities](https://learn.microsoft.com/en-us/agent-framework/workflows/)
- [Workflow concepts](https://learn.microsoft.com/en-us/agent-framework/concepts/workflows/)
- [WorkflowBuilder and execution](https://learn.microsoft.com/en-us/agent-framework/concepts/workflows/builder-and-execution)
- [Executors](https://learn.microsoft.com/en-us/agent-framework/concepts/workflows/executors)
- [Agents in workflows](https://learn.microsoft.com/en-us/agent-framework/workflows/agents-in-workflows)
- [Human-in-the-loop](https://learn.microsoft.com/en-us/agent-framework/workflows/human-in-the-loop)
- [Checkpoints and resuming](https://learn.microsoft.com/en-us/agent-framework/workflows/checkpoints)
- [Workflow orchestrations](https://learn.microsoft.com/en-us/agent-framework/workflows/orchestrations/)
- [.NET samples](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples)
- [.NET workflow samples](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/03-workflows)
