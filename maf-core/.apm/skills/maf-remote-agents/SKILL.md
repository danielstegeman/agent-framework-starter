---
name: maf-remote-agents
description: >
  Decision guidance and implementation pointers for Microsoft Agent Framework
  remote agent clients in C#/.NET: connect to a Foundry agent, FoundryAgent,
  Prompt Agent, call a hosted agent, consume an existing agent, Copilot Studio
  agent, A2A agent, connect to a remote agent, use a server-managed agent,
  or any equivalent question about using Microsoft Agent Framework to call an
  agent definition or runtime owned by someone else while keeping standard run,
  streaming, and session APIs.
---

# Microsoft Agent Framework — Remote Agents

Use this skill when your app is a **client** of an agent whose definition or runtime is owned by another service or team. The local app connects to a server-managed agent and still uses standard MAF run, streaming, and session APIs.

## Two worlds

| World | Who owns the definition? | MAF shape | Covered by |
|---|---|---|---|
| You own the agent definition | Your app supplies model, instructions, name, tools, and middleware at runtime | `AIProjectClient.AsAIAgent(model, instructions, ...)` producing a `ChatClientAgent` | `maf-csharp-implementation` |
| Someone else owns the definition/runtime | Foundry or another remote service owns model, instructions, hosted tools, deployment, and version | `FoundryAgent` or another agent-service integration | This skill |

Do not re-declare remote-agent tools or instructions locally. If Foundry owns the definition, treat the local code as a caller.

## When to use

- A registered Foundry **Prompt Agent** should be called by name and version.
- A deployed **Hosted Agent**, Copilot Studio agent, or A2A agent already owns behavior and tools.
- The caller needs MAF run, streaming, and session APIs, not local definition ownership.

Avoid this path when this app must author instructions, local tools, middleware, or model choice; when the remote agent has no stable name/version contract; or when the caller cannot get the required Foundry or service access role.

## Decision guidance

| Need | Prefer | Tradeoffs |
|---|---|---|
| Call a named, versioned Foundry server-side definition | **Prompt Agent** through `FoundryAgent` | Strong contract and versioning; changes happen in Foundry. |
| Call a deployed agent application through an agent endpoint | **Hosted Agent** | Runtime is owned by the hosted app; deployment and availability follow that service. |
| Reuse an agent built in Copilot Studio | **Copilot Studio agent service** | Capabilities and tools are configured in Copilot Studio. |
| Interoperate with an Agent-to-Agent endpoint | **A2A agent service** | Contract and auth belong to the remote A2A service. |
| Build and own model, instructions, and tools yourself | **Code-first MAF agent** | Maximum local control; your app owns deployment and versioning. |

## Packages

For Foundry remote-agent clients in C#:

```bash
dotnet add package Azure.AI.Projects --prerelease
dotnet add package Azure.Identity
dotnet add package Microsoft.Agents.AI.Foundry --prerelease
```

Use `DefaultAzureCredential` locally and managed identity in Azure unless the host requires another Azure Identity credential.

## FoundryAgent

`FoundryAgent` connects to a Microsoft Foundry Agent Service definition. Foundry owns the agent's model, instructions, hosted tools, and version.

- **Prompt Agents** are named and versioned server-side agent definitions.
- **Hosted Agents** are deployed agent applications reached through an agent-specific endpoint.

## Connect to a Prompt Agent

```csharp
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using Microsoft.Agents.AI.Foundry;

var projectClient = new AIProjectClient(new Uri(projectEndpoint), new DefaultAzureCredential());

FoundryAgent agent = projectClient.AsAIAgent(
    new AgentReference(agentName, agentVersion));

Console.WriteLine(await agent.RunAsync("What can you help me with?"));
```

## Version and identity discipline

- Pin `agentVersion` when the app must use a specific, reproducible definition.
- Treat remote agent name and version as a contract with the owning team.
- Do not assume latest is safe for production without compatibility validation.
- The local caller identity (`DefaultAzureCredential` or managed identity) needs the appropriate Foundry access role.
- Fail fast on missing endpoint, name, version, or credentials; do not silently fall back to a different agent.

To resolve the latest registered version by name, use the administration client:

```csharp
ProjectsAgentRecord agentRecord =
    await projectClient.AgentAdministrationClient.GetAgentAsync(agentName);
FoundryAgent latestAgent = projectClient.AsAIAgent(agentRecord);
```

You can also pass a `ProjectsAgentRecord` for latest or a `ProjectsAgentVersion` for explicit version to `projectClient.AsAIAgent(...)`.

## Other remote agent services

Copilot Studio agents and A2A agents run on a remote service. Their capabilities, tools, instructions, and policy are configured on the remote agent, not through the local MAF client.

## Hand-off

- Owning the definition yourself -> `maf-csharp-implementation`.
- Deploying your app as a hosted agent -> `foundry-hosted-agents` (dotnet-implementation).
- Tool surface design -> `maf-hosted-tools` and `maf-mcp-tools`.
- Identity, managed identity, secrets, and roles -> `agent-secrets-identity` / `azure-rbac`.

## What this skill does NOT cover

- Authoring code-first MAF agents where your app owns model, instructions, and tools.
- Deploying your own application as a Foundry hosted agent.
- Designing local function tools, hosted tools, MCP tool contracts, Azure roles, managed identity, or secrets.

## Official Documentation

- [Foundry agent service integration](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/agent-services/foundry)
- [Microsoft Foundry model provider](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/microsoft-foundry)
- [Copilot Studio agent service integration](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/agent-services/copilot-studio)
- [A2A agent service integration](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/agent-services/a2a)
- [Foundry hosted agent](https://learn.microsoft.com/en-us/agent-framework/hosting/foundry-hosted-agent)
