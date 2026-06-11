---
name: dotnet-agent-bootstrap
description: Bootstrap a new C# / Microsoft Agent Framework solution from scratch — runs `dotnet new` for the solution + host + agent + tools + tests projects, adds the right NuGet packages (Microsoft.Agents.AI, Azure.Identity, OpenTelemetry, Azure Monitor exporter, Microsoft.Extensions.AI.Evaluation), writes `Directory.Build.props`, `global.json`, `.editorconfig`, `.gitignore`, sample `Program.cs` and embedded Instructions markdown, and initialises git. Use this skill when the user asks "bootstrap a new MAF agent solution", "scaffold a code-first agent project in C#", "create a new agent project called X", "I want to start a new Microsoft Agent Framework solution", or "set up an empty agent solution I can build immediately". Defers package management to the `nuget-dependency-management` skill.
---

# .NET Agent Bootstrap

Scaffold a complete, buildable Microsoft Agent Framework C# solution that follows the layout documented in `maf-csharp-implementation`.

## When to use

- Greenfield. The user wants an empty solution they can `dotnet build` and `dotnet test` on immediately.
- After `agent-architecture-decisions` has captured the high-level choices.

## Inputs to collect (one batch)

1. **Solution name** (e.g. `WeatherAgent`). Used for the .sln and all project namespaces.
2. **First tools integration name** (e.g. `OpenMeteo`). Becomes `<Solution>.Tools.<Integration>`.
3. **Target directory** (default: current cwd, must be empty).
4. **Add Aspire AppHost?** (recommended yes if dev experience matters).
5. **Add evaluation tests project?** (recommended yes).

If the user is unsure, suggest defaults and proceed. Don't block on questions.

## What gets created

```
<target-dir>/
├── <Solution>.sln
├── Directory.Build.props
├── global.json
├── .editorconfig
├── .gitignore
├── README.md
├── src/
│   ├── <Solution>.Host/
│   ├── <Solution>/
│   │   ├── Instructions/
│   │   │   └── MainAgent.md
│   │   └── AssemblyMarker.cs
│   ├── <Solution>.Tools.<Integration>/
│   └── <Solution>.AppHost/                 # if Aspire chosen
└── tests/
    ├── <Solution>.Tests/
    └── <Solution>.Evaluation.Tests/        # if eval chosen
```

## Commands — run in order

> When adding packages or project references, **always delegate to `nuget-dependency-management`**. Use `dotnet add` CLI commands, never edit `.csproj` by hand.

### 1. Solution + projects

```bash
cd <target-dir>
dotnet new sln -n <Solution>

dotnet new console  -n <Solution>.Host                -o src/<Solution>.Host
dotnet new classlib -n <Solution>                     -o src/<Solution>
dotnet new classlib -n <Solution>.Tools.<Integration> -o src/<Solution>.Tools.<Integration>
dotnet new xunit    -n <Solution>.Tests               -o tests/<Solution>.Tests

# Optional
dotnet new aspire-apphost -n <Solution>.AppHost            -o src/<Solution>.AppHost
dotnet new xunit          -n <Solution>.Evaluation.Tests   -o tests/<Solution>.Evaluation.Tests

dotnet sln add (Get-ChildItem -Recurse -Filter *.csproj)   # PowerShell
# or, on bash:
# find . -name '*.csproj' -exec dotnet sln add {} +
```

### 2. Project references

```bash
dotnet add src/<Solution>.Host                reference src/<Solution>/<Solution>.csproj
dotnet add src/<Solution>                     reference src/<Solution>.Tools.<Integration>/<Solution>.Tools.<Integration>.csproj
dotnet add tests/<Solution>.Tests             reference src/<Solution>/<Solution>.csproj
dotnet add tests/<Solution>.Evaluation.Tests  reference src/<Solution>/<Solution>.csproj
dotnet add src/<Solution>.AppHost             reference src/<Solution>.Host/<Solution>.Host.csproj
```

### 3. NuGet packages

Host (entrypoint + DI + config + telemetry):
```bash
cd src/<Solution>.Host
dotnet add package Microsoft.Extensions.Hosting
dotnet add package Microsoft.Extensions.Configuration.UserSecrets
```

