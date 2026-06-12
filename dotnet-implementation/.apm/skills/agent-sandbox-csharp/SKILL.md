---
name: agent-sandbox-csharp
description: Implement a code-execution sandbox for a C# / Microsoft Agent Framework agent — an ISandbox abstraction with an Azure Container Apps dynamic-sessions implementation for the cloud and a local Docker implementation for development, plus MAF AIFunction tools (run_command / read_file / write_file / git) that proxy into the session, the custom session-container image (Dockerfile + minimal executor), and IServiceCollection wiring that selects the runtime by environment. Use this skill after the sandboxing decision has been made (see agent-sandboxing) and you need the C# implementation — when the user asks "implement the sandbox", "wire up dynamic sessions in C#", "add a run_command tool that runs in a sandbox", "how do I call the session pool from my MAF agent", or "how do I test the sandbox locally". Security-first — the agent's brain, credentials, and guardrails stay on the host; the session stays credential-less.
---

# Agent Sandbox — C# Implementation (Model A: thin sandbox)

Implements the **thin-sandbox** execution model decided in `agent-sandboxing`: the MAF agent (the brain), its model credentials, observability, and guardrails all stay on the **host**. Each capability — `run_command`, `read_file`, `write_file`, `git` — is a host-side `AIFunction` that proxies one operation into an **isolated session** over HTTP. The session runs a minimal executor and holds no credentials.

Read `agent-sandboxing` first. This skill assumes the decision is: **Azure Container Apps dynamic sessions, custom container**, with a local Docker runtime for development.

## Architecture

```
Host container (ACA app)                         Session (Hyper-V isolated)
┌───────────────────────────────┐                ┌────────────────────────────┐
│ MAF agent (brain + guardrails) │                │ executor (ASP.NET minimal) │
│  run_command / read_file /     │  HTTP + Entra  │  bash · git · toolchain    │
│  write_file / git  (AIFunctions)│ ─────────────▶ │  /workspace (per-convo)    │
│        │                        │  identifier =  │                            │
│        ▼                        │  conversationId│                            │
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
| [references/aca-sessions-sandbox.cs](references/aca-sessions-sandbox.cs) | Cloud impl over the dynamic-sessions management API (`DefaultAzureCredential`, audience `https://dynamicsessions.io`). |
| [references/local-docker-sandbox.cs](references/local-docker-sandbox.cs) | Dev impl: a small session pool over the Docker Engine API (`Docker.DotNet`) mirroring allocate-on-demand + cooldown. |
| [references/sandbox-tools.cs](references/sandbox-tools.cs) | MAF `AIFunction` tools over `ISandbox`, with per-call audit. |
| [references/wiring.cs](references/wiring.cs) | `IServiceCollection` registration; selects cloud vs local by environment. |
| [references/session-executor/Dockerfile](references/session-executor/Dockerfile) | The custom session-container image — non-root, bash + git + toolchain. |
| [references/session-executor/Executor.cs](references/session-executor/Executor.cs) | Minimal ASP.NET Core executor exposing `/execute` and `/files`. |

## Rules

- **The session stays credential-less.** Do not enable managed-identity-in-session on the pool. The host holds the `Azure ContainerApps Session Executor` role and calls the pool on behalf of the conversation. See `agent-secrets-identity`.
- **One image per coding environment.** If you need a Python-data env and a Node-web env, build two images and two pools (or select image per request). Author them from devcontainer definitions where possible so local dev and the sandbox share one toolchain source.
- **No egress from the session by default.** If a coding agent must reach a git remote, prefer brokering git through a host tool over opening session egress. If you must open it, allow-list the exact remote (decided in `agent-sandboxing`).
- **Bound every execution.** Pass a `CancellationToken` with a wall-clock timeout into every `ISandbox` call; the pool enforces CPU/memory/disk caps and idle cooldown (set in the Bicep — see `azure-container-apps-sessions-bicep`).
- **Audit every sandbox tool call** on the same trail as other tools (`agent-guardrails-safety`): tool name, conversation id, command/path (hashed in prod), exit code, duration. The references emit a trace span per call.
- **Tools are thin.** Each tool maps to exactly one `ISandbox` operation. No business logic in the tool; no `ISandbox` leakage into the agent's instructions.

## Wiring (selection by environment)

`ISandbox` is registered once; the implementation is chosen by configuration so local F5 uses Docker and the deployed app uses dynamic sessions:

```csharp
// Sandbox:Runtime = "Local" (dev) | "Aca" (cloud)
services.AddSandbox(config);          // see references/wiring.cs
services.AddSingleton<SandboxTools>();
// SandboxTools methods are discovered into AIFunctions the same way as any
// other tool class (see maf-csharp-implementation builder-and-tools.cs).
```

The conversation id flows from the agent's session/thread into `ISandbox.GetOrCreateSessionAsync(conversationId, ct)` so it becomes the dynamic-sessions `identifier`.

## MAF API notes (1.10+)

- Tools are plain methods with `[Description]`, turned into `AIFunction`s via `AIFunctionFactory.Create(...)` — identical to `maf-csharp-implementation`.
- The sandbox tool methods take a `CancellationToken` and return strings/DTOs the model can act on; keep return payloads small (truncate large stdout, return file metadata for big files).

## Hand-off

- Session-pool resource, scaling, network, RBAC → `azure-container-apps-sessions-bicep`.
- Host identity + the Session Executor role assignment → `agent-secrets-identity`.
- Tool-call audit sink and retention → `agent-guardrails-safety`.
- General agent/tool wiring conventions → `maf-csharp-implementation`.
