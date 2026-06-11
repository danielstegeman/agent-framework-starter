// references/container-apps.bicep
//
// Azure Container Apps deployment for a code-first agent.
// Uses user-assigned managed identity for: ACR pull, Azure OpenAI, Key Vault.
// References Application Insights connection string via Key Vault secret ref.

param appName string
param envName string
param image string
param location string = resourceGroup().location
param keyVaultName string
param appInsightsConnectionStringSecretName string = 'appinsights-connection-string'
param azureOpenAiEndpoint string
param azureOpenAiDeploymentName string

resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${appName}-id'
  location: location
}

resource env 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: envName
}

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${uami.id}': {} }
  }
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      registries: [
        {
          server: split(image, '/')[0]
          identity: uami.id
        }
      ]
      secrets: [
        {
          name: 'appinsights-connection-string'
          keyVaultUrl: '${kv.properties.vaultUri}secrets/${appInsightsConnectionStringSecretName}'
          identity: uami.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: appName
          image: image
          env: [
            { name: 'AzureOpenAI__Endpoint',       value: azureOpenAiEndpoint }
            { name: 'AzureOpenAI__DeploymentName', value: azureOpenAiDeploymentName }
            { name: 'AZURE_CLIENT_ID',             value: uami.properties.clientId }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'appinsights-connection-string' }
            { name: 'OTEL_SERVICE_NAME',           value: appName }
          ]
          resources: { cpu: json('0.5'), memory: '1Gi' }
          probes: [
            { type: 'Liveness',  httpGet: { path: '/health/live',  port: 8080 } }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 } }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
        rules: [
          {
            name: 'http'
            http: { metadata: { concurrentRequests: '50' } }
          }
        ]
      }
    }
  }
}

output appUrl string = 'https://${app.properties.configuration.ingress.fqdn}'
output identityClientId string = uami.properties.clientId
output identityPrincipalId string = uami.properties.principalId
