---
name: agent-architecture-decisions
description: Walk a developer through the architectural decisions required to build a code-first AI agent — trigger model, observability, hosting, sandboxing, tool surface including MCP, context and memory providers, orchestration/determinism, guardrails, identity, CI/CD, and model deployment — by comparing implementation-backed options and tradeoffs before producing an ADR-style decisions document. Use this skill at the start of any new agent project, when modernising an existing one, or whenever someone asks "how should I structure my agent", "what should I decide before I build an agent", "design my agent architecture", "what are the trade-offs for X agent decision", or mentions ADRs, arc42, or architecture decisions for AI agents. Language-neutral — applies regardless of SDK or platform; keep security-first decisions explicit.
---

# Agent Architecture Decisions

A guided interview that surfaces the architectural decisions every code-first agent project should make explicitly, then captures them in a documented form. The output is **decisions with rationale**, not code.

This skill is driven by the `agent-architect` agent. Hand off to implementation skills (e.g. `dotnet-agent-bootstrap`, `maf-csharp-implementation`) — via the `agent-builder` agent — only after the decisions document is complete.

## When to use

- Greenfield agent project — before any code is written.
- Existing agent project missing documented decisions.
- A new architectural concern arises (e.g. adding sandboxing, switching hosting target).

## Goal

Produce a written record of the decisions below, each with: **chosen option**, **whether it follows a backed/documented starting point or is a custom alternative**, **rationale**, and a **"revisit when" trigger**.

## Core principle — recommend only what we can explain and build

Start from implementation-backed options: choices a skill in this starter, a companion Azure skill, or current official Microsoft Agent Framework documentation explains well enough to build. Some concerns have several reasonable options; do **not** force a single default for hosting, CI/CD, or orchestration. Compare tradeoffs neutrally, recommend a starting point when useful, and record why the chosen option fits.

