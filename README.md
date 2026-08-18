# Code-First Agent Starter

An [APM](https://microsoft.github.io/apm/) **curated aggregator**: two agents + twenty focused skills, organised into five installable sub-packages — one **core MAF** package plus four **additive** solution layers — that walk a developer end-to-end through building a code-first AI agent — architecture decisions → C# / Microsoft Agent Framework scaffolding → Azure infrastructure → evaluation → guardrails → identity.

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

```bash
# Windows (PowerShell)
irm https://aka.ms/apm-windows | iex
```

More detail: see the [APM documentation](https://microsoft.github.io/apm/).

### 2. Install this package into a project

```bash
apm install <owner>/code-first-agent-starter           # latest (all five sub-packages)
apm install <owner>/code-first-agent-starter#v0.2.0    # pinned
```

The root package is a curated aggregator — installing it pulls all five sub-packages. You can also install a single sub-package (e.g. `maf-core` for just the framework skills, or `agent-design`) on its own.

APM auto-detects which harnesses are configured in the project and deploys each primitive to the right location. Force a single target with `--target copilot|claude|cursor|opencode|codex|gemini|windsurf`.

### 3. Use

In a fresh chat:

> *"Help me design a code-first agent."*

The `agent-architect` walks you through the decisions, then hands off to `agent-builder` to implement. Individual skills also auto-activate on description match — invoke one directly by stating its problem (e.g. *"Generate the Azure Container Apps Bicep for an agent service."*).

## What you get

### Agents

| File | Role |
|---|---|
| `agent-design/.apm/agents/agent-architect.agent.md` | Decides & documents. Discovers which skills are actually installed, recommends from those, grills alternatives, emits a short decisions summary, then hands off. **Writes no code.** |
| `dotnet-implementation/.apm/agents/agent-builder.agent.md` | Implements from the decisions doc: scaffold → implement → infra → deploy → evaluate → harden. Also handles expanding an existing agent. |

### Skills (by sub-package)

Skills split into **core MAF** — the parts that *are* Microsoft Agent Framework — and four **additive** layers you add around it to complete a solution.

#### Core MAF

**maf-core** — *the framework itself*

| Skill | Scope |
|---|---|
| `maf-csharp-implementation` | C# / Microsoft Agent Framework patterns: project-structure options, `AIProjectClient.AsAIAgent` wiring, tool authoring, instructions loading, multi-turn sessions, middleware, structured output, native OpenTelemetry. |
| `maf-workflows-orchestration` | Graph workflows + multi-agent orchestration (sequential, concurrent, handoff, group-chat, magentic), human-in-the-loop, checkpointing. |
| `maf-mcp-tools` | MCP tool surface: local + provider-hosted MCP tools, and exposing the agent as an MCP server. |
| `maf-hosted-tools` | Foundry-hosted (provider-executed) tools: web search, code interpreter, file search, Bing grounding, Azure AI Search, SharePoint, image generation, Foundry Toolbox — agent runs locally, tool runs on the Foundry Responses runtime. |
| `maf-remote-agents` | Connect (as a client) to service-managed / remote agents: `FoundryAgent` for Prompt Agents + Hosted Agents (version pinning), plus Copilot Studio and A2A agents. |
| `maf-memory-context` | Beyond sessions: memory providers (Foundry memory, mem0, Redis), context providers, persistence, and when to reach for RAG/structured retrieval. |

#### Additive solution layers

**agent-design**

| Skill | Scope |
|---|---|
| `agent-architecture-decisions` | **Language-neutral.** Discovers installed skills, adaptively interviews on the architectural choices that matter, emits a short decisions summary. |
| `agent-sandboxing` | **Security-first.** Decide how to safely execute model-generated code: runtime, egress, credential isolation, limits, audit. |

**dotnet-implementation** — *.NET scaffolding & hosting around the core*

| Skill | Scope |
|---|---|
| `dotnet-agent-bootstrap` | `dotnet new`, packages (`Microsoft.Agents.AI` + Foundry/Projects prerelease), `Directory.Build.props` / `global.json` / `.editorconfig`, git init. |
| `dotnet-aspire-apphost` | Hosting & local dev: Aspire AppHost, DevUI, ASP.NET Core self-hosting — with Foundry Hosted Agents / Azure Functions alternatives. |
| `agent-sandbox-csharp` | C# sandbox execution: hosted Code Interpreter, ACA dynamic-sessions glue, local Docker runtime. |
| `foundry-hosted-agents` | Deploy the whole agent as a managed, containerised app on Foundry Agent Service: `Microsoft.Agents.AI.Foundry.Hosting`, Responses/Invocations protocols, `azd ai agent` local dev + deploy. |

**azure-infrastructure**

| Skill | Scope |
|---|---|
| `agent-infrastructure-overview` | The "what" of agent infrastructure; routes to leaf skills; ACA/ADO as options alongside Foundry Hosted Agents. |
| `azure-container-apps-bicep` | Bicep for ACA with managed identity, Key Vault refs, OTel wiring (one hosting option). |
| `azure-container-apps-sessions-bicep` | Bicep for ACA dynamic session pools (code-execution sandbox runtimes). |
| `azure-devops-pipelines-for-agents` | ADO YAML for build + deploy to Azure Container Apps (GitHub Actions noted as an alternative). |
| `agent-secrets-identity` | `DefaultAzureCredential`, KV refs, OBO, federated credentials. |
| `foundry-model-deployment` | Bicep for an Azure AI Foundry account + project + model deployment; outputs the project endpoint. |

**quality-safety**

| Skill | Scope |
|---|---|
| `agent-evaluation-strategy` | Fixtures + datasets + `Microsoft.Extensions.AI.Evaluation`. |
| `agent-guardrails-safety` | Middleware-based input / output / tool-call guardrails. |

### References

Where a pattern is documented in the official Microsoft Agent Framework docs, skills **link to the living documentation and samples** rather than embedding code that goes stale. Reference files remain only for code with no good doc equivalent — Bicep, pipeline YAML, sandbox glue, and the eval fixture. Each lives in a `references/` folder **inside the skill that uses it** (e.g. `agent-evaluation-strategy/references/eval-fixture.cs`), keeping every skill self-contained. Read-only.

## Philosophy

- **Use Microsoft Agent Framework directly.** Don't build a wrapper library on day one.
- **A paved path, not a straitjacket.** We suggest a starting point for each concern (e.g. Azure Container Apps, Aspire, Azure AI Foundry) but present alternatives with tradeoffs — hosting, CI/CD, orchestration, and tool surface are *your* decisions, documented rather than mandated.
- **Start simple, add structure when it pays for itself.** Begin with a minimal MAF-idiomatic layout; adopt vertical slices, separate tool projects, MCP tool surfaces, or a workflow orchestrator only when the product grows to need them.
- **Link to the living docs.** Skills cite the official Microsoft Agent Framework documentation and samples for code patterns rather than embedding snapshots that go stale.
- **Markdown is code.** Agent instructions are checked-in assets with the same review discipline as C#.
- **Observability from day zero.** OpenTelemetry traces via MAF's native `.UseOpenTelemetry()` / `.WithOpenTelemetry()`, tool-call spans, export target wired before the first prompt runs.

## Repo layout

```
code-first-agent-starter/
├── apm.yml                                  # curated aggregator (lists the 5 sub-packages)
├── maf-core/                                # ← IS Microsoft Agent Framework
│   ├── apm.yml
│   └── .apm/skills/
│       ├── maf-csharp-implementation/SKILL.md    # link-first (no embedded references)
│       ├── maf-workflows-orchestration/SKILL.md
│       ├── maf-mcp-tools/SKILL.md
│       ├── maf-hosted-tools/SKILL.md
│       ├── maf-remote-agents/SKILL.md
│       └── maf-memory-context/SKILL.md
├── agent-design/                            # ← additive: design decisions
│   ├── apm.yml
│   └── .apm/
│       ├── agents/agent-architect.agent.md
│       └── skills/{agent-architecture-decisions,agent-sandboxing}/SKILL.md
├── dotnet-implementation/                   # ← additive: .NET scaffolding & hosting
│   ├── apm.yml
│   └── .apm/
│       ├── agents/agent-builder.agent.md
│       └── skills/
│           ├── dotnet-agent-bootstrap/SKILL.md
│           ├── dotnet-aspire-apphost/SKILL.md
│           ├── foundry-hosted-agents/SKILL.md
│           └── agent-sandbox-csharp/{SKILL.md, references/}   # ISandbox, ACA sessions, local docker, executor
├── azure-infrastructure/                    # ← additive: Azure hosting/infra
│   ├── apm.yml
│   └── .apm/skills/
│       ├── agent-infrastructure-overview/SKILL.md
│       ├── azure-container-apps-bicep/{SKILL.md, references/container-apps.bicep}
│       ├── azure-container-apps-sessions-bicep/{SKILL.md, references/aca-session-pool.bicep}
│       ├── azure-devops-pipelines-for-agents/{SKILL.md, references/azure-pipelines.yml}
│       ├── agent-secrets-identity/SKILL.md
│       └── foundry-model-deployment/{SKILL.md, references/azure-ai-foundry.bicep}
├── quality-safety/                          # ← additive: eval & guardrails
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
- Pick the sub-package by concern: **is it part of Microsoft Agent Framework itself?** → `maf-core`. Otherwise it's additive: design → `agent-design`, .NET scaffolding/hosting → `dotnet-implementation`, Azure infra → `azure-infrastructure`, eval/guardrails → `quality-safety`. Any reference snippet a skill cites lives in a `references/` folder **inside that skill's own folder**, linked as `references/<file>`. A skill that reuses a sibling skill's reference links across as `../<other-skill>/references/<file>`.
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
