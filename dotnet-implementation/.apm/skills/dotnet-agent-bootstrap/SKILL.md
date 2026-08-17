---
name: dotnet-agent-bootstrap
description: Bootstrap a new C# / Microsoft Agent Framework solution from scratch — runs `dotnet new` for the solution, host, agent, optional tools, Aspire AppHost, and tests projects; adds the current Foundry package set (`Microsoft.Agents.AI`, `Microsoft.Agents.AI.Foundry`, `Azure.AI.Projects`, `Azure.Identity`); writes `Directory.Build.props`, `global.json`, `.editorconfig`, `.gitignore`; and initialises git. Use this skill when the user asks "bootstrap a new MAF agent solution", "scaffold a code-first agent project in C#", "create a new agent project called X", "I want to start a new Microsoft Agent Framework solution", or "set up an empty agent solution I can build immediately". Defers package management to the `nuget-dependency-management` skill.
---

# .NET Agent Bootstrap

Scaffold a buildable Microsoft Agent Framework C# solution. Keep the scaffold minimal, then use the official get-started docs for first-agent code, tools, and instructions.

## When to use

- Greenfield. The user wants an empty solution they can `dotnet build` and `dotnet test` immediately.
- After `agent-architecture-decisions` has captured the high-level choices.

## Inputs to collect (one batch)

1. **Solution name** (e.g. `WeatherAgent`). Used for the .sln and namespaces.
2. **Target directory** (default: current cwd, should be empty).
3. **First tools integration name** (optional, e.g. `OpenMeteo`). If omitted, keep simple local tools in the agent project.
4. **Add Aspire AppHost?** Optional; useful for local multi-service development.
5. **Add evaluation tests project?** Optional but recommended for agent quality work.

If the user is unsure, suggest defaults and proceed. Don't block on questions.

## What gets created

Minimal default:

```text
<target-dir>/
├── <Solution>.sln
├── Directory.Build.props
├── global.json
├── .editorconfig
├── .gitignore
├── README.md
├── src/
│   ├── <Solution>.Host/
│   └── <Solution>.Agent/
└── tests/
    └── <Solution>.Tests/
```

Optional additions:

```text
src/<Solution>.Tools.<Integration>/   # use when a tool integration deserves its own project
src/<Solution>.AppHost/               # if Aspire chosen
tests/<Solution>.Evaluation.Tests/    # if eval chosen
```

A vertical-slice `Agents/<AgentName>/` layout and separate tools projects are scaling options, not defaults. Start with the smallest structure that is clear; split when multiple agents or integrations make the boundaries valuable.

## Commands — run in order

> When adding packages or project references, delegate to `nuget-dependency-management` when that skill is available. Use `dotnet add` CLI commands, never edit `.csproj` by hand.

### 1. Solution + projects

```powershell
Set-Location <target-dir>
dotnet new sln -n <Solution>

dotnet new console  -n <Solution>.Host  -o src/<Solution>.Host
dotnet new classlib -n <Solution>.Agent -o src/<Solution>.Agent
dotnet new xunit    -n <Solution>.Tests -o tests/<Solution>.Tests

# Optional tools project, if the integration should be isolated.
dotnet new classlib -n <Solution>.Tools.<Integration> -o src/<Solution>.Tools.<Integration>

# Optional Aspire / evaluation projects.
dotnet new aspire-apphost -n <Solution>.AppHost          -o src/<Solution>.AppHost
dotnet new xunit          -n <Solution>.Evaluation.Tests -o tests/<Solution>.Evaluation.Tests

dotnet sln add (Get-ChildItem -Recurse -Filter *.csproj)
```

### 2. Project references

```powershell
dotnet add src/<Solution>.Host\<Solution>.Host.csproj reference src/<Solution>.Agent\<Solution>.Agent.csproj
dotnet add tests/<Solution>.Tests\<Solution>.Tests.csproj reference src/<Solution>.Agent\<Solution>.Agent.csproj

# Optional tools project.
dotnet add src/<Solution>.Agent\<Solution>.Agent.csproj reference src/<Solution>.Tools.<Integration>\<Solution>.Tools.<Integration>.csproj

# Optional eval and Aspire projects.
dotnet add tests/<Solution>.Evaluation.Tests\<Solution>.Evaluation.Tests.csproj reference src/<Solution>.Agent\<Solution>.Agent.csproj
dotnet add src/<Solution>.AppHost\<Solution>.AppHost.csproj reference src/<Solution>.Host\<Solution>.Host.csproj
```

