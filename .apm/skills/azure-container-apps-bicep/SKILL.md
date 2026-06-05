---
name: azure-container-apps-bicep
description: Author Bicep for hosting a code-first agent on Azure Container Apps — user-assigned managed identity, ACR pull, Key Vault secret references, Azure OpenAI access, OpenTelemetry env vars, ingress, scaling rules, and health probes. Use this skill when the user asks "write the Bicep for my agent on Container Apps", "deploy my agent to ACA with managed identity", "Container Apps Bicep with Key Vault references", "ACA Bicep template for an MAF agent", or any equivalent IaC request targeted at Container Apps for an agent workload.
---

# Azure Container Apps — Bicep for Agents

Bicep module that deploys a single Container App for a code-first agent. Reference template: [references/container-apps.bicep](../../references/container-apps.bicep).

## When to use

- New agent service deploying to ACA for the first time.
- Adding/changing identity, secrets, scaling, or probes on an existing ACA-hosted agent.
- Standing up a new environment (dev / test / prod) for an agent.

## What this skill produces

`infra/container-apps.bicep` — a deployable Bicep file that depends on:
- An existing **Container Apps environment** (created separately; usually shared across apps).
- An existing **Key Vault** (with the App Insights connection string already in it).
- An existing **Azure OpenAI resource** with a deployment.
- An existing **ACR** with the image already pushed (the pipeline handles this).

It creates:
- A **user-assigned managed identity** for the app.
- The **Container App** itself, with identity attached, ACR pull, KV secret refs, OTel env vars, ingress, scaling, and probes.

It does **not** create RBAC role assignments — those should live in a separate `infra/rbac.bicep` you deploy once at environment setup (so the pipeline doesn't need elevated permissions on every deploy).

## Parameters that matter

| Param | Notes |
|---|---|
| `appName` | Used as the app name AND the UAMI suffix (`${appName}-id`) AND the `OTEL_SERVICE_NAME`. Keep it short and kebab-case. |
| `envName` | Existing Container Apps environment name in the same RG. |
| `image` | Fully-qualified: `<acr>.azurecr.io/<repo>:<tag>`. Bicep derives the registry server with `split(image, '/')[0]`. |
| `keyVaultName` | Existing KV in the same RG. |
| `appInsightsConnectionStringSecretName` | Secret name in KV (default `appinsights-connection-string`). |
| `azureOpenAiEndpoint`, `azureOpenAiDeploymentName` | Passed as env vars to the agent. |

## Required RBAC (deploy separately)

The UAMI needs:
- `AcrPull` on the registry resource.
- `Key Vault Secrets User` on the Key Vault.
- `Cognitive Services OpenAI User` on the Azure OpenAI resource.

Put these in `infra/rbac.bicep` and deploy with elevated permissions (one-time). The deploy pipeline only needs `Container Apps Contributor` on the RG.

## Patterns to follow

- **`activeRevisionsMode: 'Single'`** — agent deployments are full replacements, no blue/green at the revision level. Use ACA's built-in revision rollback for emergencies.
- **`ingress.transport: 'auto'`** — supports HTTP/2 streaming for token streaming endpoints.
- **Health probes** — `/health/live` and `/health/ready` distinct. Readiness should check downstream (Azure OpenAI reachable, Key Vault reachable) so cold-start traffic doesn't hit an unprepared container.
- **Scaling on concurrent requests, not CPU.** Agents are I/O-bound (waiting on the LLM). CPU is a poor proxy.
- **`min: 1`** in production to avoid cold-start latency on the first request. `min: 0` is fine for dev.
- **OTel env vars are not optional** — every prod container exports traces.

## Patterns to avoid

- **Embedding secret values in the template.** Use `secrets[].keyVaultUrl + identity`. The pipeline never sees the secret value.
- **System-assigned identity.** Switch to UAMI from day one so the RBAC graph survives recreations.
- **`internal: true` ingress** unless you actually have VNet integration on the environment. It fails silently otherwise.
- **Hard-coded subscription IDs / RG names.** Use `resourceGroup().location`, parameters, and `existing` references.

## Composing the environment

If the user also needs the Container Apps environment, App Insights, Key Vault, and ACR, that's outside this skill's scope. Either:
- Defer to `azure-prepare` for the broader Azure scaffolding, OR
- Compose a `main.bicep` that wires:
  ```bicep
  module env       'environment.bicep' = { ... }
  module identity  'identity.bicep'    = { ... }
  module rbac      'rbac.bicep'        = { dependsOn: [identity] ... }
  module app       'container-apps.bicep' = { dependsOn: [env, rbac] ... }
  ```

## Validation

Before opening a PR:
```bash
bicep build infra/container-apps.bicep         # syntax + linting
az deployment group what-if \
  --resource-group rg-agent-dev \
  --template-file infra/container-apps.bicep \
  --parameters @infra/dev.parameters.json      # preview changes
```

The pipeline runs `az deployment group create` — if `what-if` shows surprises, fix the template before merging.

## Hand-off

- Pipeline that deploys this -> `azure-devops-pipelines-for-agents`.
- RBAC role selection -> `azure-rbac`.
- Identity / federation -> `agent-secrets-identity`.
- App Insights connection-string source -> `appinsights-instrumentation`.
- Deployment execution / azd -> `azure-deploy`.
- Pre-deploy validation -> `azure-validate`.
