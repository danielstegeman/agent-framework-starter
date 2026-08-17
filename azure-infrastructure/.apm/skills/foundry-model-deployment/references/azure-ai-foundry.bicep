// references/azure-ai-foundry.bicep
//
// Provisions an Azure AI Foundry resource for a code-first agent:
//   - AIServices-kind account (not OpenAI-kind) — supports Foundry projects.
//   - A Foundry project scoped under the account.
//   - A model deployment with GlobalStandard SKU.
//
// After deployment, set in appsettings.json / hosting env vars:
//   AZURE_AI_PROJECT_ENDPOINT       = outputs.projectEndpoint
//   AZURE_AI_MODEL_DEPLOYMENT_NAME  = outputs.deploymentName

param accountName string

param projectName string = '${accountName}-project'

param location string = resourceGroup().location

@description('Model publisher as shown in the Foundry catalog (e.g. OpenAI, Microsoft, Meta). Maps to the deployment format field.')
param modelPublisher string

@description('Model name as shown in the Foundry catalog.')
param modelName string

@description('Deployment name the application passes as AZURE_AI_MODEL_DEPLOYMENT_NAME.')
param deploymentName string = modelName

@description('Exact model version string. Check the Foundry portal for available versions.')
param modelVersion string

@description('Capacity in thousands of tokens per minute (TPU). 10 = 10k TPM. Increase if requests are throttled.')
@minValue(1)
param capacityTpu int = 10

// ─── Foundry account ─────────────────────────────────────────────────────────

resource account 'Microsoft.CognitiveServices/accounts@2026-05-01' = {
  name: accountName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: 'S0'
  }
  kind: 'AIServices'
  properties: {
    customSubDomainName: accountName
    allowProjectManagement: true
    disableLocalAuth: true              // keyless auth only — no API keys
  }
}

// ─── Foundry project ─────────────────────────────────────────────────────────

resource project 'Microsoft.CognitiveServices/accounts/projects@2026-05-01' = {
  parent: account
  name: projectName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    displayName: projectName
  }
}

// ─── Model deployment ────────────────────────────────────────────────────────
// GlobalStandard SKU routes to Microsoft-managed global capacity.
// capacityTpu is in units of 1,000 tokens per minute.

resource deployment 'Microsoft.CognitiveServices/accounts/deployments@2026-05-01' = {
  parent: account
  name: deploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: capacityTpu
  }
  properties: {
    model: {
      format: modelPublisher    // publisher name: 'OpenAI', 'Microsoft', 'Meta', etc.
      name: modelName
      version: modelVersion
    }
  }
}

// ─── Outputs ─────────────────────────────────────────────────────────────────
// projectEndpoint is the Agent Framework / AIProjectClient endpoint.

output projectEndpoint string = 'https://${accountName}.services.ai.azure.com/api/projects/${project.name}'
output deploymentName string = deployment.name
output accountName string = account.name
output accountId string = account.id
output projectName string = project.name
output projectId string = project.id