### 3. NuGet packages

Host (entrypoint + DI + config):

```powershell
dotnet add src/<Solution>.Host\<Solution>.Host.csproj package Microsoft.Extensions.Hosting
dotnet add src/<Solution>.Host\<Solution>.Host.csproj package Microsoft.Extensions.Configuration.UserSecrets
```

Agent project (MAF + Foundry + Azure auth):

```powershell
# GA
dotnet add src/<Solution>.Agent\<Solution>.Agent.csproj package Microsoft.Agents.AI
dotnet add src/<Solution>.Agent\<Solution>.Agent.csproj package Azure.Identity

# Prerelease
dotnet add src/<Solution>.Agent\<Solution>.Agent.csproj package Microsoft.Agents.AI.Foundry --prerelease
dotnet add src/<Solution>.Agent\<Solution>.Agent.csproj package Azure.AI.Projects --prerelease
```

Do **not** add `Azure.AI.Inference` for new Foundry-based MAF scaffolds.

Optional tools project:

```powershell
dotnet add src/<Solution>.Tools.<Integration>\<Solution>.Tools.<Integration>.csproj package Azure.Identity
```

Evaluation tests, if created:

```powershell
dotnet add tests/<Solution>.Evaluation.Tests\<Solution>.Evaluation.Tests.csproj package Microsoft.Extensions.AI.Evaluation
dotnet add tests/<Solution>.Evaluation.Tests\<Solution>.Evaluation.Tests.csproj package Microsoft.Extensions.AI.Evaluation.Quality
dotnet add tests/<Solution>.Evaluation.Tests\<Solution>.Evaluation.Tests.csproj package Microsoft.Extensions.AI.Evaluation.Reporting
```

### 4. Repo-level files

`global.json` — pin the SDK line used by the project:

```json
{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }
```

`Directory.Build.props` — apply to every project:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

`.editorconfig` — run `dotnet new editorconfig`, then keep the repository's normal C# formatting choices.

`.gitignore` — run `dotnet new gitignore`.

### 5. First-agent implementation

Do not paste a custom framework wrapper or large bespoke sample. Follow the official [Your First Agent](https://learn.microsoft.com/en-us/agent-framework/get-started/your-first-agent) guide for the initial `Program.cs`, instructions, and tools, then adapt the provider setup to the Foundry pattern in `maf-csharp-implementation`.

Use these configuration names for Foundry:

```text
AZURE_AI_PROJECT_ENDPOINT=https://<service>.services.ai.azure.com/api/projects/<project>
AZURE_AI_MODEL_DEPLOYMENT_NAME=<deployment-name>
```

### 6. Verify

```powershell
dotnet restore
dotnet build
dotnet test
```

All three must pass before declaring success.

### 7. Initialise git

```powershell
git init -b main
git add .
git commit -m "chore: scaffold <Solution> via dotnet-agent-bootstrap"
```

Do **not** push to a remote — that's the user's call.

## Rules

- Run `dotnet build` after each significant scaffold step so problems surface immediately.
- If `dotnet new aspire-apphost` fails, the Aspire workload is missing; tell the user to run `dotnet workload install aspire` and continue without AppHost if needed.
- Don't add packages just in case. Add more deliberately when the chosen architecture requires them.
- Keep the generated code MAF-native. Do not create a wrapper around `AIAgent` during bootstrap.
- Start with local tools in the agent project when that is simplest. Split into a tools project when the integration has enough SDKs, tests, or ownership boundaries to justify it.

## Hand-off

- Implementation deep-dive -> `maf-csharp-implementation`.
- Workflow orchestration -> `maf-workflows-orchestration`.
- Local dev orchestration -> `dotnet-aspire-apphost`.
- Infra -> `agent-infrastructure-overview` -> `azure-container-apps-bicep` + `azure-devops-pipelines-for-agents`.
- Eval scaffolding inside the new test project -> `agent-evaluation-strategy`.
- Guardrail middleware on the new agent -> `agent-guardrails-safety`.

## Official Documentation

- [Your First Agent](https://learn.microsoft.com/en-us/agent-framework/get-started/your-first-agent)
- [Microsoft Foundry model provider](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/microsoft-foundry)
- [Tools overview](https://learn.microsoft.com/en-us/agent-framework/agents/tools/)
- [Microsoft.Extensions.AI.Evaluation](https://learn.microsoft.com/en-us/dotnet/ai/evaluation/libraries)
- [.NET samples](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples)
