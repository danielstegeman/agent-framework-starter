---
name: foundry-model-deployment
description: Provision an Azure AI Foundry resource (AIServices-kind account + project + model deployment) for use with a code-first agent. Produces infra/azure-ai-foundry.bicep and outputs the Foundry project endpoint plus model deployment name for `AZURE_AI_PROJECT_ENDPOINT` / `AZURE_AI_MODEL_DEPLOYMENT_NAME`. Run this when no Foundry project/model deployment exists yet, or when adding a new model deployment for a new agent. The Bicep can be deployed as a one-time manual step or promoted into the CI/CD pipeline. Use when the user asks "provision a Foundry model", "set up Azure AI Foundry", "deploy a model", "I need a model endpoint for my agent", "create a model deployment", "add a model", or any equivalent. Part of the agent-framework-starter infrastructure phase.
---

# Foundry Model Deployment

Provisions an **Azure AI Foundry** resource — an `AIServices`-kind Cognitive Services account, a Foundry project, and a model deployment — and outputs the **project endpoint** and deployment name that the agent reads from config.

Reference template: [references/azure-ai-foundry.bicep](references/azure-ai-foundry.bicep).

## When to use

- **New agent project** — no Azure AI Foundry resource/project exists yet.
- **Adding a new agent** to an existing solution that needs its own model or a different model deployment.
- **Changing the model** on an existing deployment (update `modelPublisher`, `modelName`, `modelVersion`, `deploymentName`, or `capacityTpu` and re-deploy — the deployment re-deploy is idempotent; it updates capacity and model version in place).

**Skip this skill entirely** if you already have a Foundry project endpoint and a model deployment name. Record those values in the decisions document as `AZURE_AI_PROJECT_ENDPOINT` and `AZURE_AI_MODEL_DEPLOYMENT_NAME` and proceed.

## Why `AIServices`, not `OpenAI`

An `OpenAI`-kind resource only exposes OpenAI models via the Azure OpenAI endpoint. An `AIServices`-kind resource:

- Supports Foundry projects and current Agent Framework project-endpoint wiring.
- Can host model deployments from the Foundry catalog, subject to regional/catalog availability and quota.
- Keeps the app configuration model-agnostic: the agent receives a project endpoint and deployment name, not provider-specific client keys.

Use `OpenAI` kind only if you have an existing AOAI resource you cannot migrate. Record that as an alternative in the decisions document.

## Inputs to collect

| Input | Default | Notes |
|---|---|---|
| `accountName` | — | Cognitive Services account name + DNS subdomain. Kebab-case, globally unique. |
| `projectName` | `${accountName}-project` | Foundry project scoped under the account. |
| `resourceGroup` | — | Should match the hosting environment's resource group unless Foundry is centrally managed. |
| `location` | RG location | Prefer the same region as the host to minimise latency and cross-region egress cost. |
| `modelPublisher` | — | Publisher name as shown in the Foundry catalog (for example `OpenAI`, `Microsoft`, or `Meta`). This is the `format` field in the deployment Bicep. |
| `modelName` | — | Model name as shown in the Foundry catalog. Keep this model-agnostic in docs; choose an approved deployment at implementation time. |
| `deploymentName` | `modelName` | Deployment name the app uses as `AZURE_AI_MODEL_DEPLOYMENT_NAME`. Override if your deployment naming standard differs from the catalog model name. |
| `modelVersion` | — | Exact version string from the Foundry portal/catalog for the selected model. |
| `capacityTpu` | `10` | Thousands of tokens per minute. Start low; increase if throttled. |

## What gets created

`infra/azure-ai-foundry.bicep` deploying:

- **Foundry account** (`Microsoft.CognitiveServices/accounts`, kind `AIServices`) — with `disableLocalAuth: true` (keyless only) and `allowProjectManagement: true`.
- **Foundry project** (`Microsoft.CognitiveServices/accounts/projects`) — scoped under the account.
- **Model deployment** (`Microsoft.CognitiveServices/accounts/deployments`) — `GlobalStandard` SKU, capacity in TPU, model identified by publisher (`format`) + name + version.

The reference Bicep uses current stable Cognitive Services API versions verified against the Microsoft.CognitiveServices/accounts template documentation.

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
      modelPublisher=<publisher> \
      modelName=<model-name> \
      deploymentName=<deployment-name> \
      modelVersion=<model-version> \
      capacityTpu=10
```

Requires `Cognitive Services Contributor` (or a custom least-privilege equivalent) on the resource group. The **app deploy pipeline does not need this permission** unless you intentionally choose Option B.

### Option B — CI/CD pipeline stage (managed path)

Add an infra stage to the selected CI/CD system that runs `az deployment group create` for `azure-ai-foundry.bicep` before the app deploy stage. Use incremental deployment — the resource re-deploy and deployment re-deploy are idempotent and safe to run on every merge.

Grant the deployer `Cognitive Services Contributor` on the resource group. See `azure-devops-pipelines-for-agents` for the ADO pipeline scaffolding; GitHub Actions can run the same Azure CLI command with federated login.

Prefer Option B when:
- Model versions or capacity need to be updated as part of a code merge.
- You want model deployment changes tied to commits and reviewed as code.
- Multiple environments (dev / test / prod) must stay in sync on model selection.

## Outputs

Record these values after deployment:

| Output | Config key / env var | Example value |
|---|---|---|
| `projectEndpoint` | `AZURE_AI_PROJECT_ENDPOINT` | `https://<account>.services.ai.azure.com/api/projects/<project>` |
| `deploymentName` | `AZURE_AI_MODEL_DEPLOYMENT_NAME` | `<deployment-name>` |

These become environment variables in Container Apps (see `azure-container-apps-bicep`) and local user secrets or appsettings for local development. Do not use legacy inference endpoints or legacy Foundry config keys for new Agent Framework wiring.

## Required RBAC

The workload identity used by the host needs a least-privilege data-plane role on the Foundry account/project, commonly **`Cognitive Services User`** on the Foundry account for project/model access. Use `azure-rbac` to confirm the narrowest role for the exact provider features and tools.

Put this in `infra/rbac.bicep` alongside the existing `AcrPull` and `Key Vault Secrets User` assignments. Deploy `rbac.bicep` once with elevated permissions; it does not need to re-run on every app deploy.

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

For local dev, `DefaultAzureCredential` uses the signed-in `az login` identity. Grant your developer identity the same data-plane role on the Foundry account/project.

Add the project endpoint and deployment name to user secrets:

```bash
dotnet user-secrets set "AZURE_AI_PROJECT_ENDPOINT" "https://<account>.services.ai.azure.com/api/projects/<project>"
dotnet user-secrets set "AZURE_AI_MODEL_DEPLOYMENT_NAME" "<deployment-name>"
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

## Official Documentation

- [Azure AI Foundry overview](https://learn.microsoft.com/en-us/azure/ai-foundry/what-is-azure-ai-foundry)
- [Foundry Agent Service overview](https://learn.microsoft.com/en-us/azure/foundry/agents/overview)
- [Foundry Models sold by Azure](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure)
- [Microsoft.CognitiveServices/accounts Bicep reference](https://learn.microsoft.com/en-us/azure/templates/microsoft.cognitiveservices/accounts)
- [Microsoft.CognitiveServices/accounts/projects Bicep reference](https://learn.microsoft.com/en-us/azure/templates/microsoft.cognitiveservices/accounts/projects)
- [Microsoft.CognitiveServices/accounts/deployments Bicep reference](https://learn.microsoft.com/en-us/azure/templates/microsoft.cognitiveservices/accounts/deployments)