The user **may always propose an alternative**. Use the protocol below for unsupported, high-risk, or platform-leaving choices (for example leaving C#/MAF, leaving Azure entirely, bypassing the sandbox for generated code, or weakening identity/secret controls). Do not use it as a grilling gate for ordinary choices among the hosting, CI/CD, or orchestration options listed here.

### Implementation-backed / documented option map

A choice is "backed" when a skill exists to build it. A choice is "documented" when the official MAF docs describe the pattern but this starter does not yet ship a dedicated implementation skill. `*` marks a **companion skill** that ships with the Azure VS Code extensions (still counts as backed — confirm the extensions are installed).

| Decision | Backed or documented option(s) | Backing skill(s) / docs |
|---|---|---|
| Language / SDK | C# + Microsoft Agent Framework | `dotnet-agent-bootstrap`, `maf-csharp-implementation` |
| Hosting | Self-hosted ASP.NET Core on Azure Container Apps or App Service; Azure Functions/Durable; Foundry Hosted Agents | `azure-container-apps-bicep`, `azure-prepare`*; [MAF hosting](https://learn.microsoft.com/en-us/agent-framework/hosting/) |
| Observability | App Insights / Azure Monitor; OTLP + Aspire (local) | `otel` reference + `appinsights-instrumentation`*; `dotnet-aspire-apphost` |
| Tool surface | In-process C# tools; MCP tools for shared/remote capabilities | `maf-csharp-implementation`; `maf-mcp-tools` when installed; [MAF tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/) |
| CI/CD pipeline | Azure DevOps pipeline; GitHub Actions using the same validate/deploy stages | `azure-devops-pipelines-for-agents`; custom workflow if the repo standard is GitHub |
| Identity & secrets | UAMI, Key Vault refs, OBO, workload identity federation | `agent-secrets-identity`, `azure-rbac`*, `entra-app-registration`* |
| Deploy lifecycle | validate → deploy | `azure-validate`*, `azure-deploy`* |
| Code-execution sandbox | Provider-hosted code interpreter, MAF shell tools, ACA dynamic sessions, or third-party sandboxes depending on the sandbox decision | `agent-sandboxing` |
| Memory / context | MAF sessions, context providers, persistence, RAG, structured retrieval | [Memory & persistence](https://learn.microsoft.com/en-us/agent-framework/get-started/memory), [Conversations & memory](https://learn.microsoft.com/en-us/agent-framework/concepts/agents/conversations/) |
| Determinism / orchestration | Single agent, MAF Workflows graph/orchestration, or app-level CQRS orchestrator | `maf-workflows-orchestration` when installed; `maf-csharp-implementation` CQRS reference; [MAF workflows](https://learn.microsoft.com/en-us/agent-framework/workflows/) |
| Model deployment | Azure AI Foundry project endpoint + model deployment | `foundry-model-deployment`; [Foundry model provider](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/microsoft-foundry) |

### Protocol: when the user chooses an unsupported or high-risk alternative

Apply this block when the user wants something outside the backed/documented paths, or when they want to weaken a security boundary:

1. **Acknowledge** the alternative and restate the backed/documented option it replaces.
2. **Research** it before debating — use a research subagent and/or web fetches to get current, specific facts (maturity, cost, operational burden, how it integrates with the rest of the stack). Don't grill from memory.
3. **Grill** until shared understanding: what does leaving the paved path cost in build effort, operations, and the loss of the backing skill? What concretely makes the backed option unworkable here? Push back on vague answers.
4. **Record** in the decisions document: the chosen alternative, **why the backed option was rejected**, the residual risk, and a **revisit trigger**.

If the user can't articulate why a high-risk alternative is necessary, recommend the safer backed/documented option.

## The decision set

Walk through these in order. One cluster at a time: state the recommended starting point or option table, capture the answer, move on. Resolve upstream decisions before downstream ones.

### 0. Language / SDK

- **Backed starting point:** C# + Microsoft Agent Framework (`dotnet-agent-bootstrap`).
- Any other language/SDK weakens every downstream C# implementation skill. If they choose it, run the protocol and record which implementation skills no longer apply.

### 1. Trigger model — how does the outside world invoke the agent?

| Option | Strengths | Costs |
|---|---|---|
| **Streaming chat (HTTP/SSE or WebSocket)** | Low latency, multi-turn UX, easy to demo. | Long-lived connections; harder horizontal scale; need session affinity or external state. |
| **Event-driven (queue / event grid / service bus)** | Decoupled, retry semantics, scales horizontally. | Higher latency to user; needs a result-delivery channel. |
| **Scheduled / cron** | Predictable load; good for periodic reviews. | No user interactivity; output channel must be defined separately. |
| **Webhook (single-shot HTTP)** | Simple to expose; good for integrations (e.g. PR review on push). | No streaming; must complete within hosting timeout. |
| **CLI / desktop process** | Fastest dev loop; no hosting needed. | Not shareable; user is the runtime. |

Ask: *Does the agent need to stream tokens to a human, or can it produce a final answer asynchronously?* That single question collapses most of the matrix.

### 2. Observability — how will you see what the agent is doing?

- **Backed starting point:** OpenTelemetry as the trace backbone, exporting to **Application Insights / Azure Monitor** in Azure (`appinsights-instrumentation`*) and to the **OTLP / Aspire dashboard** locally (`dotnet-aspire-apphost`).
- Self-hosted backends (Jaeger, Grafana Tempo) or SaaS (Datadog, Honeycomb) are valid if the organisation already standardises on them; record exporter ownership and PII handling.

Also capture:
- **What you trace**: agent runs, tool calls (args in dev, hashed in prod), prompt/response sizes, token counts, latencies, errors.
- **Log retention & PII**: how long, redacted or raw, who can read.
- **Dashboards & alerts**: SLOs (p95 latency, success rate, cost per run).

### 3. Hosting model — where does the agent run?

Use [MAF hosting](https://learn.microsoft.com/en-us/agent-framework/hosting/) as the framing: Microsoft-managed Foundry hosting versus self-hosting.

| Option | When it fits | Tradeoffs |
|---|---|---|
| **Self-host ASP.NET Core on Azure Container Apps** | Containerised agent, streaming HTTP, custom middleware, network controls, KEDA scale-to-zero. Good starting point for this starter because the infra skills are ready. | You own image build, container runtime, scaling, and rollout. |
| **Self-host ASP.NET Core on App Service** | Simpler web-app operations, existing App Service standard, HTTP-first workloads. | Less event/KEDA-native; scale-to-zero and container/session patterns may differ. |
| **Foundry Hosted Agents** | You want Microsoft-managed hosted agents and less app-host infrastructure. | Less control over custom host process and local parity; verify tool/network requirements. See [Foundry Hosted Agents](https://learn.microsoft.com/en-us/agent-framework/hosting/foundry-hosted-agent). |
| **Azure Functions / Durable** | Event-driven, durable workflow, background processing, or long-running orchestration. | Function execution model and cold-start constraints shape UX. See [Azure Functions hosting](https://learn.microsoft.com/en-us/agent-framework/hosting/azure-functions). |
| **CLI / desktop / edge** | Local-only assistant or developer tool. | Not a shared service; identity, telemetry, and updates are a different problem. |

For self-hosted MAF apps, see [Self-host Agent Framework applications](https://learn.microsoft.com/en-us/agent-framework/hosting/self-hosting/). Pick the host based on trigger model, operational standard, network needs, and sandbox/tool requirements — not because one default is mandatory.

### 4. Code-execution sandbox — does the agent run model-generated code?

A yes/no gate, then a dedicated decision branch.

- A sandbox is **required whenever the agent executes any form of model-generated code or command** (code interpreter, data analysis, `run_command`, build/test loops, coding agent, LLM-chosen filesystem/git). It is **not** required when the agent only calls typed tools, only returns code as text, or only retrieves context.
- If execution is in scope, **defer to the `agent-sandboxing` skill** for the full decision: whether to execute, runtime choice, execution model, per-environment images where relevant, egress policy, credential isolation, resource/time/scaling limits, audit, and local dev runtime. Capture its output here.
- If the agent never executes generated code, record that explicitly and move on.

Security note: open network egress from untrusted, model-generated code is the highest-risk choice in the whole design. Don't wave it through.

### 5. Tool surface — what can the agent do, and how is it exposed?

| Option | When it fits | Tradeoffs |
|---|---|---|
| **In-process C# tools** | The capability belongs to this agent, uses local DI/services, and can be versioned with the app. | Tight coupling to the agent deployment; less reusable across agents. |
| **MCP tool server** | A tool is shared across agents, owned by another team, remote, or useful to non-.NET clients. Use `maf-mcp-tools` when installed and the [MAF MCP tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/local-mcp-tools) docs. | Extra process/protocol, auth boundary, schema governance, and audit surface. |
| **External HTTP/API tool** | Existing service already exposes a safe contract. | Need hand-written client, retries, auth, and schema validation. |
| **No tool — context only** | Static/small data can be pre-fetched into the prompt. | Cheapest and most deterministic, but stale if the source changes mid-run. |

For each candidate tool record: name, input/output shape, side effect (read/write/external-call), idempotency, owner, approval requirement, and audit data.

### 6. Context, memory, and grounding sources

Use MAF sessions/context providers rather than inventing a hidden memory layer. See [Memory & persistence](https://learn.microsoft.com/en-us/agent-framework/get-started/memory) and [Conversations & memory](https://learn.microsoft.com/en-us/agent-framework/concepts/agents/conversations/).

| Source/provider | When |
|---|---|
| **Pre-fetched context in the prompt** | Stable, small, per-session — cheapest and most deterministic. |
| **Session memory / persisted conversation state** | Multi-turn UX needs continuity across requests or restarts. |
| **Context provider** | The agent needs dynamic, structured context injected at run time without making it a model-called tool. |
| **Tool-fetched at runtime** | Dynamic data depends on the conversation and fits the tool model. |
| **RAG / vector search** | Large corpus, semantic queries, no exact schema. |
| **Structured retrieval (SQL / Graph / API)** | Source of truth has a schema; you want filters and joins. |
| **MCP resource** | Cross-agent shared context or a context source owned by another team. |

Be explicit about staleness, caching, eviction, storage location, PII, and who can inspect memory. If a context source needs new infrastructure, treat that infrastructure as its own decision.

### 7. Flexibility, determinism, and multi-agent orchestration

This is an option set, not a single mandated orchestrator.

| Option | When it fits | Tradeoffs |
|---|---|---|
| **Single agent call / loop** | One agent, one main instruction set, a few tools, conversation drives the flow. | Least overhead; less deterministic for repeatable multi-step processes. |
| **MAF Workflows graph** | Steps, branches, checkpoints, human-in-the-loop, or multiple agents need a deterministic skeleton. Use `maf-workflows-orchestration` when installed plus [MAF workflows](https://learn.microsoft.com/en-us/agent-framework/workflows/). | More design upfront; workflow state and versioning become first-class. |
| **MAF orchestration pattern** | Multi-agent handoff, group chat, concurrent, sequential, or magentic coordination. See [Workflow orchestrations](https://learn.microsoft.com/en-us/agent-framework/workflows/orchestrations/). | Requires clear agent roles, termination rules, and audit of cross-agent handoffs. |
| **App-level CQRS/orchestrator** | The product already standardises on Brighter/MediatR/CQRS and agent calls are just one step in a larger app workflow. | Keep agent code MAF-native; don't bury agent concerns in generic command handlers. |

Ask: *Which steps must be the same every time?* Those are workflow/orchestrator. *Which steps depend on conversation judgment?* Those stay in the agent.

### 8. Guardrails — what must never happen?

Defer to `agent-guardrails-safety` for implementation. Capture the **policy** decisions here:
- PII handling (detect, redact, block, log)
- Prompt-injection posture
- Content-filter level
- Tool-call allow/deny rules and approvals
- Audit-log retention and access

### 9. Identity & secrets

- **Backed:** user-assigned managed identity, Key Vault references, OBO where the agent acts as the user, workload identity federation for CI/CD (`agent-secrets-identity`, `azure-rbac`*, `entra-app-registration`*).
- Service principals with stored secrets, or secrets in app settings, are alternatives → run the protocol.

### 10. CI/CD pipeline and deploy lifecycle

Keep the lifecycle shape the same regardless of runner: build/test → validate infrastructure → deploy → smoke test.

| Option | When it fits | Tradeoffs |
|---|---|---|
| **Azure DevOps Pipelines** | Organisation uses Azure DevOps repos/boards/pipelines or wants the shipped starter pipeline. | Backed by `azure-devops-pipelines-for-agents`; ADO-specific YAML and service connections. |
| **GitHub Actions** | Repository and policy already live in GitHub. | Valid option; translate the same validate/deploy stages and use workload identity federation. |
| **Manual / local deploy** | Prototype only. | Record a short revisit trigger; not acceptable for production without auditability. |

Do not grill Azure DevOps vs GitHub Actions as if one is inherently correct. Choose the runner that matches repository governance and deployment controls. Do grill proposals that skip validation, store long-lived secrets, or bypass review.

### 11. Model deployment — what model will the agent use?

- **Backed:** **Azure AI Foundry** — an `AIServices`-kind account with a Foundry project and a model deployment (`foundry-model-deployment`). New MAF code should use the **project endpoint** and `AIProjectClient.AsAIAgent(...)`, not the legacy `/models` inference endpoint. See the [Foundry model provider](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/microsoft-foundry).
- **Option: Use an existing Foundry project.** If a Foundry account/project and deployment already exist, record the Foundry project endpoint from the portal (the `services.ai.azure.com/api/projects/<project>` URL) and deployment name — no Bicep needed.
- **Option: `OpenAI`-kind AOAI resource.** Loses access to non-OpenAI catalog models and Foundry platform features → run the protocol if that tradeoff matters.

**Sub-decision — model choice.** Any approved catalog model/deployment can be valid. Resolve with:
- What modalities does the agent need (text, vision, code, tool use)?
- Are there cost or latency constraints that rule out frontier models?
- Is there a data-residency or approval requirement for the model tier?

Use a neutral placeholder such as `<deployment-name>` in reusable templates. Non-OpenAI models require no change to MAF agent code when they are exposed through the selected provider; only the deployment/configuration changes.

**Order:** Resolve after **#3 Hosting** (sets the region; use the same region where practical to minimise latency and cross-region egress) and before **#9 Identity** (the UAMI needs the appropriate Foundry access role).

**Capture:**
- Project endpoint → `AZURE_AI_PROJECT_ENDPOINT`
- Deployment name → `AZURE_AI_MODEL_DEPLOYMENT_NAME`

## Producing the artifact

Two paths — **ask the user which they prefer**:

1. **The user already has a documentation skill / convention.** Use it. Pass it this skill's decision set as input.
2. **No existing convention.** Suggest one of:
   - **arc42** (section 9 — "Architecture Decisions"), one ADR per decision above.
   - **MADR** (Markdown Any Decision Record) — `docs/adr/0001-trigger-model.md`, one file per decision.
   - **Single `decisions.md`** — simplest; one page, each decision a heading.

Whichever path, every decision must record: **chosen option**, **backed/documented starting point or custom alternative**, **rationale**, **security implications**, and **revisit trigger**.

## Hand-off

When the decisions document is complete and the user confirms shared understanding, the `agent-architect` agent hands off to `agent-builder`. Do **not** start coding in this skill. Decisions first.
