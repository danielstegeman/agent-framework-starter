---
name: agent-architecture-decisions
description: Walk a developer through the architectural decisions required to build a code-first AI agent — trigger model, observability, hosting, sandboxing, tool surface including MCP, context and memory providers, orchestration/determinism, guardrails, identity, CI/CD, and model deployment — by discovering which skills are actually installed, comparing options and tradeoffs, and producing a short decisions summary for `agent-builder`. Use this skill at the start of any new agent project, when modernising an existing one, or whenever someone asks "how should I structure my agent", "what should I decide before I build an agent", "design my agent architecture", or "what are the trade-offs for X agent decision". Language-neutral — applies regardless of SDK or platform; keep security-first decisions explicit.
---

# Agent Architecture Decisions

A guided, adaptive interview that surfaces only the architectural decisions that matter for *this* agent, then records them with rationale. The output is **decisions with rationale**, not code.

Driven by the `agent-architect` agent. Hand off to implementation skills (e.g. `dotnet-agent-bootstrap`, `maf-csharp-implementation`) via `agent-builder`, only after the decisions summary is done.

## When to use

- Greenfield agent project — before any code is written.
- Existing agent project missing documented decisions.
- A new architectural concern arises (e.g. adding sandboxing, switching hosting target).

## Goal

A short written record of the decisions actually made — **chosen option**, **backed skill or custom alternative**, **one-line rationale**, **revisit-when trigger** — nothing more, nothing forced.

## Step 0 — Discover what's actually installed

Do this before asking anything. Don't rely on a hardcoded list of options — the workspace itself is the source of truth, and it changes as sub-packages are added or removed:

1. Read `apm.yml` (root and any sub-packages) to see which sub-packages are installed (`maf-core`, `agent-design`, `dotnet-implementation`, `azure-infrastructure`, `quality-safety`, or others).
2. List each installed sub-package's `.apm/skills/*/SKILL.md` and read its frontmatter `description` — that's the live capability map: what's actually backed, right now, in this workspace.
3. Note companion skills (ship with the Azure VS Code extensions, not `.apm/skills`) separately — backed *once installed*, not unavailable.
4. Build a short mental table: **capability → skill name**. Use it instead of a fixed option list for every recommendation below.

If nothing is installed for a capability the user needs, say so plainly and treat any option there as a custom alternative (protocol below) rather than pretending a default exists.

## Core principle — recommend only what's discovered, grill what isn't

Recommend the option backed by a skill found in step 0. Where a concern genuinely has more than one live, reasonable option (e.g. hosting, CI/CD runner, orchestration), present them neutrally — don't force a single default.

