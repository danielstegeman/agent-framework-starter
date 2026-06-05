# Reference Snippets

These files illustrate **one pattern each**. They are not a working application — they are minimal, decontextualised code that the skills point at when explaining a concept.

Edit the snippets if a referenced pattern changes. Each skill names the reference it relies on so it's easy to update both together.

| File | Used by skill(s) |
|---|---|
| [builder-and-tools.cs](builder-and-tools.cs) | `maf-csharp-implementation`, `dotnet-agent-bootstrap` |
| [instructions-embedded.cs](instructions-embedded.cs) | `maf-csharp-implementation` |
| [otel-azuremonitor.cs](otel-azuremonitor.cs) | `maf-csharp-implementation`, `agent-infrastructure-overview` |
| [orchestrator-cqrs.cs](orchestrator-cqrs.cs) | `maf-csharp-implementation` (advanced section) |
| [eval-fixture.cs](eval-fixture.cs) | `agent-evaluation-strategy` |
| [guardrail-middleware.cs](guardrail-middleware.cs) | `agent-guardrails-safety` |
| [azure-pipelines.yml](azure-pipelines.yml) | `azure-devops-pipelines-for-agents` |
| [container-apps.bicep](container-apps.bicep) | `azure-container-apps-bicep` |

## Principles

- **One concept per file.** Don't merge concerns. If a skill needs two patterns, it cites two files.
- **Compilable in isolation is a non-goal.** These are reading material, not a buildable solution.
- **No org-specific names.** Use generic identifiers (`WeatherTools`, `weather-agent`).
- **Comments carry the intent.** A reader should understand *why* the pattern looks this way from the file alone.
