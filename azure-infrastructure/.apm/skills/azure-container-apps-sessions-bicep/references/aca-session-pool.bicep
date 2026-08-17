// references/aca-session-pool.bicep
//
// Azure Container Apps DYNAMIC SESSIONS pool — the code-execution sandbox runtime
// for a code-first agent. Each session is a Hyper-V-isolated container started
// from a CUSTOM image; the host allocates one per conversation over the
// management API (see agent-sandbox-csharp).
//
// Two-phase deploy: the session image must already be in ACR BEFORE this runs.
//
// Security posture baked in:
//   - No managed identity on the session (sessions stay credential-less).
//   - Egress disabled by default.
//   - The HOST's user-assigned identity gets "Azure ContainerApps Session Executor"
//     so it (and only it) can allocate/execute sessions.

@description('Session pool name.')
param poolName string

@description('Azure region.')
param location string = resourceGroup().location

@description('Container Apps managed environment resource id.')
param managedEnvironmentId string

@description('Full session image reference in ACR, e.g. <acr>.azurecr.io/agent-session-executor:1.0')
param sessionImage string

@description('ACR login server, e.g. <acr>.azurecr.io')
param acrLoginServer string

@description('Resource id of the user-assigned identity that PULLS the image and (separately) HOSTS the agent.')
param hostIdentityId string

@description('Principal (object) id of the HOST identity that calls the pool — receives the Session Executor role.')
param hostIdentityPrincipalId string

@description('Hard ceiling on concurrently live sessions.')
param maxConcurrentSessions int = 20

@description('Pre-warmed sessions kept ready for sub-second allocation.')
param readySessionInstances int = 5

@description('Seconds an idle session lives before it is reclaimed.')
param cooldownPeriodInSeconds int = 300

@description('Per-session CPU cores.')
param sessionCpu string = '0.5'

@description('Per-session memory.')
param sessionMemory string = '1Gi'

// "Azure ContainerApps Session Executor" built-in role.
var sessionExecutorRoleId = '0fb8eba5-a2bb-4abe-b1c1-49dfad359bb0'

resource pool 'Microsoft.App/sessionPools@2024-10-02-preview' = {
  name: poolName
  location: location
  identity: {
    // Identity used to PULL the custom image from ACR. This is the pool's own
    // identity for registry access — it is NOT injected into the session.
    type: 'UserAssigned'
    userAssignedIdentities: { '${hostIdentityId}': {} }
  }
  properties: {
    environmentId: managedEnvironmentId
    poolManagementType: 'Dynamic'
    containerType: 'CustomContainer'

    scaleConfiguration: {
      maxConcurrentSessions: maxConcurrentSessions
      readySessionInstances: readySessionInstances
    }

    dynamicPoolConfiguration: {
      executionType: 'Timed'
      cooldownPeriodInSeconds: cooldownPeriodInSeconds
    }

    // No outbound network from the session by default.
    sessionNetworkConfiguration: {
      status: 'EgressDisabled'
    }

    customContainerTemplate: {
      registryCredentials: {
        server: acrLoginServer
        identity: hostIdentityId
      }
      containers: [
        {
          name: 'session-executor'
          image: sessionImage
          resources: {
            cpu: json(sessionCpu)
            memory: sessionMemory
          }
          // Port the executor listens on inside the session container.
          // The management API forwards request paths to this port.
        }
      ]
      ingress: {
        targetPort: 8080
      }
    }
  }
}

// The HOST identity must be able to allocate/execute sessions on this pool.
// Without this, the host's Entra token (audience https://dynamicsessions.io) is
// rejected. The session itself never receives any token.
resource sessionExecutor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(pool.id, hostIdentityPrincipalId, sessionExecutorRoleId)
  scope: pool
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions', sessionExecutorRoleId)
    principalId: hostIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

@description('Feed into the C# AcaSessionsOptions.PoolManagementEndpoint.')
output poolManagementEndpoint string = pool.properties.poolManagementEndpoint

@description('For diagnostic settings / further role assignments.')
output sessionPoolId string = pool.id