The user **may always propose an alternative**. Apply the protocol below only for unsupported, high-risk, or platform-leaving choices (leaving C#/MAF, leaving Azure entirely, skipping the sandbox for generated code, weakening identity/secrets). Ordinary choices among discovered options don't need grilling.

### Protocol for an unsupported or high-risk alternative

1. **Name the discovered option it replaces**, and research the alternative (web + a research subagent) if you don't already know it well — don't debate from memory.
2. **Grill**: what does leaving the discovered option cost in effort, operations, and support? What makes the discovered option unworkable here? Push back on vague answers.
3. **Record**: the alternative chosen, why the discovered option was rejected, residual risk, revisit trigger.

If the user can't articulate a concrete reason for a high-risk alternative, recommend the discovered option instead.

## The interview — adaptive, not a fixed checklist

Don't walk every branch below in every session. Start small, then only open a branch when the answers make it relevant.

**Opening questions (max 3 at a time):**
1. What triggers the agent — a person chatting, an event/webhook, a schedule, or a one-shot call?
2. What does it need to do — call your own code, call a shared/remote tool, execute model-generated code, or just answer from context?
3. Is this one agent, or does it coordinate several agents / a fixed multi-step process?

Resolve upstream decisions before downstream ones (language/SDK and hosting shape everything else; model deployment follows hosting; identity follows model deployment). Only then open the relevant branches:

### Language / SDK

Confirm C# + Microsoft Agent Framework if `dotnet-agent-bootstrap` / `maf-csharp-implementation` are installed, then move on without belaboring it. Any other language/SDK weakens every downstream skill in this starter — run the protocol and record which skills no longer apply.

### Trigger model

Streaming chat, event-driven (queue/webhook), scheduled, single-shot webhook, or CLI — pick from what the answer to opening question 1 implies. This shapes hosting; don't re-derive it later.

### Observability

Recommend OpenTelemetry as the backbone, exported to whatever's installed (e.g. App Insights via `appinsights-instrumentation`, local via `dotnet-aspire-apphost`). Capture: what's traced (runs, tool calls, prompt/response sizes, latency, errors), PII handling in logs, retention, and the alerts/SLOs that matter.

### Hosting

Only open this if the trigger model doesn't make it obvious. Compare whatever's discovered (e.g. self-host on Container Apps/App Service, Foundry Hosted Agents, Azure Functions, CLI/local) against operational standard, network needs, and sandbox/tool requirements — no single mandated default.

### Code-execution sandbox

Gate question: does the agent execute any model-generated code or command (code interpreter, `run_command`, build/test loop, coding agent, LLM-chosen filesystem/git)? Not needed if it only calls typed tools or returns code as text.

If yes → hand the whole branch to `agent-sandboxing` (runtime, execution model, egress, credential isolation, limits, audit) and record its output here. Open network egress from untrusted generated code is the highest-risk choice in the whole design — don't wave it through.

### Tool surface

In-process tools (owned by this agent), an MCP tool server (shared/remote/owned elsewhere — `maf-mcp-tools` if installed), a plain external HTTP API, or context-only (no tool, pre-fetched). For each real candidate tool, record name, input/output shape, side effect, idempotency, owner, and audit need.

### Context, memory, grounding

Prefer MAF sessions/context providers over inventing a memory layer. Pick from: pre-fetched prompt context, session/persisted memory, a context provider, tool-fetched at runtime, RAG, structured retrieval, or an MCP resource — whichever matches staleness and size needs. Be explicit about PII and who can read stored memory.

### Flexibility, determinism, orchestration

Single agent/loop unless the opening answers indicated multiple agents or a fixed multi-step process — then `maf-workflows-orchestration` (graph workflows, handoff/group-chat/concurrent/sequential/magentic) if installed, or an app-level CQRS orchestrator if the product already standardises on one. Ask: *which steps must be identical every time* (workflow) vs *which depend on conversation judgment* (agent).

### Guardrails

Policy only here — defer implementation to `agent-guardrails-safety`. Capture: PII handling, prompt-injection posture, content-filter level, tool-call allow/deny + approvals, audit-log retention.

### Identity & secrets

Recommend UAMI + Key Vault refs + workload identity federation (`agent-secrets-identity`, plus `azure-rbac` / `entra-app-registration` if installed). Stored secrets or long-lived service-principal credentials are alternatives — run the protocol.

### CI/CD

Same lifecycle shape regardless of runner: build/test → validate infra → deploy → smoke test. Azure DevOps (`azure-devops-pipelines-for-agents` if installed) vs GitHub Actions is a real choice, not a default — pick by repo governance. Manual/local deploy is fine for a prototype but needs a revisit trigger.

### Model deployment

Resolve after hosting (sets region), before identity (UAMI needs the right role). Recommend Azure AI Foundry project endpoint + model deployment (`foundry-model-deployment` if no deployment exists yet, otherwise just record the existing project endpoint and deployment name). Sub-decision: modalities needed, cost/latency constraints, data-residency/approval requirements. Capture `AZURE_AI_PROJECT_ENDPOINT` and `AZURE_AI_MODEL_DEPLOYMENT_NAME`.

## The output — one decisions summary

Always write a single lightweight decisions summary (e.g. `docs/decisions.md`) — no format negotiation, no ADR/arc42/MADR ceremony. One heading per decision *actually made* (skip branches that were never opened). Each entry: chosen option, the skill it maps to (or "custom alternative" + why the backed option was rejected), a one-sentence rationale, and a revisit trigger. This is the direct input `agent-builder` reads to start implementing.

## Hand-off

When the summary is complete and the user confirms shared understanding, `agent-architect` hands off to `agent-builder`. Don't start coding here — decisions first.
