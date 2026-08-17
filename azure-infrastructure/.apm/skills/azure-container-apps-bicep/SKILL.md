---
name: azure-container-apps-bicep
description: Author Bicep for hosting a code-first agent on Azure Container Apps as one recommended self-hosting option — user-assigned managed identity, ACR pull, Key Vault secret references, Azure AI Foundry project endpoint configuration, OpenTelemetry env vars, ingress, scaling rules, and health probes. Use this skill when the user asks "write the Bicep for my agent on Container Apps", "deploy my agent to ACA with managed identity", "Container Apps Bicep with Key Vault references", "ACA Bicep template for an MAF agent", or any equivalent IaC request targeted at Container Apps for an agent workload. If the user wants managed Foundry Hosted Agents, App Service, Azure Functions, AKS, or GitHub Actions-only guidance, route accordingly instead of forcing ACA.
---

# Azure Container Apps — Bicep for Agents

Bicep module that deploys a single Container App for a code-first agent. Reference template: [references/container-apps.bicep](references/container-apps.bicep).

## When to use

- The user has chosen **Azure Container Apps** as the self-hosting platform.
- New agent service deploying to ACA for the first time.
- Adding/changing identity, secrets, scaling, or probes on an existing ACA-hosted agent.
- Standing up a new environment (dev / test / prod) for an agent.

Do **not** imply ACA is mandatory. If the user can use managed **Foundry Hosted Agents**, most of this infrastructure is unnecessary. App Service and Azure Functions are also valid self-hosting alternatives when their hosting model fits better.

## What this skill produces

`infra/container-apps.bicep` — a deployable Bicep file that depends on:
- An existing **Container Apps environment** (created separately; usually shared across apps).
- An existing **Key Vault** (with the App Insights connection string already in it).
- An existing **Azure AI Foundry account/project** with a model deployment — run `foundry-model-deployment` first if not yet provisioned.
- An existing **ACR** with the image already pushed (the pipeline handles this).

It creates:
- A **user-assigned managed identity** for the app.
- The **Container App** itself, with identity attached, ACR pull, Key Vault secret refs, Foundry project endpoint env vars, OTel env vars, ingress, scaling, and probes.

It does **not** create RBAC role assignments — those should live in a separate `infra/rbac.bicep` you deploy once at environment setup so the app deploy pipeline doesn't need elevated permissions on every run.

## Parameters that matter

| Param | Notes |
|---|---|
| `appName` | Used as the app name AND the UAMI suffix (`${appName}-id`) AND the `OTEL_SERVICE_NAME`. Keep it short and kebab-case. |
| `envName` | Existing Container Apps environment name in the same RG. |
| `image` | Fully-qualified: `<acr>.azurecr.io/<repo>:<tag>`. Bicep derives the registry server with `split(image, '/')[0]`. |
| `environmentName` | Non-secret environment label used in `OTEL_RESOURCE_ATTRIBUTES`. |
| `keyVaultName` | Existing KV in the same RG. |
| `appInsightsConnectionStringSecretName` | Secret name in KV (default `appinsights-connection-string`). |
| `azureAiProjectEndpoint`, `azureAiModelDeploymentName` | Passed as `AZURE_AI_PROJECT_ENDPOINT` and `AZURE_AI_MODEL_DEPLOYMENT_NAME`. Values come from `foundry-model-deployment` outputs or an existing Foundry project. |

## Required RBAC (deploy separately)

The UAMI needs:
- `AcrPull` on the registry resource.
- `Key Vault Secrets User` on the Key Vault.
- The least-privilege role needed to call the Foundry project/model deployment, typically `Cognitive Services User` on the Foundry account or a narrower provider-specific role when applicable.

Put these in `infra/rbac.bicep` and deploy with elevated permissions (one-time). The deploy pipeline only needs permissions to update the Container App and deploy the resource group template.

## Patterns to follow

- **`activeRevisionsMode: 'Single'`** — agent deployments are full replacements, no blue/green at the revision level. Use ACA's built-in revision rollback for emergencies.
- **`ingress.transport: 'auto'`** — supports HTTP/2 streaming for token streaming endpoints.
- **Health probes** — `/health/live` and `/health/ready` distinct. Readiness should check downstream dependencies such as Foundry and Key Vault so cold-start traffic doesn't hit an unprepared container.
- **Scaling on concurrent requests, not CPU.** Agents are I/O-bound while waiting on model responses. CPU is a poor proxy.
- **Keep at least one replica in production** if first-request latency matters. Scale-to-zero is fine for dev or low-priority environments.
- **OTel env vars are not optional** — every prod container exports traces.

## Patterns to avoid

- **Embedding secret values in the template.** Use `secrets[].keyVaultUrl + identity`. The pipeline never sees the secret value.
- **System-assigned identity.** Switch to UAMI from day one so the RBAC graph survives recreations.
- **`internal: true` ingress** unless you actually have VNet integration on the environment. It fails silently otherwise.
- **Hard-coded subscription IDs / RG names.** Use `resourceGroup().location`, parameters, and `existing` references.
- **Old Foundry config conventions.** Do not emit legacy Foundry config keys or model-inference endpoints for new MAF wiring; use the project endpoint convention.

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
bicep build infra/container-apps.bicep
az deployment group what-if \
  --resource-group rg-agent-dev \
  --template-file infra/container-apps.bicep \
  --parameters @infra/dev.parameters.json
```

The pipeline runs `az deployment group create` — if `what-if` shows surprises, fix the template before merging.

## Hand-off

- Pipeline that deploys this -> `azure-devops-pipelines-for-agents` if Azure DevOps is the chosen CI/CD system; GitHub Actions is a valid alternative.
- RBAC role selection -> `azure-rbac`.
- Identity / federation -> `agent-secrets-identity`.
- App Insights connection-string source -> `appinsights-instrumentation`.
- Deployment execution / azd -> `azure-deploy`.
- Pre-deploy validation -> `azure-validate`.
- Managed Foundry hosting decision -> `agent-infrastructure-overview`.

## Official Documentation

- [Agent Framework hosting overview](https://learn.microsoft.com/en-us/agent-framework/hosting/)
- [Foundry Agent Service overview](https://learn.microsoft.com/en-us/azure/foundry/agents/overview)
- [Azure Container Apps managed identity](https://learn.microsoft.com/en-us/azure/container-apps/managed-identity)
- [Azure Container Apps secret management (Key Vault references)](https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets)
- [Azure Container Apps ingress](https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview)
- [Microsoft.App/containerApps Bicep reference](https://learn.microsoft.com/en-us/azure/templates/microsoft.app/containerapps)

