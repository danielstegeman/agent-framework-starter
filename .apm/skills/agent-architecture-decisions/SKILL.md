---
name: agent-architecture-decisions
description: Walk a developer through the architectural decisions required to build a code-first AI agent — trigger model, observability, hosting & sandboxing, tool surface, context sources, and the flexibility-vs-determinism balance — and produce an ADR-style artifact capturing each choice with rationale. Use this skill at the start of any new agent project, when modernising an existing one, or whenever someone asks "how should I structure my agent", "what should I decide before I build an agent", "design my agent architecture", "what are the trade-offs for X agent decision", or mentions ADRs, arc42, or architecture decisions for AI agents. Language-neutral — applies regardless of SDK or platform.
---

# Agent Architecture Decisions

A guided interview that surfaces the architectural decisions every code-first agent project should make explicitly, then captures them in a documented form. The output is **decisions with rationale**, not code. Hand off to implementation skills (e.g. `maf-csharp-implementation`, `dotnet-agent-bootstrap`) after the user is satisfied.

## When to use

- Greenfield agent project — before any code is written.
- Existing agent project missing documented decisions.
- A new architectural concern arises (e.g. adding sandboxing, switching hosting target).

## Goal

Produce a written record of the following decisions, each with: chosen option, alternatives considered, rationale, and a "revisit when" trigger.

## The decision set

Walk through these in order. Ask one cluster at a time, summarise the user's answer, then move on. Don't lecture — surface the trade-offs and let the user choose.

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

Cover:
- **Trace backbone**: OpenTelemetry is the default. Confirm.
- **Export target**: Application Insights / Azure Monitor (default for Azure), OTLP to Aspire dashboard (local), Jaeger / Grafana Tempo (self-hosted), Datadog / Honeycomb (SaaS).
- **What you trace**: agent runs, tool calls (with args in dev, hashed in prod), prompt + response sizes, token counts, latencies, errors.
- **Log retention & PII**: how long, redacted or raw, who can read.
- **Dashboards & alerts**: SLOs (p95 latency, success rate, $/run).

Hand off to `appinsights-instrumentation` skill if the user picks App Insights and wants depth.

### 3. Hosting model — where does the agent run?

| Option | When to pick |
|---|---|
| **Azure Container Apps** | Default. Containerised, autoscaling to zero, KEDA-aware, supports HTTP + jobs. |
| **App Service** | If team already runs App Service and doesn't want containers. |
| **Azure Functions** | True event-driven, per-trigger scaling, short-lived runs only. |
| **AKS** | Existing K8s investment, need fine-grained networking, or sidecars (e.g. Dapr). |
| **Sandbox / isolated compute** | The agent executes untrusted code or LLM-authored shell commands. See sub-question below. |
| **Edge / desktop** | The agent must run offline or on user hardware. |

**Sandboxing & filesystem access** — separate yes/no:
- Will the agent execute LLM-generated code? -> require an isolated runtime (Container Apps job per run, Azure Container Instance, or hardened sandbox like e2b / Daytona).
- Does the agent need to read/write a workspace? -> mount what, with what identity, retain how long, isolated per-session?
- Network egress: open, allow-list, or none?

### 4. Tool surface — what can the agent do?

For each capability the agent needs:
- **In-process tool** (C# method with `[Description]`): default for things the agent owns.
- **MCP server**: when the tool is shared across multiple agents or owned by another team.
- **External API via HTTP tool**: when an existing API is the source of truth.
- **No tool — context only**: pre-fetch and put in the prompt; cheaper and more predictable.

List the candidate tools. For each, record: name, input/output shape, side-effect (read/write/external-call), idempotency, who owns it.

### 5. Context sources — where does grounding come from?

| Source | When |
|---|---|
| **Pre-fetched context in the prompt** | Stable, small, per-session — cheapest and most deterministic. |
| **Tool-fetched at runtime** | Dynamic, depends on the conversation, fits the tool model. |
| **RAG (vector search)** | Large corpus, semantic queries, no exact schema. |
| **Structured retrieval (SQL / Graph / API)** | Source of truth has a schema; you want filters and joins. |
| **MCP resource** | Cross-agent shared context. |

Be explicit about staleness: how fresh must each source be? Caching strategy?

### 6. Flexibility vs determinism — where on the spectrum?

| Pattern | Trade-off |
|---|---|
| **Single free-form agent loop** | Maximum flexibility; minimum predictability. |
| **Agent-as-tool / handoff** | One agent calls another. More structure, still LLM-driven. |
| **Workflow orchestrator (CQRS, DAG, state machine)** | Deterministic skeleton with LLM steps inside. Auditable. |
| **Pure pipeline with LLM transforms** | Most deterministic; least adaptive. |

Ask: *Which steps must be the same every time?* Those are workflow. *Which steps depend on the conversation?* Those stay in the agent. The answer almost always lands on a workflow with one or more agent steps inside.

### 7. Guardrails — what must never happen?

Defer to `agent-guardrails-safety` for the implementation, but capture the policy decisions here:
- PII handling (detect, redact, block, log)
- Prompt injection posture
- Content filter level
- Tool-call allow/deny rules
- Audit-log retention and access

### 8. Identity & secrets

Defer to `agent-secrets-identity`. Capture here:
- Run-time identity (system-assigned MI, user-assigned MI, service principal, OBO).
- Secret storage (Key Vault refs, app settings, in-pipeline only).
- Federation for CI/CD (workload identity federation).

## Producing the artifact

Two paths — **ask the user which they prefer**:

1. **The user already has a documentation skill.** Use it. Pass them this skill's decision set as input.
2. **No existing documentation convention.** Suggest one of:
   - **arc42** (lightweight, section 9 — "Architecture Decisions"). One ADR per decision above.
   - **MADR** (Markdown Any Decision Record) — `docs/adr/0001-trigger-model.md`, one file per decision.
   - **Single decisions.md** — simplest; one page with each decision as a heading.

Whichever path, the artifact must include for each decision: **chosen option**, **alternatives considered**, **rationale**, **revisit trigger**.

## Hand-off

When decisions are recorded, hand off:
- *Greenfield* -> `dotnet-agent-bootstrap` (if C# / MAF) to scaffold the solution.
- *Existing project* -> `maf-csharp-implementation` for refactor guidance.
- *Infra-only decisions* -> `agent-infrastructure-overview`.

Do **not** start coding in this skill. Decisions first.
