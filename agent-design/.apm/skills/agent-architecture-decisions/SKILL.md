---
name: agent-architecture-decisions
description: Walk a developer through the architectural decisions required to build a code-first AI agent — trigger model, observability, hosting, sandboxing, tool surface, context sources, determinism, guardrails, identity — recommending only implementation-backed options and grilling any alternative before producing an ADR-style decisions document. Use this skill at the start of any new agent project, when modernising an existing one, or whenever someone asks "how should I structure my agent", "what should I decide before I build an agent", "design my agent architecture", "what are the trade-offs for X agent decision", or mentions ADRs, arc42, or architecture decisions for AI agents. Language-neutral — applies regardless of SDK or platform.
---

# Agent Architecture Decisions

A guided interview that surfaces the architectural decisions every code-first agent project should make explicitly, then captures them in a documented form. The output is **decisions with rationale**, not code.

This skill is driven by the `agent-architect` agent. Hand off to implementation skills (e.g. `dotnet-agent-bootstrap`, `maf-csharp-implementation`) — via the `agent-builder` agent — only after the decisions document is complete.

## When to use

- Greenfield agent project — before any code is written.
- Existing agent project missing documented decisions.
- A new architectural concern arises (e.g. adding sandboxing, switching hosting target).

## Goal

Produce a written record of the decisions below, each with: **chosen option**, **whether it is the implementation-backed default or an alternative** (and why), **rationale**, and a **"revisit when" trigger**.

## Core principle — recommend only what we can build

For every decision I **propose only implementation-backed options**: choices a skill in this starter (or a companion skill from the Azure VS Code extensions) already documents how to build. That is my recommendation and the paved path.

The user **may always propose an alternative**. They are never locked into the backed option. But an alternative must be *earned* — see the protocol below.

### Implementation-backed option map

A choice is "backed" if a skill exists to build it. `*` marks a **companion skill** that ships with the Azure VS Code extensions (still counts as backed — confirm the extensions are installed).

| Decision | Backed option(s) | Backing skill(s) |
|---|---|---|
| Language / SDK | C# + Microsoft Agent Framework | `dotnet-agent-bootstrap`, `maf-csharp-implementation` |
| Hosting | Azure Container Apps | `azure-container-apps-bicep`, `azure-prepare`* |
| Observability | App Insights / Azure Monitor; OTLP + Aspire (local) | `otel` reference + `appinsights-instrumentation`*; `dotnet-aspire-apphost` |
| Tool surface | In-process C# tools | `maf-csharp-implementation` |
| CI/CD pipeline | Azure DevOps | `azure-devops-pipelines-for-agents` |
| Identity & secrets | UAMI, Key Vault refs, OBO, workload identity federation | `agent-secrets-identity`, `azure-rbac`*, `entra-app-registration`* |
| Deploy lifecycle | validate → deploy | `azure-validate`*, `azure-deploy`* |
| Code-execution sandbox | e2b / Daytona / ACI / Container Apps job-per-run | `agent-sandboxing` |
| Determinism | workflow orchestrator with agent steps | `maf-csharp-implementation` (orchestrator reference) |
| Model deployment | Azure AI Foundry (AIServices account + project + model) | `foundry-model-deployment` |

Anything not in this table — App Service, Functions, AKS, GitHub Actions, MCP/HTTP tool surface, a non-C# SDK, a self-hosted trace backend — is an **alternative**.

### Protocol: when the user chooses an alternative

Apply this block any time the user wants something off the backed path:

