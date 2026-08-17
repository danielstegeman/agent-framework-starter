---
name: agent-secrets-identity
description: Configure identity, authentication, and secret management for a code-first agent across hosting options — `DefaultAzureCredential` for local dev + managed identity in Azure, user-assigned managed identity for self-hosted workloads, Key Vault references vs runtime SDK lookups, Azure AI Foundry project endpoint configuration, On-Behalf-Of (OBO) flow when the agent acts as the user, and workload identity federation for ADO or GitHub deployers. Use this skill when the user asks "how do I authenticate my agent", "set up managed identity for my MAF agent", "Key Vault for my agent", "agent should act on behalf of the user", "OBO flow", "workload identity federation for my pipeline", or anything about agent auth / secrets architecture.
---

# Agent Secrets & Identity

Three identities are common in a code-first agent system. Get them right once and you avoid secret rotation for the agent itself.

## Hosting and pipeline options

Identity guidance applies across hosting choices, but the amount of infrastructure differs:

- **Foundry Hosted Agents**: managed runtime; use Foundry/Entra identity controls and skip most Container Apps/ACR wiring.
- **Azure Container Apps**: recommended self-hosting option in this repo; use UAMI, Key Vault references, and ACR pull identity.
- **App Service / Azure Functions**: valid self-hosting alternatives; use the same managed identity and Key Vault principles.
- **Azure DevOps or GitHub Actions**: both should authenticate to Azure with workload identity federation, never stored client secrets.

## The three identities

| | Who | What it can do | Credential |
|---|---|---|---|
| **Developer** | A human running the agent locally | Read dev resources; impersonate dev MI optionally | `az login` -> `DefaultAzureCredential` |
| **Workload** | The deployed agent process | Talk to Azure AI Foundry, Key Vault, downstream APIs | **User-assigned managed identity** attached to the host, unless using a managed hosted-agent runtime |
| **Deployer** | The ADO/GitHub pipeline | Push images, deploy Bicep, set up environment resources | **Workload identity federation** (no secrets) |

There are **no service principal client secrets** in this architecture. If one appears, it's a regression.

## Workload identity (the agent at runtime)

For self-hosted Azure workloads, prefer **user-assigned managed identity**, not system-assigned. Reasons:
- Survives app recreation.
- Can be granted RBAC before the app exists.
- Can be shared across multiple replicas / regions of the same logical agent.

In C#, every Azure SDK client takes a `TokenCredential`. Pass `new DefaultAzureCredential()` — it transparently uses:
- The UAMI in Azure (via `AZURE_CLIENT_ID` env var).
- The developer's `az login` locally.
- Visual Studio / VS Code creds in IDE scenarios.

```csharp
var project = new AIProjectClient(new Uri(projectEndpoint), new DefaultAzureCredential());
var kv      = new SecretClient(new Uri(kvUri),           new DefaultAzureCredential());
```

Set `AZURE_CLIENT_ID` on self-hosted containers so `DefaultAzureCredential` picks the *user-assigned* identity. Without this, a host with multiple identities is ambiguous.

## Azure AI Foundry configuration

New MAF wiring uses the **Foundry project endpoint**, not the older model-inference endpoint convention:

| Setting | Example |
|---|---|
| `AZURE_AI_PROJECT_ENDPOINT` | `https://<svc>.services.ai.azure.com/api/projects/<project>` |
| `AZURE_AI_MODEL_DEPLOYMENT_NAME` | `<deployment-name>` |

These values are configuration, not secrets. Put them in Container Apps env vars, App Service app settings, Functions app settings, ADO variable groups, GitHub environment variables, or local user secrets as appropriate.

## Key Vault references vs runtime SDK lookups

Two ways to give the runtime a secret:

| | When |
|---|---|
| **Platform secret reference** (for example Container Apps `secrets[].keyVaultUrl + identity`) | Default for deployment-time values. The platform resolves the secret at app start and exposes it as an env var. No SDK code. |
| **Runtime `SecretClient.GetSecretAsync(...)`** | The secret can rotate without restart, or you need many secrets keyed dynamically. |

Use platform secret refs for the App Insights connection string and any "set once at deploy" secret. Use the SDK only when rotation-without-restart matters.

Never put secret values in `appsettings.json`, even with `#{}` tokens replaced in the pipeline.

## On-Behalf-Of (OBO) — agent acting as a user

When the agent needs to call an API **as the calling user** (so authorisation, audit trails, and data access reflect the user, not the agent):

