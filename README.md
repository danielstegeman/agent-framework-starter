# Code-First Agent Starter

A portable set of **skills** and an **orchestrator agent** that guide a developer end-to-end through building a code-first AI agent: from architectural decisions through C# / Microsoft Agent Framework scaffolding, Azure infrastructure, evaluation, guardrails, and secrets management.

Designed to work with **both GitHub Copilot and Claude Code**. Each skill is a `SKILL.md` file with YAML frontmatter — the same format both tools consume.

## What you get

| | What it does |
|---|---|
| `agents/code-first-agent.md` | Top-level agent that sequences the journey: discover → decide → scaffold → implement → infra → evaluate → harden. |
| `skills/agent-architecture-decisions/` | **Language-neutral.** Interviews you on the key architectural choices (trigger model, observability, hosting, tools, context sources, flexibility vs determinism) and produces an ADR-style artifact. |
| `skills/maf-csharp-implementation/` | C# / Microsoft Agent Framework patterns: vertical-slice projects, tools-as-separate-projects, orchestration, instructions loading, multi-turn sessions. |
| `skills/agent-infrastructure-overview/` | Describes the *what* of agent infrastructure (containers, registry, identity, secrets, observability, pipeline, environment promotion) and routes to leaf skills. |
| `skills/azure-devops-pipelines-for-agents/` | ADO YAML for build + deploy of an agent service to Azure Container Apps. |
| `skills/azure-container-apps-bicep/` | Bicep for Azure Container Apps with managed identity, Key Vault refs, OTel wiring. |
| `skills/dotnet-aspire-apphost/` | Aspire AppHost for local F5 and container-manifest generation. |
| `skills/dotnet-agent-bootstrap/` | Runs `dotnet new`, adds packages, writes `Directory.Build.props` / `global.json` / `.editorconfig`, initialises git. |
| `skills/agent-evaluation-strategy/` | Test fixtures + datasets + `Microsoft.Extensions.AI.Evaluation` wiring. |
| `skills/agent-guardrails-safety/` | Middleware-based input / output / tool-call guardrails (PII, prompt injection, content filter). |
| `skills/agent-secrets-identity/` | `DefaultAzureCredential`, Key Vault references, OBO, federated credentials for ADO → Azure. |
| `references/` | Read-only example snippets the skills point at (no copy-paste shipping). |

## Install

This repo is kept **separate** from the project repos it helps you create. Install once into your home directory and use from any workspace.

### Windows (PowerShell)
```powershell
./install/install.ps1            # installs for both Copilot and Claude (default)
./install/install.ps1 -Tool copilot
./install/install.ps1 -Tool claude
./install/install.ps1 -Scope project   # symlink into ./.github/skills and ./.claude/skills of cwd
```

### macOS / Linux (bash)
```bash
./install/install.sh             # installs for both Copilot and Claude
./install/install.sh --tool copilot
./install/install.sh --scope project
```

The installer creates symlinks where possible (so edits to this repo are picked up immediately). On Windows without symlink privileges it falls back to copy.

## Use

After install, in any workspace:

> *"Help me build a code-first agent."*

The `code-first-agent` orchestrator picks up the request and walks you through the journey, delegating to skills as appropriate. You can also invoke any skill directly:

> *"Walk me through the architectural decisions for an agent."*
> *"Bootstrap a new C# Microsoft Agent Framework solution called `pr-reviewer`."*
> *"Generate the Azure Container Apps Bicep for an agent service."*

## Philosophy

- **Use Microsoft Agent Framework directly.** Don't build a wrapper library on day one. Extract a shared library only after two or more agents have proven they need the same abstractions.
- **One opinion per concern.** A single recommended path (Azure Container Apps + Bicep + ADO pipelines + Aspire AppHost) with each variation captured as an explicit decision in the architecture skill.
- **Vertical slice + orchestrator.** Group code by feature, not by technical layer. Tools live in their own projects so they can be shared across agents.
- **Markdown is code.** Agent instructions are checked-in assets with the same review discipline as C#.
- **Observability from day zero.** OpenTelemetry traces, tool-call spans, and an export target are configured before the first prompt runs.

## Repo layout

```
code-first-agent-starter/
├── agents/
│   └── code-first-agent.md
├── skills/
│   ├── <skill-name>/
│   │   └── SKILL.md
│   └── ...
├── references/                 # extracted, decontextualised example snippets
├── install/
│   ├── install.ps1
│   └── install.sh
├── docs/
│   └── walkthrough.md
└── README.md
```

## Contributing / extending

- Add a skill by creating `skills/<name>/SKILL.md` with the standard frontmatter (`name`, `description`). The `description` field is what both Copilot and Claude use to decide when to invoke the skill — make it specific and include the trigger phrases users actually say.
- Re-run `./install/install.ps1` (or `.sh`) after adding a new skill to register it.
- Reference snippets in `references/` are deliberately minimal. They illustrate one pattern at a time. Avoid expanding them into full applications — point the user at the skill that shipped them instead.

## License

MIT