1. **Acknowledge** the alternative and restate the backed option it replaces.
2. **Research** it before debating — use a research subagent and/or `web` to get current, specific facts (maturity, cost, operational burden, how it integrates with the rest of the stack). Don't grill from memory.
3. **Grill** until shared understanding: what does leaving the paved path cost in build effort, operations, and the loss of the backing skill? What concretely makes the backed option unworkable here? Push back on vague answers.
4. **Record** in the decisions document: the chosen alternative, **why the backed option was rejected**, the residual risk, and a **revisit trigger** (the condition under which we'd return to the paved path).

If the user can't articulate why the backed option fails them, recommend the backed option.

## The decision set

Walk through these in order. One cluster at a time: state the backed recommendation, note that an alternative is allowed, capture the answer, move on. Resolve upstream decisions before downstream ones.

### 0. Language / SDK

- **Backed:** C# + Microsoft Agent Framework (`dotnet-agent-bootstrap`).
- Any other language/SDK is an alternative → run the protocol. Note: the rest of this starter's implementation skills assume C#/MAF, so an alternative here weakens every downstream backing.

### 1. Trigger model — how does the outside world invoke the agent?

| Option | Strengths | Costs |
|---|---|---|
| **Streaming chat (HTTP/SSE or WebSocket)** | Low latency, multi-turn UX, easy to demo. | Long-lived connections; harder horizontal scale; need session affinity or external state. |
| **Event-driven (queue / event grid / service bus)** | Decoupled, retry semantics, scales horizontally. | Higher latency to user; needs a result-delivery channel. |
| **Scheduled / cron** | Predictable load; good for periodic reviews. | No user interactivity; output channel must be defined separately. |
| **Webhook (single-shot HTTP)** | Simple to expose; good for integrations (e.g. PR review on push). | No streaming; must complete within hosting timeout. |
| **CLI / desktop process** | Fastest dev loop; no hosting needed. | Not shareable; user is the runtime. |

Ask: *Does the agent need to stream tokens to a human, or can it produce a final answer asynchronously?* That single question collapses most of the matrix. All these trigger models are supported on the backed hosting option (Container Apps).

### 2. Observability — how will you see what the agent is doing?

- **Backed:** OpenTelemetry as the trace backbone (confirm), exporting to **Application Insights / Azure Monitor** in Azure (`appinsights-instrumentation`*) and to the **OTLP / Aspire dashboard** locally (`dotnet-aspire-apphost`).
- Self-hosted backends (Jaeger, Grafana Tempo) or SaaS (Datadog, Honeycomb) are alternatives → run the protocol.

Also capture (these are policy, not backing-dependent):
- **What you trace**: agent runs, tool calls (args in dev, hashed in prod), prompt/response sizes, token counts, latencies, errors.
- **Log retention & PII**: how long, redacted or raw, who can read.
- **Dashboards & alerts**: SLOs (p95 latency, success rate, $/run).

### 3. Hosting model — where does the agent run?

- **Backed:** **Azure Container Apps** — containerised, autoscaling to zero, KEDA-aware, supports HTTP + jobs (`azure-container-apps-bicep`, `azure-prepare`*).
- App Service, Azure Functions, AKS, edge/desktop are alternatives → run the protocol (research the scaling / networking / cold-start trade-offs before grilling).

### 4. Code-execution sandbox — does the agent run model-generated code?

A yes/no gate, then a dedicated decision branch.

- A sandbox is **required whenever the agent executes any form of model-generated code or command** (code interpreter, data analysis, `run_command`, build/test loops, coding agent, LLM-chosen filesystem/git). It is **not** required when the agent only calls typed tools, only returns code as text, or only retrieves context.
- If execution is in scope, **defer to the `agent-sandboxing` skill** for the full decision (whether to execute, runtime choice — Container Apps dynamic sessions custom container is the default — execution model, per-environment images, egress policy, credential isolation, resource/time/scaling limits, audit, local dev runtime). Capture its output here.
- If the agent never executes generated code, record that explicitly and move on.

Security note: open network egress from untrusted, model-generated code is the highest-risk choice in the whole design. Don't wave it through.

### 5. Tool surface — what can the agent do?

- **Backed:** **in-process C# tools** (methods with `[Description]`) — default for capabilities the agent owns (`maf-csharp-implementation`).
- MCP servers, external HTTP-API tools are alternatives → run the protocol (MCP is reasonable when a tool is shared across agents or owned by another team — but it's still off the backed path, so record why).
- "No tool — context only": pre-fetch and put in the prompt; cheaper and more predictable. Prefer this when it fits.

For each candidate tool record: name, input/output shape, side-effect (read/write/external-call), idempotency, owner.

### 6. Context sources — where does grounding come from?

| Source | When |
|---|---|
| **Pre-fetched context in the prompt** | Stable, small, per-session — cheapest and most deterministic. |
| **Tool-fetched at runtime** | Dynamic, depends on the conversation, fits the tool model. |
| **RAG (vector search)** | Large corpus, semantic queries, no exact schema. |
| **Structured retrieval (SQL / Graph / API)** | Source of truth has a schema; you want filters and joins. |
| **MCP resource** | Cross-agent shared context. |

Be explicit about staleness: how fresh must each source be? Caching strategy? These are design choices rather than backed-vs-alternative — but if a source needs new infrastructure, treat that infra as its own backed/alternative decision.

### 7. Flexibility vs determinism — where on the spectrum?

- **Backed:** a **workflow orchestrator with LLM steps inside** — deterministic skeleton, auditable, agent steps where the conversation demands it (`maf-csharp-implementation` orchestrator reference).
- A single free-form agent loop, or a pure deterministic pipeline, are the ends of the spectrum — treat a strong pull to either end as an alternative and grill it.

Ask: *Which steps must be the same every time?* Those are workflow. *Which steps depend on the conversation?* Those stay in the agent.

### 8. Guardrails — what must never happen?

Defer to `agent-guardrails-safety` for implementation. Capture the **policy** decisions here:
- PII handling (detect, redact, block, log)
- Prompt-injection posture
- Content-filter level
- Tool-call allow/deny rules
- Audit-log retention and access

### 9. Identity & secrets

- **Backed:** user-assigned managed identity, Key Vault references, OBO where the agent acts as the user, workload identity federation for CI/CD (`agent-secrets-identity`, `azure-rbac`*, `entra-app-registration`*).
- Service principals with stored secrets, or secrets in app settings, are alternatives → run the protocol.

### 10. Deploy lifecycle

- **Backed:** `azure-validate`* → `azure-deploy`*, with the build/deploy pipeline on **Azure DevOps** (`azure-devops-pipelines-for-agents`).
- GitHub Actions or manual deploy are alternatives → run the protocol.

### 11. Model deployment — what model will the agent use?

- **Backed:** **Azure AI Foundry** — an `AIServices`-kind account with a Foundry project and a model deployment (`foundry-model-deployment`). Supports any model in the catalog (OpenAI GPT, Microsoft Phi, Meta Llama, and others) through a single inference endpoint. Default model: `gpt-4o`.
- **Alternative 1: Use an existing Foundry resource.** If a Foundry account and deployment already exist, record the inference endpoint (`https://<account>.services.ai.azure.com/models`) and deployment name — no Bicep needed. Earn it by providing these values now.
- **Alternative 2: `OpenAI`-kind AOAI resource.** Loses access to non-OpenAI catalog models and Foundry platform tools → run the protocol.

**Sub-decision — model choice.** Any catalog model is valid on the backed path. Resolve with:
- What modalities does the agent need (text, vision, code, tool use)?
- Are there cost or latency constraints that rule out frontier models?
- Is there a data-residency or approval requirement for the model tier?

The default recommendation is `gpt-4o` (`GlobalStandard`, 10k TPM) — capable, tool-capable, widely available. Non-OpenAI models (e.g. `Phi-4` from Microsoft) require no change to the agent code; only the Bicep parameters differ.

**Order:** Resolve after **#3 Hosting** (sets the region; use the same region to minimise latency and cross-region egress) and before **#9 Identity** (the UAMI needs `Cognitive Services User` on the Foundry account).

**Capture:**
- Inference endpoint → `AzureAIFoundry__Endpoint`
- Deployment name → `AzureAIFoundry__DeploymentName`

## Producing the artifact

Two paths — **ask the user which they prefer**:

1. **The user already has a documentation skill / convention.** Use it. Pass it this skill's decision set as input.
2. **No existing convention.** Suggest one of:
   - **arc42** (section 9 — "Architecture Decisions"), one ADR per decision above.
   - **MADR** (Markdown Any Decision Record) — `docs/adr/0001-trigger-model.md`, one file per decision.
   - **Single `decisions.md`** — simplest; one page, each decision a heading.

Whichever path, every decision must record: **chosen option**, **backed or alternative** (and if alternative, **why the backed option was rejected**), **rationale**, **revisit trigger**.

## Hand-off

When the decisions document is complete and the user confirms shared understanding, the `agent-architect` agent hands off to `agent-builder`. Do **not** start coding in this skill. Decisions first.