1. The caller authenticates to the agent endpoint with their token (Bearer).
2. The agent validates the token (audience = the agent's app registration).
3. The agent exchanges the token for a downstream token using OBO (`OnBehalfOfCredential`).
4. The downstream API sees the original user.

```csharp
var obo = new OnBehalfOfCredential(
    tenantId: tenantId,
    clientId: agentAppRegClientId,
    clientCertificate: certFromKv,   // or federated
    userAssertion: incomingBearerToken);

var downstream = new HttpClient { /* attach obo bearer */ };
```

OBO requires:
- An **app registration** for the agent (use `entra-app-registration`).
- API permissions on the downstream API granted to the agent app reg.
- Admin consent if scopes are admin-only.
- A client credential — **always federated, never a secret**. Federate to the workload MI so the agent uses its MI to mint the OBO token.

Use OBO sparingly — it shifts the trust model from "agent is trusted" to "agent enforces user permissions per call". That's right for write-heavy operations, overkill for read-only summarisation.

## Deployer identity (CI/CD)

The ADO/GitHub pipeline needs Azure access to deploy. **Workload identity federation**:

- ADO: create the service connection with "Workload Identity federation (automatic)" — ADO mints a federated credential against an app registration tied to that connection.
- GitHub Actions: configure `azure/login` with federated credentials, no client secret.

The federated service principal needs scope-appropriate roles:
- `Contributor` on the RG (for Bicep deploys).
- `AcrPush` on the registry when pushing images.
- **Not** `Owner` — `Owner` lets the pipeline change RBAC, which is a privilege escalation path. RBAC should be assigned by a separate one-time process.

## RBAC bootstrapping

Separate `rbac.bicep` deployed once per environment by a privileged identity (a human admin, or a one-shot pipeline with elevated permission). It assigns:

| Identity | Scope | Role |
|---|---|---|
| Workload UAMI | Azure AI Foundry / model provider | Least-privilege data-plane role, commonly `Cognitive Services User` for Foundry account access or provider-specific equivalent |
| Workload UAMI | Key Vault | `Key Vault Secrets User` |
| Workload UAMI | ACR | `AcrPull` |
| Workload UAMI | Dynamic-sessions pool (if the agent runs model-generated code) | `Azure ContainerApps Session Executor` |
| Deployer SP | RG | `Contributor` |
| Deployer SP | ACR | `AcrPush` |

After bootstrap, the deploy pipeline never touches RBAC. Hand off to `azure-rbac` to pick the right role per tool the agent uses.

## Code-execution sandbox identity (if the agent runs model-generated code)

When the agent executes model-generated code in an Azure Container Apps dynamic-sessions pool (see `agent-sandboxing`, `azure-container-apps-sessions-bicep`), the identity split is:

- **The host's workload UAMI** holds `Azure ContainerApps Session Executor` on the pool and calls the management API with a `DefaultAzureCredential` token for the dynamic sessions audience. Only the host talks to the pool.
- **The session is credential-less.** Do **not** enable a managed identity *inside* the session — that would hand cloud credentials to attacker-controllable, model-generated code. The pool's own identity is used solely to pull the session image from ACR (`AcrPull`), never injected into the running session.

This keeps the blast radius of a prompt-injection-driven code path to the sandbox itself.

## Local dev

- Developer runs `az login` once.
- `DefaultAzureCredential` resolves to the developer's identity.
- The developer needs the same data-plane roles the workload UAMI has — usually grant them via an Entra group ("agent-dev").
- For secrets, the developer can either:
  - Read from the dev Key Vault using their own KV-Secrets-User role (no local secrets file), OR
  - Use `dotnet user-secrets` for non-production local overrides.

`appsettings.Development.json` holds non-secret per-dev overrides such as a personal Foundry project endpoint.

## What goes where — quick reference

| Thing | Lives in |
|---|---|
| Azure AI project endpoint URL | App setting / env var `AZURE_AI_PROJECT_ENDPOINT`. |
| Model deployment name | App setting / env var `AZURE_AI_MODEL_DEPLOYMENT_NAME`. |
| App Insights connection string | Key Vault, surfaced as platform secret ref. |
| Downstream API key (legacy auth) | Key Vault, surfaced as platform secret ref. |
| OAuth client secret for downstream OBO | **Doesn't exist.** Use federated credential to workload MI. |
| Storage account connection string | **Doesn't exist.** Use the storage SDK with `DefaultAzureCredential`. |

## Hand-off

- Bicep that creates UAMI + KV refs -> `azure-container-apps-bicep` when ACA is the selected host.
- Pipeline service connection with WIF -> `azure-devops-pipelines-for-agents` for ADO, or GitHub Actions equivalent.
- App registration for OBO -> `entra-app-registration`.
- Picking specific RBAC roles -> `azure-rbac`.
- Dynamic-sessions pool + the Session Executor role assignment -> `azure-container-apps-sessions-bicep`.
- Auditing what the identity actually does -> `agent-guardrails-safety`.

## Official Documentation

**Azure Identity & Managed Identity**
- [DefaultAzureCredential / credential chains](https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/credential-chains?tabs=dac)
- [User-assigned managed identities](https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/how-manage-user-assigned-managed-identities)
- [Azure Identity client library for .NET](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/identity-readme)

**Authentication flows**
- [On-Behalf-Of flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow)
- [Workload identity federation](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation)

**Secrets management**
- [Azure Container Apps secret management (Key Vault references)](https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets)

**Hosting**
- [Agent Framework hosting overview](https://learn.microsoft.com/en-us/agent-framework/hosting/)
- [Foundry Agent Service overview](https://learn.microsoft.com/en-us/azure/foundry/agents/overview)


