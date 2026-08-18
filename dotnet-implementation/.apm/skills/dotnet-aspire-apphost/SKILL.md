---
name: dotnet-aspire-apphost
description: "Choose and implement the local/hosting story for a .NET Microsoft Agent Framework agent: Aspire AppHost for local F5 orchestration and telemetry, DevUI for interactive testing/debugging, ASP.NET Core self-hosting with Microsoft.Agents.AI.Hosting, or alternatives such as Foundry Hosted Agents and Azure Functions/Durable hosting. Use when the user asks to host an agent, add DevUI, self-host a MAF agent, set up Aspire AppHost, or create a local dev loop for an agent."
---

# .NET Agent Hosting: Aspire AppHost, DevUI, and Self-hosting

Start from the official docs, then add only the glue the repo needs:

- [Host your agent (get-started Step 7)](https://learn.microsoft.com/en-us/agent-framework/get-started/hosting)
- [Agent Framework hosting overview](https://learn.microsoft.com/en-us/agent-framework/hosting/)
- [ASP.NET Core / generic host self-hosting](https://learn.microsoft.com/en-us/agent-framework/hosting/self-hosting)
- [DevUI](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/ui/devui/)
- [Aspire AppHost](https://aspire.dev/get-started/app-host/) and [Aspire dashboard](https://aspire.dev/dashboard/overview/)

## Decision guide

| Need | Use | Tradeoff |
| --- | --- | --- |
| One-command local F5 for agent service + dependencies + traces/logs | **Aspire AppHost** | Development orchestration only; do not treat AppHost as the production runtime. |
| Interactive local testing/debugging of agents and workflows | **DevUI** | A development sample UI, not a production UI or security boundary. |
| The app owns HTTP routes, identity, authz, storage, scaling, and deployment | **ASP.NET Core self-hosting** with [`Microsoft.Agents.AI.Hosting`](https://www.nuget.org/packages/Microsoft.Agents.AI.Hosting) (prerelease) | More control, but the app owns infrastructure and request policy. |
| Skip most host infrastructure and use managed agent hosting | **[Foundry Hosted Agents](https://learn.microsoft.com/en-us/agent-framework/hosting/foundry-hosted-agent)** (see the `foundry-hosted-agents` skill) | Managed service path; less application-level control than self-hosting. |
| Durable, event-driven, long-running, or serverless workloads | **[Azure Functions / Durable Extension](https://learn.microsoft.com/en-us/agent-framework/hosting/azure-functions)** | Durable Task model and Functions hosting conventions become part of the design. |

## Aspire AppHost for local F5

Use Aspire when the local dev loop currently requires multiple terminals, local dependencies, or a separate telemetry viewer. Aspire gives the agent solution one `aspire run`, resource wiring, service discovery, and the local dashboard.

Typical setup:

```bash
aspire init
dotnet add src/<Agent>.AppHost reference src/<Agent>.Host/<Agent>.Host.csproj
```

Keep the AppHost small: declare the agent host project, optional local dependencies, and development-only environment overrides. Aspire injects `OTEL_EXPORTER_OTLP_ENDPOINT` for resources it runs, so the agent's OpenTelemetry setup can export to the dashboard without hardcoded endpoints.

Rules:

- AppHost is local/deployment-model glue, not the production agent host.
- Do not put production endpoints or secrets in AppHost.
- Add containerized dependencies only when Docker is available; otherwise model them as external resources or skip them for local dev.
- Keep telemetry decisions in the agent host implementation; AppHost should only wire local observability.

If the project uses `azd`, the AppHost can still publish an Aspire manifest:

```bash
dotnet run --project src/<Agent>.AppHost -- --publisher manifest --output-path ./aspire-manifest.json
```

Treat generated infra as a starting point, not a mandate.

## DevUI for local interactive testing/debugging

Use [DevUI](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/ui/devui/) when developers need to chat with, inspect, and debug one or more local agents before wiring a real client UI.

Packages are prerelease:

- Agent service: [`Microsoft.Agents.AI.DevUI`](https://www.nuget.org/packages/Microsoft.Agents.AI.DevUI)
- Aspire AppHost integration: [`Aspire.Hosting.AgentFramework.DevUI`](https://www.nuget.org/packages/Aspire.Hosting.AgentFramework.DevUI)

Follow the DevUI docs for the exact endpoint mapping. In short:

- The agent service registers named agents and exposes OpenAI Responses/Conversations endpoints.
- The AppHost adds a DevUI resource and connects it to each agent service.
- Agent names declared in the AppHost must match the names registered by the agent service.

Do not ship DevUI as a production UX.

## ASP.NET Core self-hosting

Use [self-hosting](https://learn.microsoft.com/en-us/agent-framework/hosting/self-hosting) when the agent must live inside an application-owned ASP.NET Core or generic-host process.

`Microsoft.Agents.AI.Hosting` (prerelease) registers `AIAgent`/workflow instances with DI and lets protocol packages resolve named agents. It does **not** remove the need to design:

- authentication and authorization;
- request validation, rate limits, and allowed model/tool policy;
- session persistence and conversation storage;
- deployment, scaling, health, and observability.

Use the official self-hosting page for the current package/API shape instead of copying large samples into this skill.

## Alternatives are options, not mandates

- [Foundry Hosted Agents](https://learn.microsoft.com/en-us/agent-framework/hosting/foundry-hosted-agent): choose when managed hosting, built-in session lifecycle, and Foundry integration matter more than owning the host infrastructure. Implementation details live in the `foundry-hosted-agents` skill.
- [Azure Functions / Durable Extension](https://learn.microsoft.com/en-us/agent-framework/hosting/azure-functions): choose for durable orchestration, long-running sessions, triggers, scale-to-zero, or event-driven workloads.
- Plain `dotnet run`: acceptable for a simple agent with no local dependencies and no need for the Aspire dashboard or DevUI.
- Docker Compose: acceptable when the team already standardizes on Compose; integrate the agent there rather than forcing Aspire.

## Hand-off

- Agent implementation patterns -> the `maf-csharp-implementation` skill (in the **maf-core** package).
- If the user asks for production infrastructure, first use the hosting overview to choose managed hosting vs self-hosting vs Functions/Durable; do not default to Azure Container Apps unless the user or architecture needs it.
