---
name: foundry-model-deployment
description: Provision an Azure AI Foundry resource (AIServices-kind account + project + model deployment) for use with a code-first agent. Produces infra/azure-ai-foundry.bicep. Run this when no Foundry model deployment exists yet, or when adding a new model deployment for a new agent. The Bicep can be deployed as a one-time manual step or promoted into the CI/CD pipeline. Use when the user asks "provision a Foundry model", "set up Azure AI Foundry", "deploy a model", "I need a model endpoint for my agent", "create a model deployment", "add a model", or any equivalent. Part of the agent-framework-starter infrastructure phase.
---

# Foundry Model Deployment

Provisions an **Azure AI Foundry** resource — an `AIServices`-kind Cognitive Services account, a Foundry project, and a model deployment — and outputs the model inference endpoint and deployment name that the agent reads from config.

Reference template: [references/azure-ai-foundry.bicep](references/azure-ai-foundry.bicep).

## When to use

- **New agent project** — no Azure AI Foundry resource exists yet.
- **Adding a new agent** to an existing solution that needs its own model or a different model.
- **Changing the model** on an existing deployment (update `modelName`, `modelVersion`, or `capacityTpu` and re-deploy — the deployment re-deploy is idempotent; it updates capacity and model version in place).

**Skip this skill entirely** if you already have a Foundry project endpoint and a model deployment name. Just record those values in the decisions document as `AzureAIFoundry__Endpoint` and `AzureAIFoundry__DeploymentName` and proceed.

## Why `AIServices`, not `OpenAI`

An `OpenAI`-kind resource only exposes OpenAI models via the Azure OpenAI endpoint. An `AIServices`-kind resource:

- Exposes **all Foundry catalog models** — OpenAI GPT, Microsoft Phi, Meta Llama, Mistral, and any other publisher in the catalog.
- Provides the **Azure AI Model Inference API** (`/models` endpoint) — a single endpoint that routes to any deployed model by deployment name.
- Supports **Foundry platform features** — Foundry projects, Agents Service, AI Search integration.

Use `OpenAI` kind only if you have an existing AOAI resource you cannot migrate. Record that as an alternative in the decisions document.

## Inputs to collect

| Input | Default | Notes |
|---|---|---|
| `accountName` | — | Cognitive Services account name + DNS subdomain. Kebab-case, globally unique. |
| `projectName` | `${accountName}-project` | Foundry project scoped under the account. |
| `resourceGroup` | — | Should match the hosting environment's resource group. |
| `location` | RG location | Same region as Container Apps to minimise latency and cross-region egress cost. |
| `modelPublisher` | `OpenAI` | Publisher name as shown in the Foundry catalog (e.g. `OpenAI`, `Microsoft`, `Meta`). This is the `format` field in the deployment Bicep. |
| `modelName` | `gpt-4o` | Model name as shown in the Foundry catalog. Also used as the deployment name. |
| `modelVersion` | `2025-04-14` | Exact version string. Check the Foundry portal for available versions. |
| `capacityTpu` | `10` | Thousands of tokens per minute. 10 = 10k TPM. Start low; increase if throttled. |

## What gets created

`infra/azure-ai-foundry.bicep` deploying:

- **Foundry account** (`Microsoft.CognitiveServices/accounts@2025-06-01`, kind `AIServices`) — with `disableLocalAuth: true` (keyless only) and `allowProjectManagement: true`.
- **Foundry project** (`Microsoft.CognitiveServices/accounts/projects@2025-06-01`) — scoped under the account.
- **Model deployment** (`Microsoft.CognitiveServices/accounts/deployments@2025-06-01`) — `GlobalStandard` SKU, capacity in TPU, model identified by publisher (`format`) + name + version.

## Deployment options

### Option A — one-time manual (simple path)

Use when the Foundry resource is shared across environments or managed separately from the agent's CI/CD pipeline.

```bash
az deployment group create \
  --resource-group <rg> \
  --template-file infra/azure-ai-foundry.bicep \
  --parameters \
      accountName=<name> \
      projectName=<project> \
      modelPublisher=OpenAI \
      modelName=gpt-4o \
      modelVersion=2025-04-14 \
      capacityTpu=10
```

Requires `Cognitive Services Contributor` (or `Owner`) on the resource group. The **deploy pipeline does not need this permission**.

### Option B — CI/CD pipeline stage (managed path)

Add an `infra-deploy` stage to `azure-pipelines.yml` that runs `az deployment group create` for `azure-ai-foundry.bicep` before the app deploy stage. Use `--mode Incremental` — the resource re-deploy and deployment re-deploy are both idempotent and safe to run on every merge.

Grant the ADO service connection `Cognitive Services Contributor` on the resource group. See `azure-devops-pipelines-for-agents` for the pipeline scaffolding.

Prefer Option B when:
- Model versions or capacity need to be updated as part of a code merge.
- You want model deployment changes tied to commits and reviewed as code.
- Multiple environments (dev / test / prod) must stay in sync on model version.

## Outputs

Record these values after deployment:

| Output | Config key | Example value |
|---|---|---|
| `modelsEndpoint` | `AzureAIFoundry__Endpoint` | `https://<account>.services.ai.azure.com/models` |
| `deploymentName` | `AzureAIFoundry__DeploymentName` | `gpt-4o` |

These become environment variables in Container Apps (see `azure-container-apps-bicep`) and go in `appsettings.json` for local development.

## Required RBAC

The UAMI provisioned by `azure-container-apps-bicep` needs:
- **`Cognitive Services User`** on the Foundry account — to call the model inference API.

Put this in `infra/rbac.bicep` alongside the existing `AcrPull` and `Key Vault Secrets User` assignments. Deploy `rbac.bicep` once with elevated permissions; it does not need to re-run on every deploy.

```bicep
// In infra/rbac.bicep — add alongside existing assignments
var cognitiveServicesUserRole = resourceId(
  'Microsoft.Authorization/roleDefinitions',
  'a97b65f3-24c7-4388-baec-2e87135dc908')  // Cognitive Services User

resource foundryRbac 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, uami.id, cognitiveServicesUserRole)
  scope: foundryAccount
  properties: {
    roleDefinitionId: cognitiveServicesUserRole
    principalId: uami.properties.principalId
    principalType: 'ServicePrincipal'
  }
}
```

## Local development

For local dev, `DefaultAzureCredential` uses the signed-in `az login` identity. Grant your developer identity the **`Cognitive Services User`** role on the Foundry account.

Add the inference endpoint and deployment name to user secrets:

```bash
dotnet user-secrets set "AzureAIFoundry:Endpoint" "https://<account>.services.ai.azure.com/models"
dotnet user-secrets set "AzureAIFoundry:DeploymentName" "gpt-4o"
```

## Finding model publisher, name, and version

1. Open **Azure AI Foundry** (ai.azure.com or portal.azure.com → AI Foundry).
2. Navigate to **Model catalog**.
3. Find the model and open its deployment details. The publisher appears as the `format` field.
4. Available versions are listed on the model card.

To query existing deployments via CLI:

```bash
az cognitiveservices account deployment list \
  --name <account> \
  --resource-group <rg> \
  --query "[].{name:name, model:properties.model.name, version:properties.model.version, publisher:properties.model.format}"
```
