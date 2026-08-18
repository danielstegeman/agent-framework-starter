---
name: maf-mcp-tools
description: >
  Decision guidance and implementation pointers for Microsoft Agent Framework
  MCP tool surfaces in C#/.NET: local MCP tools through the MCP C# SDK,
  provider-hosted MCP tools, and exposing an agent or workflow as an MCP server.
  Use this skill when the user asks "how do I add MCP tools", "local MCP tools",
  "hosted MCP tools", "MCP client", "MCP server", "expose my agent as MCP",
  "function tools vs MCP tools", "tool surface design", or any equivalent
  question about connecting Microsoft Agent Framework agents to Model Context
  Protocol tools.
---

# Microsoft Agent Framework — MCP Tools

Use MCP when the tool boundary should be protocol-based instead of an in-process C# method call. Keep simple app-owned functions in-process; move to MCP when reuse, isolation, or interoperability justifies the boundary.

Start from the official docs:

- [Tools overview](https://learn.microsoft.com/en-us/agent-framework/agents/tools/)
- [Function tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/function-tools)
- [Using local MCP tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/local-mcp-tools)
- [Hosted MCP tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/hosted-mcp-tools)
- [Self-host agents as MCP tools](https://learn.microsoft.com/en-us/agent-framework/hosting/self-hosting/mcp)

## Decision guidance

| Need | Prefer | Tradeoffs |
|---|---|---|
| App-owned C# operations, same process, same DI container, low latency | **In-process function tools** with `[Description]` metadata | Simple to test and deploy; tightly coupled to the agent service. |
| Existing MCP server, cross-agent reuse, separate process, tool shared with other clients | **Local MCP tools** | Better reuse and isolation; extra process/protocol lifecycle to manage. |
| Provider-hosted tool capability exposed through MCP | **Hosted MCP tools** | Less code to host; availability and auth depend on provider support. |
| Other agents or clients need to call your agent/workflow as a tool | **Expose the agent as an MCP server** | Makes your agent reusable; requires server hosting, auth, versioning, and tool contract discipline. |

Do not make "in-process tools only" a default architecture rule. Choose the smallest surface that satisfies ownership, security, deployment, and reuse requirements.

## MCP as a client

### Local MCP tools

Use [local MCP tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/local-mcp-tools) when the agent should consume tools from a local MCP server through the MCP C# SDK.

Good fits:

- Tooling already exists as an MCP server.
- The same tools are used by multiple agents, IDEs, or automation hosts.
- The tool needs process isolation from the agent host.
- The tool lifecycle is owned by another team or package.

Rules:

- Treat MCP tool names, descriptions, parameters, and return shapes as public contracts.
- Keep auth and secrets on the MCP server side when possible.
- Add approval or human-in-the-loop gates for high-impact mutations.
- Version or compatibility-test MCP servers before rolling them across agents.

### Hosted MCP tools

Use [hosted MCP tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/hosted-mcp-tools) when the model/provider exposes a managed MCP-compatible tool surface.

Good fits:

- The provider already hosts the capability.
- You want minimal infrastructure in your agent service.
- Provider support and authentication fit the target environment.

Check the [tools overview](https://learn.microsoft.com/en-us/agent-framework/agents/tools/) for current provider/tool support before designing against a hosted capability.

For **non-MCP** provider-executed tools — Foundry-hosted web search, code interpreter, file search, Bing grounding, Azure AI Search, and similar — see the sibling `maf-hosted-tools` skill. This skill covers only the MCP tool surface.

## Expose your agent as an MCP server

Use [Self-host agents as MCP tools](https://learn.microsoft.com/en-us/agent-framework/hosting/self-hosting/mcp) when another system should call your agent or workflow as a native MCP tool.

Good fits:

- A specialist agent should be reused by other agents or developer tools.
- A workflow should appear as one high-level tool with a stable contract.
- You need to integrate with MCP-native clients without duplicating the agent logic.

Design rules:

- Expose a narrow tool contract; avoid leaking internal workflow steps as public tools.
- Require authentication and authorization at the server boundary.
- Return enough structured status for callers to understand success, failure, pending approval, or retry.
- Keep prompts, credentials, and internal state out of MCP responses unless explicitly intended.

## Function tools vs MCP tools

| Question | If yes | Direction |
|---|---|---|
| Is the tool just a C# method over services already in the agent process? | Yes | Use [function tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/function-tools). |
| Does the tool need to be used by non-.NET clients or multiple agent hosts? | Yes | Use MCP. |
| Does the tool need process isolation or a separately deployable lifecycle? | Yes | Use MCP. |
| Is the tool latency-sensitive and private to this one agent? | Yes | Keep it in-process. |
| Does the tool mutate important external state? | Yes | Use the same security-first posture either way: scoped auth, audit, idempotency, and approval where needed. |

## Hand-off

- C# implementation shape and DI wiring -> `maf-csharp-implementation`.
- Non-MCP Foundry-hosted tools (web search, code interpreter, file search) -> `maf-hosted-tools`.
- Multi-agent workflows and human approval -> `maf-workflows-orchestration`.
- Hosting the agent service -> hosting/Aspire/Azure skills in this package.

## Official Documentation

- [Tools overview](https://learn.microsoft.com/en-us/agent-framework/agents/tools/)
- [Function tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/function-tools)
- [Using local MCP tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/local-mcp-tools)
- [Hosted MCP tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/hosted-mcp-tools)
- [Hosting overview](https://learn.microsoft.com/en-us/agent-framework/hosting/)
- [Self-hosting](https://learn.microsoft.com/en-us/agent-framework/hosting/self-hosting/)
- [Self-host agents as MCP tools](https://learn.microsoft.com/en-us/agent-framework/hosting/self-hosting/mcp)
