---
name: foundry-hosted-agents
description: "Choose and implement Foundry Hosted Agents for a .NET Microsoft Agent Framework agent: host my agent on Foundry, Foundry hosted agent, deploy agent to Foundry Agent Service, managed agent hosting, azd ai agent init/run/invoke/deploy, Responses protocol, Invocations protocol, OpenAI-compatible /responses endpoint, built-in session management, dedicated Entra identity, containerized managed agent runtime, and alternatives to Aspire or self-hosted ASP.NET Core."
---

# Foundry Hosted Agents

Use Foundry Hosted Agents when the whole agent should run as a managed, containerized application in Microsoft Foundry Agent Service instead of in an app-owned web host.
Foundry operates scaling, session-state persistence, security integration, and lifecycle; the .NET hosting integration package is prerelease even though the managed service is generally available.

## Decision guide

| Need | Use | Tradeoff |
| --- | --- | --- |
| Microsoft operates runtime, scaling, lifecycle, security integration, and session persistence | **Foundry Hosted Agents** | Less control over the host process, sidecars, and local parity. |
| OpenAI-compatible client access with platform-managed history and streaming | **Responses protocol** | Recommended path, but payload shape follows `/responses`. |
| Custom payloads or non-conversational/non-OpenAI-compatible streaming | **Invocations protocol** | More control, but the app owns request/response semantics and durable state. |
| Local F5 orchestration, DevUI, or self-hosted ASP.NET Core | **`dotnet-aspire-apphost`** | More application-level control, but you own more infrastructure. |

## When to use

Choose Foundry Hosted Agents when the user wants:
- managed infrastructure with no containers, web servers, scaling rules, or lifecycle loops to configure by hand;
- built-in session management where the platform persists `$HOME` and uploaded files across turns and idle periods;
- a dedicated Entra identity per deployed agent;
- OpenAI-compatible endpoints so clients can use any OpenAI-compatible SDK through the Responses protocol;
- a managed service for either an `Agent` or a workflow exposed as an agent with `Workflow.as_agent()`.

Requires the .NET 10 SDK. Prefer official docs for current API details over embedding large samples.

## Packages

```bash
dotnet add package Microsoft.Agents.AI.Foundry.Hosting --prerelease
dotnet add package Azure.AI.Projects --prerelease
```

## Responses protocol

Use the Responses protocol by default. It exposes an OpenAI-compatible `/responses` endpoint while Foundry manages history, streaming, and the session lifecycle.

```csharp
using Azure.AI.AgentServer.Core;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;

AIAgent agent = new AIProjectClient(projectEndpoint, new DefaultAzureCredential())
    .AsAIAgent(model: deployment, instructions: "You are a helpful AI assistant.", name: "my-agent");

var builder = AgentHost.CreateBuilder(args);
builder.Services.AddFoundryResponses(agent);
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());
var app = builder.Build();
app.Run();
```

`AgentHost.CreateBuilder` creates a host preconfigured for the Foundry hosting environment; `AddFoundryResponses` registers the agent with the Responses protocol handler; `MapFoundryResponses` maps the `/responses` endpoint.

Because Foundry manages conversation history, set `store: false` on client calls when applicable to avoid duplicating history outside the platform-managed session.

## Invocations protocol

Use the Invocations protocol when the endpoint needs full control over HTTP request and response handling, such as custom payloads, non-conversational workloads, or streaming that is not OpenAI-compatible.
In C#, implement a custom `InvocationHandler`, register the server with `AddInvocationsServer()`, and map it with `MapInvocationsServer()`.

Rules: keep protocol choice explicit; do not use in-memory session stores for production state because they are lost on restart; use durable storage such as Cosmos DB when the handler needs state.

## Local development and deploy with azd

Install the azd AI agent extension and scaffold from a manifest or from scratch:
```bash
azd ext install azure.ai.agents
azd ai agent init -m <path-or-url-to-agent.manifest.yaml>
azd ai agent init
```

Set local environment variables:
```bash
FOUNDRY_PROJECT_ENDPOINT="https://<account>.services.ai.azure.com/api/projects/<project>"
AZURE_AI_MODEL_DEPLOYMENT_NAME="<deployment>"
```

Run and test locally:
```bash
azd ai agent run
azd ai agent invoke --local "Hello!"
```
The host starts on `http://localhost:8088`; clients can also `POST` to `/responses`.

Deploy:
```bash
azd provision
azd deploy
```
`azd provision` creates the resource group with the Foundry instance, project, model deployment, Application Insights, and container registry. `azd deploy` packages the agent as a container image, pushes it to ACR, and deploys it to Foundry Agent Service.

## Runtime environment

Foundry auto-injects `FOUNDRY_PROJECT_ENDPOINT`, `AZURE_AI_MODEL_DEPLOYMENT_NAME`, and `APPLICATIONINSIGHTS_CONNECTION_STRING`.
In Foundry, the platform supplies caller user/call context so state can be isolated per user. Local runs do not get that platform context, so supply your own identity and state controls when testing locally.

## Hand-off

- Connecting a client to an already-deployed hosted agent or Prompt Agent -> `maf-remote-agents` (in the **maf-core** package).
- Self-hosting, Aspire AppHost, or DevUI alternatives -> `dotnet-aspire-apphost`; overall infrastructure decision -> `agent-infrastructure-overview`.
- Model provisioning -> `foundry-model-deployment`; agent implementation shape -> `maf-csharp-implementation`.

## Official Documentation

- https://learn.microsoft.com/en-us/agent-framework/hosting/foundry-hosted-agent
- https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/hosted-agents
- https://learn.microsoft.com/en-us/azure/foundry/agents/quickstarts/quickstart-hosted-agent?pivots=azd
- https://learn.microsoft.com/en-us/agent-framework/hosting/
- https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/agent-services/foundry
