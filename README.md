# Code-First Agent Starter

An [APM](https://microsoft.github.io/apm/) **curated aggregator**: two agents + eleven focused skills, organised into four installable sub-packages, that walk a developer end-to-end through building a code-first AI agent — architecture decisions → C# / Microsoft Agent Framework scaffolding → Azure infrastructure → evaluation → guardrails → identity.

The work is split across two agents: **`agent-architect`** decides and documents (no code), then hands off to **`agent-builder`** which implements.

Distributed via APM so it installs into **GitHub Copilot, Claude Code, Cursor, OpenCode, Codex, Gemini, and Windsurf** from a single `apm install`, with version pinning and a content-hashed lockfile.

## Prerequisites

Install the **Azure VS Code extensions** before starting — several recommended (implementation-backed) options depend on the companion skills they ship (`azure-prepare`, `azure-validate`, `azure-deploy`, `azure-rbac`, `appinsights-instrumentation`, `entra-app-registration`):

- **Azure Tools** extension pack (or **Azure Resources** + **Container Apps** + **Bicep**).
- **Azure Developer CLI (azd)** support.
- **GitHub Copilot for Azure**.

## Install

### 1. Install the APM CLI (one-time)

```bash
# macOS / Linux
curl -sSL https://aka.ms/apm-unix | sh
```

Windows install: see [APM installation docs](https://microsoft.github.io/apm/getting-started/first-package/installation/).

### 2. Install this package into a project

```bash
apm install <owner>/code-first-agent-starter           # latest (all four sub-packages)
apm install <owner>/code-first-agent-starter#v0.2.0    # pinned
```

The root package is a curated aggregator — installing it pulls all four sub-packages. You can also install a single sub-package (e.g. `agent-design`) on its own.

APM auto-detects which harnesses are configured in the project and deploys each primitive to the right location. Force a single target with `--target copilot|claude|cursor|opencode|codex|gemini|windsurf`.

### 3. Use

In a fresh chat:

> *"Help me design a code-first agent."*

The `agent-architect` walks you through the decisions, then hands off to `agent-builder` to implement. Individual skills also auto-activate on description match — invoke one directly by stating its problem (e.g. *"Generate the Azure Container Apps Bicep for an agent service."*).

## What you get

### Agents

| File | Role |
|---|---|
| `agent-design/.apm/agents/agent-architect.agent.md` | Decides & documents. Proposes only implementation-backed options, grills alternatives, emits a decisions doc, then hands off. **Writes no code.** |
| `dotnet-implementation/.apm/agents/agent-builder.agent.md` | Implements from the decisions doc: scaffold → implement → infra → deploy → evaluate → harden. Also handles expanding an existing agent. |

### Skills (by sub-package)

**agent-design**

| Skill | Scope |
|---|---|
| `agent-architecture-decisions` | **Language-neutral.** Interviews on key architectural choices, recommends backed options, emits ADR-style artifact. |
| `agent-sandboxing` | **Security-first.** Decide how to safely execute model-generated code: runtime, egress, credential isolation, limits, audit. |

**dotnet-implementation**

| Skill | Scope |
|---|---|
| `dotnet-agent-bootstrap` | `dotnet new`, packages, `Directory.Build.props` / `global.json` / `.editorconfig`, git init. |
| `maf-csharp-implementation` | C# / Microsoft Agent Framework patterns: vertical-slice projects, tools-as-separate-projects, instructions loading, multi-turn sessions. |
| `dotnet-aspire-apphost` | Aspire AppHost for local F5 + container-manifest generation. |

**azure-infrastructure**

| Skill | Scope |
|---|---|
| `agent-infrastructure-overview` | The "what" of agent infrastructure; routes to leaf skills. |
| `azure-container-apps-bicep` | Bicep for ACA with managed identity, Key Vault refs, OTel wiring. |
| `azure-devops-pipelines-for-agents` | ADO YAML for build + deploy to Azure Container Apps. |
| `agent-secrets-identity` | `DefaultAzureCredential`, KV refs, OBO, federated credentials. |

**quality-safety**

| Skill | Scope |
|---|---|
| `agent-evaluation-strategy` | Fixtures + datasets + `Microsoft.Extensions.AI.Evaluation`. |
| `agent-guardrails-safety` | Middleware-based input / output / tool-call guardrails. |

### References

Reference snippets are decontextualised example code the skills cite. Each lives in a `references/` folder **inside the skill that uses it** (e.g. `maf-csharp-implementation/references/builder-and-tools.cs`), keeping every skill self-contained. Read-only.

## Philosophy

- **Use Microsoft Agent Framework directly.** Don't build a wrapper library on day one.
- **One opinion per concern.** A single recommended path (Azure Container Apps + Bicep + ADO + Aspire AppHost); variations are explicit decisions.
- **Vertical slice + orchestrator.** Group by feature; tools live in their own projects.
- **Markdown is code.** Agent instructions are checked-in assets with the same review discipline as C#.
- **Observability from day zero.** OpenTelemetry traces, tool-call spans, export target wired before the first prompt runs.

## Repo layout

```
code-first-agent-starter/
├── apm.yml                                  # curated aggregator (lists the 4 sub-packages)
├── agent-design/
│   ├── apm.yml
│   └── .apm/
│       ├── agents/agent-architect.agent.md
│       └── skills/{agent-architecture-decisions,agent-sandboxing}/SKILL.md
├── dotnet-implementation/
│   ├── apm.yml
│   └── .apm/
│       ├── agents/agent-builder.agent.md
│       └── skills/
│           ├── dotnet-agent-bootstrap/SKILL.md
│           ├── maf-csharp-implementation/
│           │   ├── SKILL.md
│           │   └── references/      # builder-and-tools.cs, instructions-embedded.cs, otel-azuremonitor.cs, orchestrator-cqrs.cs
│           └── dotnet-aspire-apphost/SKILL.md
├── azure-infrastructure/
│   ├── apm.yml
│   └── .apm/skills/
│       ├── agent-infrastructure-overview/SKILL.md
│       ├── azure-container-apps-bicep/{SKILL.md, references/container-apps.bicep}
│       ├── azure-devops-pipelines-for-agents/{SKILL.md, references/azure-pipelines.yml}
│       └── agent-secrets-identity/SKILL.md
├── quality-safety/
│   ├── apm.yml
│   └── .apm/skills/
│       ├── agent-evaluation-strategy/{SKILL.md, references/eval-fixture.cs}
│       └── agent-guardrails-safety/{SKILL.md, references/guardrail-middleware.cs}
├── docs/walkthrough.md
└── README.md
```

## Contributing / extending

- Add a skill at `<sub-package>/.apm/skills/<name>/SKILL.md` with `name` + `description` frontmatter. The directory name **must** equal the `name` field — directory wins on disk if they disagree.
- Add an agent at `<sub-package>/.apm/agents/<name>.agent.md` (note the `.agent.md` double extension).
- Pick the sub-package by concern: design → `agent-design`, C# implementation → `dotnet-implementation`, Azure infra → `azure-infrastructure`, eval/guardrails → `quality-safety`. Any reference snippet a skill cites lives in a `references/` folder **inside that skill's own folder**, linked as `references/<file>`. A skill that reuses a sibling skill's reference links across as `../<other-skill>/references/<file>`.
- Validate before committing (per sub-package and at the root):
  ```bash
  apm install --dry-run --target copilot
  apm install --dry-run --target claude
  apm audit
  ```
- Release: `git tag v0.2.0 && git push --tags`. Consumers pin with `#v0.2.0`.

## Local development

To test edits before tagging, install from a path in a scratch project:

```bash
apm install ../path/to/code-first-agent-starter
```

For air-gapped delivery, `apm pack` produces a `.tar.gz` bundle plus a plugin-format directory consumers can install offline.

## License

MIT
