---
name: maf-hosted-tools
description: >
  Decision guidance and implementation pointers for Microsoft Agent Framework Foundry hosted tools in C#/.NET:
  provider-executed tools, hosted tools, web search tool, code interpreter, file search, Bing grounding,
  Bing Custom Search, Azure AI Search, SharePoint, Microsoft Fabric, Memory Search, Computer Use,
  Browser Automation, Agent-to-Agent, image generation, hosted MCP tools, and Microsoft Foundry Toolbox.
  Use this skill when the user asks "how do I add web search to my agent", "run tools on Foundry",
  "Responses runtime tools", "provider-executed tools", "service-side tools", "hosted tool approval",
  "Foundry toolbox", or any equivalent question about tools that execute on Azure AI Foundry instead
  of inside the local agent process.
---

# Microsoft Agent Framework — Foundry Hosted Tools

Use Foundry hosted tools when the agent runs locally in your process, but the tool executes on the Azure AI Foundry / Responses runtime. Keep simple app-owned operations as in-process C# function tools; use MCP when the boundary should be protocol-based.

## Core concept

A hosted tool is **provider-executed**: your MAF host creates the agent, sends the tool definition through `AIProjectClient.AsAIAgent(...)`, and the Foundry / Responses runtime invokes the tool. Your process does not call a local C# method for the tool body.

This is distinct from in-process function tools, which execute in your .NET process over local services, and local MCP tools, which execute behind an MCP server boundary. Do not mix the security assumptions: network access, approval, identity, auditing, and sandboxing depend on where the tool actually runs.

## Decision guidance

| Need | Prefer | Tradeoffs |
|---|---|---|
| App-owned C# operation, same process, low latency | **In-process function tools** | Easiest to test and authorize; your service owns execution and sandboxing. |
| Existing MCP server or reusable cross-client tool surface | **Local MCP tools** | Clear protocol boundary; extra server lifecycle and contract management. |
| Provider-managed web, file, code, browser, memory, or search capability | **Foundry hosted tools** | Less infrastructure in your service; availability, auth, approval, and billing follow provider behavior. |
| Curated hosted tool configuration in a Foundry project | **Microsoft Foundry Toolbox** | Named/versioned bundles; requires project governance and catalog checks. |

## When to use

- Web search, file search, code execution, browser automation, image generation, or memory should run on the provider runtime.
- Foundry already exposes the capability for the target provider, region, and model deployment.
- You need provider-managed connectors such as Bing grounding, Azure AI Search, SharePoint, Fabric, or Foundry-managed memory.
- The local agent host should stay thin and should not own the tool sandbox.
- Avoid hosted tools when the tool needs local DI/private network access, deterministic local transaction control, or non-preview lifecycle guarantees.

## C# wiring

Requires the Foundry provider package:

```powershell
dotnet add package Microsoft.Agents.AI.Foundry --prerelease
```

Pass hosted tools in the `tools` list on `AIProjectClient.AsAIAgent(...)`:

```csharp
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;

AIAgent agent = new AIProjectClient(new Uri(projectEndpoint), new DefaultAzureCredential())
    .AsAIAgent(
        model: deploymentName,
        instructions: "You are a helpful assistant that can search the web.",
        tools: [new HostedWebSearchTool()]);
```

Repository convention:

```text
AZURE_AI_PROJECT_ENDPOINT=https://<service>.services.ai.azure.com/api/projects/<project>
AZURE_AI_MODEL_DEPLOYMENT_NAME=<deployment-name>
```

Some MAF docs use `FOUNDRY_PROJECT_ENDPOINT` and `FOUNDRY_MODEL`. In this repo, prefer the `AZURE_AI_*` names unless following a doc sample verbatim.

## Capability catalogue