Agent project (MAF + telemetry + Azure auth):
```bash
cd ../<Solution>
dotnet add package Microsoft.Agents.AI
dotnet add package Azure.AI.Inference
dotnet add package Microsoft.Extensions.AI.AzureAIInference
dotnet add package Azure.Identity
dotnet add package Microsoft.Extensions.AI
dotnet add package Microsoft.Extensions.Options.DataAnnotations
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Instrumentation.Http
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package Azure.Monitor.OpenTelemetry.Exporter
```

Tools project (depends on what it talks to — minimum):
```bash
cd ../<Solution>.Tools.<Integration>
dotnet add package Microsoft.Extensions.Options
dotnet add package Azure.Identity
```

Evaluation tests:
```bash
cd ../../tests/<Solution>.Evaluation.Tests
dotnet add package Microsoft.Extensions.AI.Evaluation
dotnet add package Microsoft.Extensions.AI.Evaluation.Quality
dotnet add package Microsoft.Extensions.AI.Evaluation.Reporting
```

### 4. Repo-level files

**`global.json`** — pin the SDK:
```json
{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }
```

**`Directory.Build.props`** — apply to every project:
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

**`.editorconfig`** — minimum: 4-space C#, file-scoped namespaces, var preferences. Run `dotnet new editorconfig` then trim.

**`.gitignore`** — `dotnet new gitignore`.

### 5. Embedded instructions

In `src/<Solution>/<Solution>.csproj` add:
```xml
<ItemGroup>
  <EmbeddedResource Include="Instructions\**\*.md" />
</ItemGroup>
```

Create `src/<Solution>/Instructions/MainAgent.md` with a starter persona prompt:
```markdown
You are a helpful assistant. Be concise. Cite the tool you used when you used one.
```

Create `src/<Solution>/AssemblyMarker.cs`:
```csharp
namespace <Solution>;
public sealed class AssemblyMarker { }
```

### 6. Sample wiring

Copy the patterns from these reference files into the new projects, renaming types:
- [builder-and-tools.cs](../maf-csharp-implementation/references/builder-and-tools.cs) -> `src/<Solution>/ServiceCollectionExtensions.cs` + a starter tool class in `src/<Solution>.Tools.<Integration>/`
- [instructions-embedded.cs](../maf-csharp-implementation/references/instructions-embedded.cs) -> `src/<Solution>/InstructionsLoader.cs`
- [otel-azuremonitor.cs](../maf-csharp-implementation/references/otel-azuremonitor.cs) -> `src/<Solution>/TelemetryRegistration.cs`

In `src/<Solution>.Host/Program.cs`:
```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Services.AddAgentTelemetry(builder.Configuration);
builder.Services.Add<Solution>Agent(builder.Configuration);

using var host = builder.Build();
var agent = host.Services.GetRequiredService<AIAgent>();

var prompt = args.Length > 0 ? string.Join(' ', args) : "Hello!";
var thread = await agent.GetNewThreadAsync();
await foreach (var chunk in agent.RunStreamingAsync(prompt, thread))
    Console.Write(chunk.Text);
Console.WriteLine();
```

In `src/<Solution>.Host/appsettings.json`:
```json
{
  "AzureAIFoundry": {
    "Endpoint": "https://<account>.services.ai.azure.com/models",
    "DeploymentName": "gpt-4o"
  },
  "OpenTelemetry": { "ServiceName": "<solution>-agent" }
}
```

### 7. Verify

```bash
dotnet restore
dotnet build
dotnet test
```

All three must pass before declaring success.

### 8. Initialise git

```bash
git init -b main
git add .
git commit -m "chore: scaffold <Solution> via dotnet-agent-bootstrap"
```

Do **not** push to a remote — that's the user's call.

## Rules

- Run `dotnet build` after each significant scaffold step so problems surface immediately.
- If `dotnet new aspire-apphost` fails, the user is missing the workload: prompt them to run `dotnet workload install aspire` and continue.
- Don't add packages "just in case." Each one above is justified. If the user wants more (Brighter for orchestration, etc.), invoke `nuget-dependency-management` and add it deliberately.
- Don't author code beyond the references — that's the implementation skill's job. This skill leaves a working "Hello!" agent.

## Hand-off

- Implementation deep-dive -> `maf-csharp-implementation`.
- Local dev orchestration -> `dotnet-aspire-apphost`.
- Infra -> `agent-infrastructure-overview` -> `azure-container-apps-bicep` + `azure-devops-pipelines-for-agents`.
- Eval scaffolding inside the new test project -> `agent-evaluation-strategy`.
- Guardrail middleware on the new agent -> `agent-guardrails-safety`.
