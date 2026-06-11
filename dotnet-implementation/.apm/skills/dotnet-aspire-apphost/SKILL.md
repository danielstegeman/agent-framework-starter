---
name: dotnet-aspire-apphost
description: Add a .NET Aspire AppHost to a code-first agent solution to orchestrate local F5 development — agent service plus dependencies (OTLP dashboard, optional Redis / SQL / Cosmos emulator) — and generate the container manifest used to deploy to Azure Container Apps via azd. Use this skill when the user asks "set up Aspire for my agent", "I want F5 to work for my MAF agent", "give me a local dev story", "generate container manifest from Aspire", or "use Aspire AppHost to run my agent locally".
---

# .NET Aspire AppHost for a Code-First Agent

Add an Aspire AppHost project that runs the agent + its local dependencies under one F5 and exports a container manifest for `azd` deployment.

## When to use

- New solution, or an existing one with no local-orchestration story.
- The dev loop currently requires manually starting multiple processes.
- You want a free Aspire dashboard for local OTel traces without standing up Jaeger.

## What this skill produces

1. A new project `src/<Agent>.AppHost/` referencing all runnable projects.
2. A `Program.cs` in AppHost that declares the agent project + dependencies as Aspire resources.
3. Optional `appsettings.json` updates in the agent host so it reads OTLP endpoint from Aspire's auto-injected env vars.

## Commands

```bash
# From the solution root
dotnet new aspire-apphost  -n <Agent>.AppHost -o src/<Agent>.AppHost
dotnet sln add src/<Agent>.AppHost/<Agent>.AppHost.csproj
dotnet add src/<Agent>.AppHost reference src/<Agent>.Host/<Agent>.Host.csproj
```

For dependencies the agent uses, add their Aspire hosting packages:

```bash
dotnet add src/<Agent>.AppHost package Aspire.Hosting.Redis        # optional
dotnet add src/<Agent>.AppHost package Aspire.Hosting.Azure.CosmosDB # optional
```

## AppHost shape

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Optional infra
var redis = builder.AddRedis("session-store");

// The agent host
var agent = builder.AddProject<Projects._Agent_Host>("agent")
                   .WithReference(redis)
                   .WithEnvironment("AzureOpenAI__Endpoint",      "https://...")
                   .WithEnvironment("AzureOpenAI__DeploymentName","gpt-4o");

builder.Build().Run();
```

Aspire automatically injects `OTEL_EXPORTER_OTLP_ENDPOINT` and starts the dashboard. The agent's `AddAgentTelemetry(...)` (see [otel-azuremonitor.cs](../maf-csharp-implementation/references/otel-azuremonitor.cs)) already picks up OTLP when no App Insights connection string is set.

## Rules

- **AppHost is dev-only.** It is never deployed. The csproj should `<IsPackable>false</IsPackable>` and `<IsAspireHost>true</IsAspireHost>`.
- **No production endpoints in AppHost.** Reference Azure OpenAI explicitly with `.WithEnvironment(...)`, or have the agent host fall back to `appsettings.Development.json`. Never put production secrets in AppHost.
- **Aspire's emulators replace local installs.** Use `AddCosmosDB().RunAsEmulator()`, `AddSqlServer().RunAsContainer()`, etc. — don't ask the user to install local DBs.
- **The OTel dashboard is the killer feature.** Make sure the agent uses OTel (it should already, per `maf-csharp-implementation`). Tool spans show up automatically.

## Manifest generation for azd

When the project also targets `azd`:

```bash
dotnet run --project src/<Agent>.AppHost -- --publisher manifest --output-path ./aspire-manifest.json
```

`azd init` consumes this and emits Bicep + GitHub Actions / ADO pipelines automatically. If the user wants ADO YAML produced specifically, prefer `azure-devops-pipelines-for-agents` over `azd`'s generated GHA workflow.

## When NOT to add Aspire

- The agent has zero dependencies beyond Azure OpenAI -> overkill, just run the host directly with `dotnet run` and add OTLP env var manually.
- The team uses Docker Compose religiously for local dev -> don't fight that; map the agent into the existing compose file.

## Hand-off

- Production infra -> `azure-container-apps-bicep`.
- CI/CD that *doesn't* use azd -> `azure-devops-pipelines-for-agents`.
- CI/CD that *does* use azd -> `azure-prepare` then `azure-deploy`.
- Implementation patterns -> `maf-csharp-implementation`.
