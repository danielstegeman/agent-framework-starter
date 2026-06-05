# Code-First Agent Starter

An [APM](https://microsoft.github.io/apm/) package: one orchestrator agent + ten focused skills that walk a developer end-to-end through building a code-first AI agent — architecture decisions → C# / Microsoft Agent Framework scaffolding → Azure infrastructure → evaluation → guardrails → identity.

Distributed via APM so it installs into **GitHub Copilot, Claude Code, Cursor, OpenCode, Codex, Gemini, and Windsurf** from a single `apm install`, with version pinning and a content-hashed lockfile.

## Install

### 1. Install the APM CLI (one-time)

```bash
# macOS / Linux
curl -sSL https://aka.ms/apm-unix | sh
```

Windows install: see [APM installation docs](https://microsoft.github.io/apm/getting-started/first-package/installation/).

### 2. Install this package into a project

```bash
apm install <owner>/code-first-agent-starter           # latest
apm install <owner>/code-first-agent-starter#v0.1.0    # pinned
```

APM auto-detects which harnesses are configured in the project and deploys each primitive to the right location. Force a single target with `--target copilot|claude|cursor|opencode|codex|gemini|windsurf`.

### 3. Use

In a fresh chat:

> *"Help me build a code-first agent."*

The `code-first-agent` orchestrator takes it from there. Individual skills also auto-activate on description match — invoke one directly by stating its problem (e.g. *"Generate the Azure Container Apps Bicep for an agent service."*).

## What you get

### Agent

| File | Role |
|---|---|
| `.apm/agents/code-first-agent.agent.md` | Orchestrator that sequences: discover → decide → scaffold → implement → infra → evaluate → harden. |

### Skills

| Skill | Scope |
|---|---|
| `agent-architecture-decisions` | **Language-neutral.** Interviews on key architectural choices, emits ADR-style artifact. |
| `maf-csharp-implementation` | C# / Microsoft Agent Framework patterns: vertical-slice projects, tools-as-separate-projects, instructions loading, multi-turn sessions. |
| `agent-infrastructure-overview` | The "what" of agent infrastructure; routes to leaf skills. |
| `azure-devops-pipelines-for-agents` | ADO YAML for build + deploy to Azure Container Apps. |
| `azure-container-apps-bicep` | Bicep for ACA with managed identity, Key Vault refs, OTel wiring. |
| `dotnet-aspire-apphost` | Aspire AppHost for local F5 + container-manifest generation. |
| `dotnet-agent-bootstrap` | `dotnet new`, packages, `Directory.Build.props` / `global.json` / `.editorconfig`, git init. |
| `agent-evaluation-strategy` | Fixtures + datasets + `Microsoft.Extensions.AI.Evaluation`. |
| `agent-guardrails-safety` | Middleware-based input / output / tool-call guardrails. |
| `agent-secrets-identity` | `DefaultAzureCredential`, KV refs, OBO, federated credentials. |

### References

The [references/](references/) folder holds decontextualised example snippets the skills cite. Read-only — not shipped to consumers by APM.

## Philosophy

- **Use Microsoft Agent Framework directly.** Don't build a wrapper library on day one.
- **One opinion per concern.** A single recommended path (Azure Container Apps + Bicep + ADO + Aspire AppHost); variations are explicit decisions.
- **Vertical slice + orchestrator.** Group by feature; tools live in their own projects.
- **Markdown is code.** Agent instructions are checked-in assets with the same review discipline as C#.
- **Observability from day zero.** OpenTelemetry traces, tool-call spans, export target wired before the first prompt runs.

## Repo layout

```
code-first-agent-starter/
├── apm.yml                                 # APM manifest
├── .apm/                                   # source primitives (deployed by APM)
│   ├── agents/
│   │   └── code-first-agent.agent.md
│   └── skills/
│       └── <skill-name>/SKILL.md
├── references/                             # snippets the skills cite (not deployed)
├── docs/walkthrough.md
└── README.md
```

## Contributing / extending

- Add a skill at `.apm/skills/<name>/SKILL.md` with `name` + `description` frontmatter. The directory name **must** equal the `name` field — directory wins on disk if they disagree.
- Add an agent at `.apm/agents/<name>.agent.md` (note the `.agent.md` double extension).
- Validate before committing:
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
