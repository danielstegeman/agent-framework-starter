---
name: agent-secrets-identity
description: Configure identity, authentication, and secret management for a code-first agent — `DefaultAzureCredential` for local dev + managed identity in Azure, user-assigned managed identity for the workload, Key Vault references vs runtime SDK lookups, On-Behalf-Of (OBO) flow when the agent acts as the user, and workload identity federation for the ADO/GitHub deployer. Use this skill when the user asks "how do I authenticate my agent", "set up managed identity for my MAF agent", "Key Vault for my agent", "agent should act on behalf of the user", "OBO flow", "workload identity federation for my pipeline", or anything about agent auth / secrets architecture.
---

# Agent Secrets & Identity

Three identities are at play in a code-first agent system. Get them right once and you never deal with secret rotation again.

## The three identities

| | Who | What it can do | Credential |
|---|---|---|---|
| **Developer** | A human running the agent locally | Read dev resources; impersonate dev MI optionally | `az login` -> `DefaultAzureCredential` |
| **Workload** | The deployed agent process | Talk to Azure OpenAI, Key Vault, downstream APIs | **User-assigned managed identity** attached to the host |
| **Deployer** | The ADO/GitHub pipeline | Push images, deploy Bicep, set up RBAC | **Workload identity federation** (no secrets) |

There are **no service principal client secrets** in this architecture. If one appears, it's a regression.

## Workload identity (the agent at runtime)

Always **user-assigned managed identity**, not system-assigned. Reasons:
- Survives app recreation (e.g. swapping Container Apps revisions cleanly across blue/green).
- Can be granted RBAC before the app exists.
- Can be shared across multiple replicas / regions of the same logical agent.

In C#, every Azure SDK client takes a `TokenCredential`. Always pass `new DefaultAzureCredential()` — it transparently uses:
- The UAMI in Azure (via `AZURE_CLIENT_ID` env var).
- The developer's `az login` locally.
- Visual Studio / VS Code creds in IDE scenarios.

```csharp
var openAi = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential());
var kv     = new SecretClient(new Uri(kvUri),         new DefaultAzureCredential());
```

Set `AZURE_CLIENT_ID` on the container so `DefaultAzureCredential` picks the *user-assigned* identity (without this, on a container with multiple identities, the SDK is ambiguous).

## Key Vault references vs runtime SDK lookups

Two ways to give the container a secret:

| | When |
|---|---|
| **Container Apps secret with `keyVaultUrl`** (Bicep `secrets[].keyVaultUrl + identity`) | Default. The platform resolves the secret at app start and exposes it as `secretRef` env var. No SDK code. |
| **Runtime `SecretClient.GetSecretAsync(...)`** | The secret can rotate without restart, or you need many secrets keyed dynamically. |

Use Container Apps secret refs for the App Insights connection string and any "set once at deploy" secret. Use the SDK only when rotation-without-restart matters.

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
- GitHub Actions: configure `azure/login@v2` with federated credentials, no client secret.

The federated service principal needs scope-appropriate roles:
- `Contributor` on the RG (for Bicep deploys).
- `AcrPush` on the registry.
- **Not** `Owner` — `Owner` lets the pipeline change RBAC, which is a privilege escalation path. RBAC should be assigned by a separate one-time process (see below).

## RBAC bootstrapping

Separate `rbac.bicep` deployed once per environment by a privileged identity (a human admin, or a one-shot pipeline with elevated permission). It assigns:

| Identity | Scope | Role |
|---|---|---|
| Workload UAMI | Azure OpenAI | `Cognitive Services OpenAI User` |
| Workload UAMI | Key Vault | `Key Vault Secrets User` |
| Workload UAMI | ACR | `AcrPull` |
| Workload UAMI | Dynamic-sessions pool (if the agent runs model-generated code) | `Azure ContainerApps Session Executor` |
| Deployer SP | RG | `Contributor` |
| Deployer SP | ACR | `AcrPush` |

After bootstrap, the deploy pipeline never touches RBAC. Hand off to `azure-rbac` to pick the right role per tool the agent uses.

## Code-execution sandbox identity (if the agent runs model-generated code)

When the agent executes model-generated code in an Azure Container Apps dynamic-sessions pool (see `agent-sandboxing`, `azure-container-apps-sessions-bicep`), the identity split is:

- **The host's workload UAMI** holds `Azure ContainerApps Session Executor` on the pool and calls the management API with a `DefaultAzureCredential` token whose audience is `https://dynamicsessions.io`. Only the host talks to the pool.
- **The session is credential-less.** Do **not** enable a managed identity *inside* the session — that would hand cloud credentials to attacker-controllable, model-generated code. The pool's own identity is used solely to pull the session image from ACR (`AcrPull`), never injected into the running session.

This keeps the blast radius of a prompt-injection-driven code path to the sandbox itself.


## Local dev

- Developer runs `az login` once.
- `DefaultAzureCredential` resolves to the developer's identity.
- The developer needs the same data-plane roles the workload UAMI has — usually grant them via an Entra group ("agent-dev").
- For secrets, the developer can either:
  - Read from the dev Key Vault using their own KV-Secrets-User role (no local secrets file), OR
  - Use `dotnet user-secrets` for things that shouldn't touch Azure (rare).

`appsettings.Development.json` holds non-secret per-dev overrides (e.g. a personal AOAI endpoint).

## What goes where — quick reference

| Thing | Lives in |
|---|---|
| Azure OpenAI endpoint URL | App setting (env var). |
| Azure OpenAI deployment name | App setting. |
| App Insights connection string | Key Vault, surfaced as Container Apps secret ref. |
| Downstream API key (legacy auth) | Key Vault, surfaced as Container Apps secret ref. |
| OAuth client secret for downstream OBO | **Doesn't exist.** Use federated credential to workload MI. |
| Storage account connection string | **Doesn't exist.** Use the storage SDK with `DefaultAzureCredential`. |

## Hand-off

- Bicep that creates UAMI + KV refs -> `azure-container-apps-bicep`.
- Pipeline service connection with WIF -> `azure-devops-pipelines-for-agents`.
- App registration for OBO -> `entra-app-registration`.
- Picking specific RBAC roles -> `azure-rbac`.
- Dynamic-sessions pool + the Session Executor role assignment -> `azure-container-apps-sessions-bicep`.
- Auditing what the identity actually does -> `agent-guardrails-safety`.
