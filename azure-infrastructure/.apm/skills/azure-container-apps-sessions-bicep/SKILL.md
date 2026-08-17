---
name: azure-container-apps-sessions-bicep
description: Author Bicep for an Azure Container Apps dynamic-sessions pool as one sandbox runtime option for a code-first agent — a custom-container session pool (Microsoft.App/sessionPools, Dynamic, CustomContainer) backed by an ACR image, with maxConcurrentSessions / readySessionInstances / timed lifecycle scaling, no session egress, session managed-identity left OFF, and the "Azure ContainerApps Session Executor" role assigned to the host's user-assigned identity. Use this skill when the user asks "write the Bicep for a dynamic sessions pool", "provision an ACA session pool for my sandbox", "Bicep for a custom-container code sandbox", "how do I deploy the sandbox image to a session pool", or any equivalent IaC request for the agent sandbox runtime. Pairs with agent-sandbox-csharp (the C# side) and agent-secrets-identity (the role assignment). If Foundry Hosted Agents or provider-hosted code interpreter removes the need for a custom sandbox, do not force this path.
---

# Azure Container Apps Dynamic Sessions — Bicep for the Agent Sandbox

Bicep for the **session pool** that runs the agent's code-execution sandbox when the design chooses Azure Container Apps dynamic sessions. Each session is a Hyper-V-isolated container started from your **custom image**; the host allocates one per conversation over the management API. Reference template: [references/aca-session-pool.bicep](references/aca-session-pool.bicep).

Read `agent-sandboxing` for the decision and `agent-sandbox-csharp` for the C# that calls this pool. This skill provisions the runtime only.

## When to use

- The agent executes model-generated code/commands and the architecture calls for a custom container sandbox.
- You are already on the self-hosted ACA path, or another host needs to call an ACA dynamic-sessions pool.
- Changing scaling (concurrency / pre-warm / cooldown), the session image, or network isolation.
- Wiring the host's managed identity to the pool so it can allocate sessions.

Do **not** use this by default. If the user selected Foundry Hosted Agents, provider-hosted code interpreter, Azure Functions without custom code execution, or typed tools only, confirm whether a custom sandbox pool is still needed.

## Two-phase deploy (image must exist first)

A custom-container pool references an image by digest/tag. That image must already be in ACR:

1. **Build & push** the session image (see `agent-sandbox-csharp` → `references/session-executor/Dockerfile`) to ACR.
2. **Deploy this Bicep**, passing the image reference. Creating the pool before the image exists fails.

In a pipeline: build/push step → then `az deployment group create` for the pool. Azure DevOps and GitHub Actions are both valid as long as they use workload identity federation and do not carry registry or Azure client secrets.

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
- `dynamicPoolConfiguration.lifecycleConfiguration.lifecycleType: 'Timed'` + cooldown — idle sessions are reclaimed.
- `readySessionInstances` (where supported) — pre-warmed sessions for sub-second allocation.
- `sessionNetworkConfiguration.status: 'EgressDisabled'` — **no outbound network by default**.
- `managedIdentitySettings.lifecycle: 'None'` — the pool identity can pull the image, but is not made available inside sessions.

## Rules

- **Do NOT enable a managed identity on the session.** The session stays credential-less; only the host calls the pool. Enabling session MI hands cloud credentials to model-generated code. (See `agent-secrets-identity`.)
- **Keep egress disabled** unless `agent-sandboxing` decided a coding agent needs a git remote — and then prefer host-brokered git over `EgressEnabled`.
- **Registry pull uses the host/pool UAMI**, not admin keys. Assign `AcrPull` to the identity that pulls the session image.
- **Right-size per-session caps** (`cpu` / `memory`) to the smallest that runs the workload; the pool multiplies them by `maxConcurrentSessions`.
- **The host needs the role.** Without `Azure ContainerApps Session Executor` on the pool, the host's `DefaultAzureCredential` token (for the dynamic sessions audience) is rejected.

## Outputs

- `poolManagementEndpoint` — feed into the C# `AcaSessionsOptions.PoolManagementEndpoint`.
- `sessionPoolId` — for diagnostic settings / further role assignments.

## Hand-off

- C# client over this pool → `agent-sandbox-csharp`.
- Host identity + the role assignment rationale → `agent-secrets-identity`.
- Where this fits in the overall infra → `agent-infrastructure-overview`.
- The host Container App itself, when ACA is selected → `azure-container-apps-bicep`.

## Official Documentation

- [Dynamic sessions in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/sessions)
- [Serverless code interpreter sessions in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/sessions-code-interpreter)
- [Microsoft.App/sessionPools Bicep reference](https://learn.microsoft.com/en-us/azure/templates/microsoft.app/sessionpools)

