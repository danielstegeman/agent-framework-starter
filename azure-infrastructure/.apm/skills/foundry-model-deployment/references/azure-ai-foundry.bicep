// references/azure-ai-foundry.bicep
//
// Provisions an Azure AI Foundry resource for a code-first agent:
//   - AIServices-kind account (not OpenAI-kind) — supports any catalog model.
//   - A Foundry project scoped under the account.
//   - A model deployment with GlobalStandard SKU.
//
// After deployment, set in appsettings.json / Container Apps env vars:
//   AzureAIFoundry__Endpoint       = outputs.modelsEndpoint
//   AzureAIFoundry__DeploymentName = outputs.deploymentName

param accountName string

param projectName string = '${accountName}-project'

param location string = resourceGroup().location

@description('Model publisher as shown in the Foundry catalog (e.g. OpenAI, Microsoft, Meta). Maps to the deployment format field.')
param modelPublisher string = 'OpenAI'

@description('Model name as shown in the Foundry catalog. Also used as the deployment name.')
param modelName string = 'gpt-4o'

@description('Exact model version string. Check the Foundry portal for available versions.')
param modelVersion string = '2025-04-14'

@description('Capacity in thousands of tokens per minute (TPU). 10 = 10k TPM. Increase if requests are throttled.')
@minValue(1)
param capacityTpu int = 10

// ─── Foundry account ─────────────────────────────────────────────────────────

resource account 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
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

resource project 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: account
  name: projectName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {}
}

// ─── Model deployment ────────────────────────────────────────────────────────
// GlobalStandard SKU routes to Microsoft-managed global capacity.
// capacityTpu is in units of 1,000 tokens per minute.

resource deployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: account
  name: modelName
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
// modelsEndpoint is the Azure AI Model Inference API endpoint.
// It works with ChatCompletionsClient (Azure.AI.Inference) for any deployed model.

output modelsEndpoint string = 'https://${accountName}.services.ai.azure.com/models'
output deploymentName string = deployment.name
output accountName string = account.name
output accountId string = account.id
