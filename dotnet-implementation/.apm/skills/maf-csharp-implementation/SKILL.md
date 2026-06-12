---
name: maf-csharp-implementation
description: Reference patterns for implementing a code-first agent in C# with Microsoft Agent Framework — project structure (vertical-slice + orchestrator), tools as separate projects, IServiceCollection wiring, embedded markdown instructions, multi-turn session handling, middleware pipeline, and structured outputs. Use this skill when the user is writing or reviewing C#/.NET Microsoft Agent Framework code, asks "how should I structure my MAF agent solution", "where do tools go", "how do I load instructions from markdown", "how do I add middleware to my agent", "what's the right project layout for a C# agent", or any equivalent question about MAF code organisation. The skill explicitly recommends using MAF directly — do NOT build a wrapper library around AIAgent until two or more agents share the same abstractions.
---

# Microsoft Agent Framework — C# Implementation Patterns

Patterns for building maintainable code-first agents on Microsoft Agent Framework (`Microsoft.Agents.AI*`). Distilled into rules you can apply without ceremony.

## Core principle

> **Use Microsoft Agent Framework directly. Don't build a wrapper.**

The temptation is to wrap `AIAgent` in a `MyTeamAgent` class with a fluent builder, "mandatory" middleware, and a custom DI extension. Resist it on day one. The wrapper:
- Adds an indirection every reader has to follow.
- Couples your solution to your wrapper's release cadence.
- Becomes a maintenance burden once MAF evolves.

Promote a wrapper or base class **only when two or more agents in production share the same abstractions** and you can name them. Until then, register `AIAgent` directly in DI and put cross-cutting concerns into middleware.

## Solution layout

```
<solution>.sln
├── src/
│   ├── <Agent>.Host/              # console / web / function entrypoint
│   │   ├── Program.cs
│   │   └── appsettings.json
│   ├── <Agent>/                   # agent classlib — one subfolder per agent inside
│   │   ├── ServiceCollectionExtensions.cs   # project-level composer
│   │   ├── InstructionsLoader.cs
│   │   ├── TelemetryRegistration.cs
│   │   ├── AssemblyMarker.cs
│   │   └── Agents/
│   │       └── <AgentName>/       # vertical slice — one per agent
│   │           ├── <AgentName>Extensions.cs  # slice DI wiring
│   │           ├── Instructions/
│   │           │   └── <AgentName>.md        # EmbeddedResource
│   │           └── Orchestrator.cs           # optional: only if deterministic workflow
│   └── <Agent>.Tools.<Provider>/  # one tools project per external system
│       ├── ServiceCollectionExtensions.cs
│       ├── Configuration/
│       └── *Tools.cs
└── tests/
    ├── <Agent>.Tests/             # unit tests
    └── <Agent>.Evaluation.Tests/  # eval suite (see agent-evaluation-strategy)
```

Rules:
- **Host is throwaway.** It wires DI, parses input, calls the agent, prints output. No business logic.
- **Tools live in their own project per integration.** They have no reference to the agent project. The agent depends on tools, not the reverse.
- **One assembly marker per project that owns embedded resources.** It anchors `LoadFromResource<TMarker>(...)` lookups.
- **One folder per agent.** Everything an agent needs — instructions, DI wiring, optional orchestrator — lives inside `Agents/<AgentName>/`. Adding a new agent means adding one folder and one line in the project-level composer.

See [references/builder-and-tools.cs](references/builder-and-tools.cs) for the canonical wiring.

## Vertical slice per agent

Each agent is a self-contained vertical slice inside `Agents/<AgentName>/`:

| File | Responsibility |
|---|---|
| `<AgentName>Extensions.cs` | DI wiring for this agent: options, tools, `AIAgent` singleton |
| `Instructions/<AgentName>.md` | Agent persona prompt (EmbeddedResource) |
| `Orchestrator.cs` | CQRS handler (Brighter/MediatR) — add only when the workflow is deterministic |

The project-level `ServiceCollectionExtensions.cs` is the **composer** — it does nothing except call each slice's extension and telemetry:

```csharp
// <Agent>/ServiceCollectionExtensions.cs
public static IServiceCollection AddWeatherAgentProject(
    this IServiceCollection services, IConfiguration config)
{
    services.AddAgentTelemetry(config);
    services.AddWeatherAgent(config);       // Agents/WeatherAgent/WeatherAgentExtensions.cs
    // services.AddForecastAgent(config);   // add more slices here as the product grows
    return services;
}
```

**Adding an agent** checklist:
1. Create `Agents/<NewAgent>/` folder.
2. Add `<NewAgent>Extensions.cs` (copy the existing slice, rename types).
3. Add `Instructions/<NewAgent>.md` with the persona prompt.
4. Register in the project-level composer: `services.Add<NewAgent>(config)`.
5. That's it — host wiring is untouched.

Do **not** introduce a shared base class or `AgentFactory` until two slices genuinely share the same abstractions and you can name them.

## Tool authoring

Public instance methods with `[System.ComponentModel.Description]` on the method and every parameter. The tool class itself is registered in DI; the agent build-up reflects over it and produces `AIFunction` instances via `AIFunctionFactory.Create(...)`.

```csharp
public sealed class WorkItemTools
{
    [Description("Fetches a work item by its numeric ID.")]
    public async Task<WorkItem> GetWorkItem(
        [Description("Numeric work item ID")] int id,
        CancellationToken ct = default) { ... }
}
```

Rules:
- One tools class per cohesive integration (`WorkItemTools`, `PullRequestTools`).
- Constructor injection only — no static state, no service-locator.
- Methods that mutate state should return enough info for the agent to confirm success without re-reading.
- Never accept `IServiceProvider` as a parameter; that signals the design has gone wrong.

