---
name: agent-sandbox-csharp
description: Implement the C# side of the sandboxing decision for a Microsoft Agent Framework agent — either the simplest provider-hosted CodeInterpreterToolDefinition path for Python-only execution, or the custom Azure Container Apps dynamic-sessions glue with an ISandbox abstraction, local Docker implementation, MAF AIFunction tools (run_command / read_file / write_file / git), custom session-container image, and IServiceCollection wiring. Use this skill after the sandboxing decision has been made (see agent-sandboxing) and you need the C# implementation — when the user asks "implement the sandbox", "add hosted code interpreter", "wire up dynamic sessions in C#", "add a run_command tool that runs in a sandbox", "how do I call the session pool from my MAF agent", or "how do I test the sandbox locally". Security-first — the agent's brain, credentials, and guardrails stay on the host; custom sessions stay credential-less.
---

# Agent Sandbox — C# Implementation

Read `agent-sandboxing` first. This skill has two implementation paths:

1. **Hosted Code Interpreter** — simplest path for Python-only execution. The provider hosts the sandbox; the agent adds `CodeInterpreterToolDefinition`.
2. **Custom ACA dynamic sessions** — power path for custom toolchains, filesystem/git/build loops, or tenant-controlled session pools. The MAF agent (the brain), model credentials, observability, and guardrails stay on the **host**. Each capability — `run_command`, `read_file`, `write_file`, `git` — is a host-side tool that proxies one operation into an isolated session.

Do not build the custom `ISandbox` glue just to run Python snippets if hosted Code Interpreter satisfies the decision.

## Option A — hosted Code Interpreter (simplest Python path)

