---
name: maf-csharp-implementation
description: Reference guidance for implementing code-first agents in C# with Microsoft Agent Framework — minimal MAF-idiomatic project structure, optional scaling layouts, AIProjectClient.AsAIAgent Foundry wiring, tools, instructions, sessions, middleware, structured outputs, observability, and workflow orchestration options. Use this skill when the user is writing or reviewing C#/.NET Microsoft Agent Framework code, asks how to structure a MAF agent solution, where tools go, how to load instructions, how to add middleware, how to wire Foundry, or any equivalent question about MAF code organisation. The skill recommends using MAF directly — do not build a wrapper library around AIAgent until two or more agents share named abstractions.
---

# Microsoft Agent Framework — C# Implementation Patterns

Patterns for building maintainable code-first agents on Microsoft Agent Framework (`Microsoft.Agents.AI*`) without adding unnecessary architecture.

## Core principle

> **Use Microsoft Agent Framework directly. Don't build a wrapper.**

The temptation is to wrap `AIAgent` in a `MyTeamAgent` class with a fluent builder, mandatory middleware, and a custom DI extension. Resist it on day one. The wrapper:
- Adds an indirection every reader has to follow.
- Couples your solution to your wrapper's release cadence.
- Becomes a maintenance burden once MAF evolves.

Promote a wrapper or base class only when two or more agents in production share the same named abstractions. Until then, register `AIAgent` directly in DI and put cross-cutting concerns into MAF middleware or OpenTelemetry.

## Packages and Foundry baseline

Prefer the current Foundry provider path over the old Azure AI Inference chat-completions wiring:

- `Microsoft.Agents.AI` — GA core (`AIAgent`, `ChatClientAgent`, sessions).
- `Microsoft.Agents.AI.Foundry` — prerelease Foundry provider (`AIProjectClient.AsAIAgent(...)`).
- `Azure.AI.Projects` — prerelease (`AIProjectClient`).
- `Azure.Identity` — GA (`DefaultAzureCredential`, `ManagedIdentityCredential`).

Configuration convention:

```text
AZURE_AI_PROJECT_ENDPOINT=https://<service>.services.ai.azure.com/api/projects/<project>
AZURE_AI_MODEL_DEPLOYMENT_NAME=<deployment-name>
```

The endpoint is the **project endpoint**, not the legacy `/models` inference endpoint. Keep the deployment name model-agnostic; don't bake a specific model name into reusable templates.

Minimal Foundry construction:

```csharp
AIAgent agent = new AIProjectClient(
        new Uri(projectEndpoint),
        new DefaultAzureCredential())
    .AsAIAgent(
        model: deploymentName,
        instructions: instructions,
        name: "weather-agent",
        tools: [ /* AIFunctionFactory.Create(...) */ ]);
```

For hosted agent records, use the Foundry provider's `aiProjectClient.AsAIAgent(agentRecord)` pattern. See the [Microsoft Foundry model provider](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/microsoft-foundry) docs and the [.NET samples](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples).

## Solution layout: start minimal, scale deliberately

A simple MAF-idiomatic default is enough for one agent and a small set of tools:

```text
<solution>.sln
├── src/
│   ├── <Solution>.Host/          # console / web / function entrypoint
│   └── <Solution>.Agent/         # DI wiring, instructions, local tools, agent factory
│       ├── ServiceCollectionExtensions.cs
│       ├── Instructions/
│       └── Tools/
└── tests/
    ├── <Solution>.Tests/
    └── <Solution>.Evaluation.Tests/   # optional
```

Guidance:
- Keep the host thin: configuration, DI, input/output, and a call to the agent.
- Keep agent instructions in markdown files so prompt changes are reviewable.
- Put small local tools beside the agent when that is easiest to understand.
- Register options with validation so misconfiguration fails at startup.
- Use `DefaultAzureCredential` locally and managed identity in Azure.

Scaling options are tradeoffs, not requirements:

| Option | Useful when | Tradeoff |
|---|---|---|
| `Agents/<AgentName>/` vertical slices | Multiple agents evolve independently | More folders and DI extension methods |
| Separate `<Solution>.Tools.<Integration>` projects | Tool integrations have their own SDKs, tests, release cadence, or owners | More project references and package management |
| Dedicated orchestration/workflow project | Deterministic workflows outgrow one agent registration | More boundary decisions |

The official [.NET samples](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples) show several layouts; follow the smallest one that fits the current product.

## Tool authoring