**Anti-pattern: shared "context store" between tools.** It's tempting to populate a per-scope `ContextStore` from an orchestrator and have downstream tools read from it. This couples tools to invocation order and breaks the moment a tool is called outside the workflow. If tools need the same data, either (a) fetch it independently and cache via `IMemoryCache`, or (b) pass it as a tool argument explicitly.

## Instructions as embedded markdown

Agent prompts belong in `.md` files, not C# string literals. Mark them as `<EmbeddedResource>` in the csproj. Load via the manifest-name-suffix pattern.

```xml
<ItemGroup>
  <EmbeddedResource Include="Agents\**\*.md" />
</ItemGroup>
```

```csharp
var instructions = InstructionsLoader.LoadFromResource<AssemblyMarker>(
    "Agents.WeatherAgent.Instructions.WeatherAgent.md");
```

See [references/instructions-embedded.cs](references/instructions-embedded.cs).

Rules:
- One markdown file per agent persona.
- Filename = persona name. Folder structure = capability area.
- Treat markdown changes the same as code changes — code review, evaluation.
- Static skill / policy files that **change without redeploy** should live on disk (`CopyToOutputDirectory`), not as embedded resources. Register them through an `AIContextProvider` so the prompt is augmented at request time.

## DI wiring — the ServiceCollection extension pattern

Two levels of extensions cooperate:

**Slice-level** — lives in `Agents/<AgentName>/<AgentName>Extensions.cs`. Wires exactly one agent:

```csharp
// Agents/WeatherAgent/WeatherAgentExtensions.cs
public static IServiceCollection AddWeatherAgent(
    this IServiceCollection services, IConfiguration config)
{
    services.AddOptions<AzureAIFoundryOptions>()
        .Bind(config.GetSection("AzureAIFoundry"))
        .ValidateDataAnnotations()
        .ValidateOnStart();
    services.AddSingleton<WeatherTools>();
    services.AddSingleton<AIAgent>(sp => BuildWeatherAgent(sp));
    return services;
}
```

**Project-level composer** — lives in `<Agent>/ServiceCollectionExtensions.cs`. Calls each slice and telemetry:

```csharp
// <Agent>/ServiceCollectionExtensions.cs
public static IServiceCollection AddWeatherAgentProject(
    this IServiceCollection services, IConfiguration config)
{
    services.AddAgentTelemetry(config);
    services.AddWeatherAgent(config);
    return services;
}
```

Rules:
- Each slice owns its own options binding, tools registration, and `AIAgent` singleton.
- The composer adds no logic — it is a call list.
- Options bound with `ValidateDataAnnotations().ValidateOnStart()` so misconfiguration surfaces at startup.
- Authentication via `DefaultAzureCredential` for all Azure SDK clients (works locally with `az login`, in Azure with managed identity).
- Agents registered as `Singleton` (chat client + options are immutable); tools as `Scoped` if they hold per-request state, otherwise `Singleton`.

## Multi-turn sessions

`AIAgent` exposes session APIs — use them rather than rolling your own list of `ChatMessage`.

```csharp
var session = await agent.GetNewThreadAsync();
await foreach (var update in agent.RunStreamingAsync(prompt, session))
    Console.Write(update.Text);

// Persist between processes:
var serialized = await agent.SerializeThreadAsync(session);
// ...store somewhere...
var restored = await agent.DeserializeThreadAsync(serialized);
```

For a web/event host, persist `serialized` to Cosmos / Redis / SQL keyed by user + conversation id.

## Middleware

Two middleware surfaces:
1. **Agent middleware** — wraps `RunAsync`. Use for: span enrichment, request-level audit, retries that need access to the whole response.
2. **IChatClient middleware** — wraps every LLM call. Use for: token accounting, prompt-injection check on user messages, output content filter.

Compose via `ChatClientAgentOptions.Use(...)` and `new ChatClientBuilder(inner).Use(...)` respectively. For the guardrail middleware pattern, see the `agent-guardrails-safety` skill and its `guardrail-middleware.cs` reference in the **quality-safety** package.

## Workflow orchestration

When the agent's job has deterministic structure (always: plan -> review -> write back), put the structure in a CQRS handler and let the agent fill in the LLM-driven steps. Paramore.Brighter is the lightweight default; MediatR works too.

See [references/orchestrator-cqrs.cs](references/orchestrator-cqrs.cs).

Heuristics:
- 1 agent + 1 tool + 1 prompt -> no orchestrator, just call the agent.
- 2+ agents handing off in a fixed order -> orchestrator.
- Free-form multi-turn -> session + single agent, no orchestrator.

## Structured outputs

Prefer JSON-schema-constrained responses over parsing free-form text.

```csharp
var result = await agent.RunAsync<MyDto>("Summarise the work item as JSON.");
```

Define `MyDto` as a record with `[Description]` on every property. The framework infers the schema and instructs the model accordingly. Validate after deserialisation; the schema isn't a contract.

## What this skill does NOT cover

- Project scaffolding commands -> `dotnet-agent-bootstrap`.
- Telemetry exporter setup -> `agent-infrastructure-overview` + `azure-container-apps-bicep`.
- Eval tests -> `agent-evaluation-strategy`.
- Guardrail middleware implementations -> `agent-guardrails-safety`.
- Auth & secrets -> `agent-secrets-identity`.
- Running model-generated code/commands in an isolated sandbox -> `agent-sandbox-csharp` (the `ISandbox` abstraction, dynamic-sessions + local Docker implementations, and `run_command` / `read_file` / `write_file` / `git` tools).
