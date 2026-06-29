---
name: dotnet-aspire-apphost
description: Add a .NET Aspire AppHost to a code-first agent solution to orchestrate local F5 development — agent service plus dependencies (OTLP dashboard, optional Redis / SQL / Cosmos emulator) — and generate the container manifest used to deploy to Azure Container Apps via azd. Use this skill when the user asks "set up Aspire for my agent", "I want F5 to work for my MAF agent", "give me a local dev story", "generate container manifest from Aspire", or "use Aspire AppHost to run my agent locally".
---

# .NET Aspire AppHost for a Code-First Agent

Add an Aspire AppHost project that runs the agent + its local dependencies under one `aspire run` and provides a free local dashboard for OTel traces, logs, and metrics.

## When to use

- New solution, or an existing one with no local-orchestration story.
- The dev loop currently requires manually starting multiple processes.
- You want a free Aspire dashboard for local OTel traces without standing up Jaeger or Docker.

## Prerequisites

- **.NET 10 SDK** — required for C# AppHosts.
- **Aspire CLI** — install once: `dotnet tool install -g aspire`. Verify: `aspire --version`.
- **Docker is NOT required** for this agent use-case. Docker is only needed if you add containerized dependencies (Redis, Postgres, etc.). The Aspire dashboard itself runs as a .NET process.

## What this skill produces

1. A new project `src/<Agent>.AppHost/` created by `aspire init` (detects the `.sln`/`.slnx` and scaffolds a project-based AppHost automatically).
2. A `Program.cs` in AppHost that declares the agent project as an Aspire resource.
3. Aspire injects `OTEL_EXPORTER_OTLP_ENDPOINT` automatically — no manual env var needed.

## Setup

```bash
# From the solution root — aspire init detects .slnx/.sln and creates a project-based AppHost
aspire init

# Add project reference from AppHost to the agent host
dotnet add src/<Agent>.AppHost reference src/<Agent>.Host/<Agent>.Host.csproj
```

For containerized dependencies the agent uses (only if Docker is available):

```bash
aspire add redis        # optional
aspire add postgres     # optional
```

## AppHost Program.cs shape

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// The agent host — Aspire uses the strongly-typed Projects.<Name> generated from the ProjectReference
var agent = builder.AddProject<Projects._Agent_Host>("agent");

builder.Build().Run();
```

Aspire automatically injects `OTEL_EXPORTER_OTLP_ENDPOINT` and starts the dashboard. The agent's `AddAgentTelemetry(...)` (see [otel-azuremonitor.cs](../maf-csharp-implementation/references/otel-azuremonitor.cs)) already picks up OTLP when no App Insights connection string is set.

## Running

```bash
# From the solution root (or the AppHost directory)
aspire run
```

The CLI prints the dashboard URL, e.g. `Dashboard: https://localhost:17068/login?t=...`.

## Rules

- **AppHost is dev-only.** It is never deployed. The csproj should have `<IsAspireHost>true</IsAspireHost>`.
- **No production endpoints in AppHost.** Use `.WithEnvironment(...)` for local overrides only, or rely on `appsettings.Development.json` in the agent host. Never put production secrets in AppHost.
- **Containerized emulators require Docker.** If the machine cannot run Docker, do not add `AddRedis()`, `AddPostgres()`, or any `RunAsContainer()` / `RunAsEmulator()` calls. Model those as external resources or skip them for local dev.
- **The OTel dashboard is the killer feature.** Make sure the agent uses OTel (it should already, per `maf-csharp-implementation`). Tool spans show up automatically.

## Manifest generation for azd

When the project also targets `azd`:

```bash
dotnet run --project src/<Agent>.AppHost -- --publisher manifest --output-path ./aspire-manifest.json
```

`azd init` consumes this and emits Bicep + GitHub Actions / ADO pipelines automatically. If the user wants ADO YAML produced specifically, prefer `azure-devops-pipelines-for-agents` over `azd`'s generated GHA workflow.

## When NOT to add Aspire

- The agent has zero dependencies beyond Azure OpenAI **and** the team can set `OTEL_EXPORTER_OTLP_ENDPOINT` manually → just run `dotnet run` and point at a standalone dashboard or skip telemetry UI entirely.
- The team uses Docker Compose religiously for local dev → don't fight that; map the agent into the existing compose file.

## Hand-off

- Production infra -> `azure-container-apps-bicep`.
- CI/CD that *doesn't* use azd -> `azure-devops-pipelines-for-agents`.
- CI/CD that *does* use azd -> `azure-prepare` then `azure-deploy`.
- Implementation patterns -> `maf-csharp-implementation`.

## Official Documentation

- [.NET Aspire overview](https://learn.microsoft.com/en-us/dotnet/aspire/get-started/aspire-overview)
- [Aspire AppHost](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/app-host-overview)
- [Aspire dashboard](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/dashboard/overview)
