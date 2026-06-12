---
name: azure-container-apps-sessions-bicep
description: Author Bicep for an Azure Container Apps dynamic-sessions pool that hosts the code-execution sandbox for a code-first agent — a custom-container session pool (Microsoft.App/sessionPools, Dynamic, CustomContainer) backed by an ACR image, with maxConcurrentSessions / readySessionInstances / cooldownPeriod scaling, no session egress, session managed-identity left OFF, and the "Azure ContainerApps Session Executor" role assigned to the host's user-assigned identity. Use this skill when the user asks "write the Bicep for a dynamic sessions pool", "provision an ACA session pool for my sandbox", "Bicep for a custom-container code sandbox", "how do I deploy the sandbox image to a session pool", or any equivalent IaC request for the agent sandbox runtime. Pairs with agent-sandbox-csharp (the C# side) and agent-secrets-identity (the role assignment).
---

# Azure Container Apps Dynamic Sessions — Bicep for the Agent Sandbox

Bicep for the **session pool** that runs the agent's code-execution sandbox. Each session is a Hyper-V-isolated container started from your **custom image**; the host allocates one per conversation over the management API. Reference template: [references/aca-session-pool.bicep](references/aca-session-pool.bicep).

Read `agent-sandboxing` for the decision and `agent-sandbox-csharp` for the C# that calls this pool. This skill provisions the runtime only.

## When to use

- Standing up the sandbox runtime for an agent that executes model-generated code/commands.
- Changing scaling (concurrency / pre-warm / cooldown), the session image, or network isolation.
- Wiring the host's managed identity to the pool so it can allocate sessions.

Do **not** use this for the built-in code-interpreter pool (no image management needed) — that's a different, simpler pool type chosen in `agent-sandboxing`.

## Two-phase deploy (image must exist first)

A custom-container pool references an image by digest/tag. That image must already be in ACR:

1. **Build & push** the session image (see `agent-sandbox-csharp` → `references/session-executor/Dockerfile`) to ACR.
2. **Deploy this Bicep**, passing the image reference. Creating the pool before the image exists fails.

In a pipeline: build/push step → then `az deployment group create` for the pool.

## What the template provisions

| Resource | Purpose |
|---|---|
| `Microsoft.App/sessionPools` | The Dynamic, CustomContainer pool — the sandbox runtime. |
| `roleAssignments` | Grants the **host** UAMI the `Azure ContainerApps Session Executor` role on the pool. |

Key properties (see the reference for the full set):

- `poolManagementType: 'Dynamic'` — Azure pre-warms and recycles sessions.
- `containerType: 'CustomContainer'` — your image, your toolchain, your port.
- `customContainerTemplate.containers[].image` — the ACR image from phase 1; CPU/memory caps per session.
- `scaleConfiguration.maxConcurrentSessions` — hard ceiling on live sessions.
- `dynamicPoolConfiguration.executionType: 'Timed'` + `cooldownPeriodInSeconds` — idle sessions are reclaimed.
- `readySessionInstances` (where supported) — pre-warmed sessions for sub-second allocation.
- `sessionNetworkConfiguration.status: 'EgressDisabled'` — **no outbound network by default**.

## Rules

- **Do NOT enable a managed identity on the session.** The session stays credential-less; only the host calls the pool. Enabling session MI hands cloud credentials to model-generated code. (See `agent-secrets-identity`.)
- **Keep egress disabled** unless `agent-sandboxing` decided a coding agent needs a git remote — and then prefer host-brokered git over `EgressEnabled`.
- **Registry pull uses the host/pool UAMI**, not admin keys. Assign `AcrPull` to the identity that pulls the session image.
- **Right-size per-session caps** (`cpu` / `memory`) to the smallest that runs the workload; the pool multiplies them by `maxConcurrentSessions`.
- **The host needs the role.** Without `Azure ContainerApps Session Executor` on the pool, the host's `DefaultAzureCredential` token (audience `https://dynamicsessions.io`) is rejected.

## Outputs

- `poolManagementEndpoint` — feed into the C# `AcaSessionsOptions.PoolManagementEndpoint`.
- `sessionPoolId` — for diagnostic settings / further role assignments.

## Hand-off

- C# client over this pool → `agent-sandbox-csharp`.
- Host identity + the role assignment rationale → `agent-secrets-identity`.
- Where this fits in the overall infra → `agent-infrastructure-overview`.
- The host Container App itself → `azure-container-apps-bicep`.