Use the native tool/function patterns in the [Tools overview](https://learn.microsoft.com/en-us/agent-framework/agents/tools/). Practical guidance:

- One tool class per cohesive capability or integration.
- Describe each callable method and parameter clearly.
- Prefer constructor injection; avoid static state and service-locator access.
- Mutating tools should return enough information for the agent to confirm success without an immediate re-read.
- If several tools need the same data, pass it explicitly or cache behind a normal service; don't depend on hidden call order.

## Instructions as markdown

The [get-started guide](https://learn.microsoft.com/en-us/agent-framework/get-started/your-first-agent) covers basic instructions and tools. In real projects, keep instructions as reviewed markdown files. Embedded resources are fine for prompts that ship with the binary:

```xml
<ItemGroup>
  <EmbeddedResource Include="Instructions\**\*.md" />
</ItemGroup>
```

If a policy, skill file, or knowledge snippet must change without redeploying, load it from storage or disk and register it through a context-provider pattern instead of embedding it.

## DI wiring

Use normal `IServiceCollection` extension methods to keep startup readable. A minimal project-level composer can register telemetry, options, tools, and the `AIAgent` singleton. Split this into per-agent extension methods only when multiple agents make that clearer.

Do not use `Azure.AI.Inference`, `ChatCompletionsClient`, or `.AsChatClient()` as the primary Foundry path for new MAF code. Use `AIProjectClient.AsAIAgent(...)` as shown above and in the [Foundry provider documentation](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/microsoft-foundry).

## Multi-turn sessions

Use MAF session APIs instead of maintaining your own `List<ChatMessage>` history. See [Conversations & Memory](https://learn.microsoft.com/en-us/agent-framework/concepts/agents/conversations/) and the [multi-turn get-started step](https://learn.microsoft.com/en-us/agent-framework/get-started/multi-turn). For web or event-driven hosts, persist serialized session state in a store keyed by user and conversation id.

## Middleware

MAF has native middleware surfaces; use them directly rather than hiding them in a wrapper:

- Agent middleware wraps agent runs and is useful for request-level audit, retries, approval, and span enrichment.
- Chat-client middleware wraps individual LLM calls and is useful for token accounting and model-call guardrails.

See the [Agent Middleware](https://learn.microsoft.com/en-us/agent-framework/concepts/agents/middleware/) docs. For safety/guardrail implementations, use the `agent-guardrails-safety` skill.

## Workflow orchestration

An orchestrator is optional. Heuristics:

- One agent + one prompt + a few tools -> call the agent directly.
- Multi-turn free-form interaction -> use MAF sessions.
- Deterministic multi-step or multi-agent flow -> consider MAF **Workflows** first.
- Existing application already standardizes on Paramore.Brighter or MediatR -> CQRS orchestration can still be a good fit, but keep the agent code MAF-native.

For native orchestration, see the sibling [maf-workflows-orchestration](../maf-workflows-orchestration/SKILL.md) skill, [Workflow capabilities](https://learn.microsoft.com/en-us/agent-framework/workflows/), and [Workflow orchestrations](https://learn.microsoft.com/en-us/agent-framework/workflows/orchestrations/).

## Structured outputs

Prefer schema-constrained structured outputs over parsing free-form text. Define DTOs with clear property descriptions, validate the result after deserialization, and keep fallback behavior explicit. See [Producing Structured Outputs with agents](https://learn.microsoft.com/en-us/agent-framework/agents/structured-outputs).

## Observability

Use native OpenTelemetry support; do not add client wrappers to bypass provider internals.

Choose the layer you want to instrument:

```csharp
const string SourceName = "Contoso.WeatherAgent";

var observedChatClient = chatClient
    .AsBuilder()
    .UseOpenTelemetry(
        sourceName: SourceName,
        configure: options => options.EnableSensitiveData = false)
    .Build();
```

or:

```csharp
var observedAgent = new ChatClientAgent(chatClient, instructions, name: "weather-agent")
    .WithOpenTelemetry(
        sourceName: SourceName,
        configure: options => options.EnableSensitiveData = false);
```

Register the same source with your tracer provider:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(SourceName));
```

If you don't set a source name, MAF uses its default source. Enabling OpenTelemetry on both the chat client and the agent can duplicate prompt/response data, so make an explicit choice. `EnableSensitiveData` is an architectural decision about the data the agent processes, not a dev/prod toggle. See [Observability](https://learn.microsoft.com/en-us/agent-framework/agents/observability).

For deterministic orchestration phases, create your own `ActivitySource`, register it with `.AddSource(...)`, and start spans around major workflow steps. The MAF `gen_ai` spans will attach beneath the current activity.

## What this skill does NOT cover

- Project scaffolding commands -> `dotnet-agent-bootstrap`.
- Workflow deep dive -> [maf-workflows-orchestration](../maf-workflows-orchestration/SKILL.md).
- Telemetry exporter setup -> `agent-infrastructure-overview` + `azure-container-apps-bicep`.
- Eval tests -> `agent-evaluation-strategy`.
- Guardrail middleware implementations -> `agent-guardrails-safety`.
- Auth & secrets -> `agent-secrets-identity`.

## Official Documentation

- [Agent Framework hub](https://learn.microsoft.com/en-us/agent-framework/)
- [Your First Agent](https://learn.microsoft.com/en-us/agent-framework/get-started/your-first-agent)
- [Microsoft Foundry model provider](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/microsoft-foundry)
- [Tools overview](https://learn.microsoft.com/en-us/agent-framework/agents/tools/)
- [Agent Middleware](https://learn.microsoft.com/en-us/agent-framework/concepts/agents/middleware/)
- [Structured outputs](https://learn.microsoft.com/en-us/agent-framework/agents/structured-outputs)
- [Observability](https://learn.microsoft.com/en-us/agent-framework/agents/observability)
- [Workflow capabilities](https://learn.microsoft.com/en-us/agent-framework/workflows/)
- [.NET samples](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples)