Use this when `agent-sandboxing` selected provider-hosted Code Interpreter and the execution need is Python-only. Link to the official [MAF Code Interpreter](https://learn.microsoft.com/en-us/agent-framework/agents/tools/code-interpreter) docs before adding custom code.

```csharp
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

var projectEndpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME")
    ?? throw new InvalidOperationException("AZURE_AI_MODEL_DEPLOYMENT_NAME is not set.");

AIAgent agent = new AIProjectClient(new Uri(projectEndpoint), new DefaultAzureCredential())
    .AsAIAgent(
        model: deploymentName,
        instructions: "You can use Python for calculations and data/file analysis.",
        name: "analysis-agent",
        tools: [new CodeInterpreterToolDefinition()]);
```

Notes:
- The endpoint is the Foundry **project endpoint** from the portal (the `services.ai.azure.com/api/projects/<project>` URL), not the legacy `/models` endpoint.
- Keep the deployment name model-agnostic; configure it as `AZURE_AI_MODEL_DEPLOYMENT_NAME`.
- Provider-hosted execution still needs a decision record for data boundary, file upload/download handling, logging, retention, and approvals.

## Option B — custom ACA dynamic sessions (thin sandbox)

Use this when the decision requires custom toolchains, shell/git/build loops, per-conversation filesystem state, or tenant-controlled Azure Container Apps dynamic sessions.

```
Host container (agent app)                       Session (isolated runtime)
┌───────────────────────────────┐                ┌────────────────────────────┐
│ MAF agent (brain + guardrails) │                │ executor (ASP.NET minimal) │
│  run_command / read_file /     │  HTTP + Entra  │  bash · git · toolchain    │
│  write_file / git  (tools)     │ ─────────────▶ │  /workspace (per-convo)    │
│        │                        │  identifier = │                            │
│        ▼                        │  conversation │                            │
│   ISandbox                      │                └────────────────────────────┘
│    ├─ AcaSessionsSandbox (cloud)│
│    └─ LocalDockerSandbox (dev)  │ ── same image ──┘
└───────────────────────────────┘
```

- The agent never executes code itself. It calls a tool; the tool calls `ISandbox`.
- `ISandbox` has two implementations selected by environment; both drive the **same session-container image**.
- The session is keyed by **conversation id**, so files persist across turns within a conversation and stay isolated between conversations.

## Files in this skill

| Reference | What it is |
|---|---|
| [references/ISandbox.cs](references/ISandbox.cs) | The abstraction the tools depend on. |
| [references/aca-sessions-sandbox.cs](references/aca-sessions-sandbox.cs) | Cloud impl over the dynamic-sessions management API using `DefaultAzureCredential`. |
| [references/local-docker-sandbox.cs](references/local-docker-sandbox.cs) | Dev impl: a small session pool over the Docker Engine API (`Docker.DotNet`) mirroring allocate-on-demand + cooldown. |
| [references/sandbox-tools.cs](references/sandbox-tools.cs) | MAF tool methods over `ISandbox`, with per-call audit. |
| [references/wiring.cs](references/wiring.cs) | `IServiceCollection` registration; selects cloud vs local by environment. |
| [references/session-executor/Dockerfile](references/session-executor/Dockerfile) | The custom session-container image — non-root, bash + git + toolchain. |
| [references/session-executor/Executor.cs](references/session-executor/Executor.cs) | Minimal ASP.NET Core executor exposing `/execute` and `/files`. |

## Rules for custom sessions

- **The session stays credential-less.** Do not enable managed identity in the session pool. The host holds the `Azure ContainerApps Session Executor` role and calls the pool on behalf of the conversation. See `agent-secrets-identity`.
- **One image per coding environment.** If you need a Python-data env and a Node-web env, build two images and two pools (or select image per request). Author them from devcontainer definitions where possible so local dev and the sandbox share one toolchain source.
- **No egress from the session by default.** If a coding agent must reach a git remote, prefer brokering git through a host tool over opening session egress. If you must open it, allow-list the exact remote (decided in `agent-sandboxing`).
- **Bound every execution.** Pass a `CancellationToken` with a wall-clock timeout into every `ISandbox` call; the pool enforces CPU/memory/disk caps and idle cooldown (set in the Bicep — see `azure-container-apps-sessions-bicep`).
- **Audit every sandbox tool call** on the same trail as other tools (`agent-guardrails-safety`): tool name, conversation id, command/path (hashed in prod), exit code, duration. The references emit a trace span per call.
- **Tools are thin.** Each tool maps to exactly one `ISandbox` operation. No business logic in the tool; no `ISandbox` leakage into the agent's instructions.

## Wiring custom sessions

`ISandbox` is registered once; the implementation is chosen by configuration so local F5 uses Docker and the deployed app uses dynamic sessions:

```csharp
// Sandbox:Runtime = "Local" (dev) | "Aca" (cloud)
services.AddSandbox(config);          // see references/wiring.cs
services.AddSingleton<SandboxTools>();
```

The conversation id flows from the agent's session/thread into `ISandbox.GetOrCreateSessionAsync(conversationId, ct)` so it becomes the dynamic-sessions `identifier`.

Use MAF's native tool/function patterns rather than wrapper libraries. Tool methods are ordinary methods with `[Description]` metadata and `CancellationToken`s; convert/register them the same way as the rest of the agent's tools (`maf-csharp-implementation`) or follow the [MAF tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/) docs.

## Observability notes

MAF has native OpenTelemetry support. Do not add the old client-wrapper workaround. Choose one instrumentation layer (chat client or agent) to avoid duplicate prompt/response spans, register the source with `.AddSource(...)`, and treat `EnableSensitiveData` as a data-policy decision, not a dev/prod toggle. The sandbox references add a separate `Agent.Sandbox` activity source for execution spans; register it with your tracer provider.

## Hand-off

- Hosted Code Interpreter data/retention/tool policy → `agent-sandboxing` decision record.
- Session-pool resource, scaling, network, RBAC → `azure-container-apps-sessions-bicep`.
- Host identity + the Session Executor role assignment → `agent-secrets-identity`.
- Tool-call audit sink and retention → `agent-guardrails-safety`.
- General agent/tool wiring conventions → `maf-csharp-implementation`.