Always check the live [tools overview](https://learn.microsoft.com/en-us/agent-framework/agents/tools/) and [Foundry provider matrix](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/microsoft-foundry) before designing against a capability. Provider, region, model, and catalog availability can differ.

| Capability | Maturity / support note | Docs |
|---|---|---|
| Function Tools, Tool Approval, Code Interpreter, File Search, Hosted MCP Tools, Local MCP Tools, Microsoft Foundry Toolbox | Listed as Supported in the Foundry C# provider matrix; Function Tools and Local MCP run locally, not as provider-executed hosted tools. | [Foundry provider](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/microsoft-foundry), [Tools overview](https://learn.microsoft.com/en-us/agent-framework/agents/tools/) |
| Web Search | Hosted/provider web search capability. | [Web Search](https://learn.microsoft.com/en-us/agent-framework/agents/tools/web-search) |
| Bing Grounding; Bing Custom Search | Grounding is experimental and needs your own Grounding with Bing Search resource; Custom Search is preview with a curated domain list. | [Tools overview](https://learn.microsoft.com/en-us/agent-framework/agents/tools/) |
| Azure AI Search | Experimental; uses a Foundry connection. | [Tools overview](https://learn.microsoft.com/en-us/agent-framework/agents/tools/) |
| SharePoint; Microsoft Fabric | Preview connector capabilities; Fabric uses a Fabric data agent. | [Tools overview](https://learn.microsoft.com/en-us/agent-framework/agents/tools/) |
| Memory Search | Preview; Foundry-managed memory store. | [Tools overview](https://learn.microsoft.com/en-us/agent-framework/agents/tools/) |
| Computer Use; Browser Automation | Preview capabilities; Browser Automation uses Azure Playwright. | [Tools overview](https://learn.microsoft.com/en-us/agent-framework/agents/tools/) |
| Agent-to-Agent (A2A) tool; Image Generation | A2A is preview; Image Generation is hosted on the Foundry / OpenAI Responses runtime. | [Tools overview](https://learn.microsoft.com/en-us/agent-framework/agents/tools/) |

Experimental and preview tools may emit an `ExperimentalWarning` the first time they are used. Treat that as a design signal: verify support, lifecycle status, region, RBAC, and fallback behavior before committing to the capability.

## Microsoft Foundry Toolbox

Microsoft Foundry Toolbox provides named, versioned bundles of hosted tool configurations managed in a Foundry project. Use it when teams need a governed catalog of provider-side tool configurations rather than repeated tool setup in each agent host. Treat toolbox names and versions as deployment inputs, and review toolbox changes like application configuration.

## Approval and identity

Framework **Tool Approval** is for locally-invoked function tools. Service-side hosted tools follow the provider's own approval behavior, not the framework human-in-the-loop path.

Security rules:

- Do not assume a hosted tool inherits the same approval, audit, or network controls as a local function tool.
- Bing grounding, Azure AI Search, SharePoint, and Fabric commonly require a Foundry connection plus appropriate RBAC.
- Use `DefaultAzureCredential` locally and managed identity in Azure where possible.
- Hand off identity, secrets, and least-privilege role selection to `agent-secrets-identity` and `azure-rbac`.

## Hand-off

- MCP tool boundary, hosted MCP vs local MCP -> `maf-mcp-tools`.
- Code-execution sandbox tradeoffs -> `agent-sandboxing` and `agent-sandbox-csharp`.
- Model deployment and project endpoint setup -> `foundry-model-deployment`.
- C# implementation shape -> `maf-csharp-implementation`.
- Identity, secrets, and RBAC -> `agent-secrets-identity` and `azure-rbac`.

## What this skill does NOT cover

- Local C# function tool implementation or general C# project shape -> `maf-csharp-implementation`.
- MCP server/client design beyond hosted-tool boundaries -> `maf-mcp-tools`.
- Sandbox architecture, model deployment, or RBAC details -> `agent-sandboxing`, `agent-sandbox-csharp`, `foundry-model-deployment`, and `azure-rbac`.

## Official Documentation

- https://learn.microsoft.com/en-us/agent-framework/agents/tools/
- https://learn.microsoft.com/en-us/agent-framework/agents/tools/web-search
- https://learn.microsoft.com/en-us/agent-framework/agents/tools/code-interpreter
- https://learn.microsoft.com/en-us/agent-framework/agents/tools/file-search
- https://learn.microsoft.com/en-us/agent-framework/agents/tools/hosted-mcp-tools
- https://learn.microsoft.com/en-us/agent-framework/agents/tools/tool-approval
- https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/microsoft-foundry
- https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/tools/foundry-toolbox
