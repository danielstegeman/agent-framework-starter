---
name: maf-memory-context
description: >
  Decision guidance and implementation pointers for Microsoft Agent Framework
  memory and context providers in C#/.NET: sessions for short-term conversation
  history, memory providers for long-term and cross-session recall, context
  providers for deterministic runtime context injection, and RAG or structured
  retrieval for large corpora. Use this skill when the user asks "add memory to
  my agent", "context provider", "conversation memory", "long-term memory",
  "remember across sessions", "FoundryMemoryProvider", "mem0", "Redis memory",
  "persist conversation state", "how do sessions differ from memory", "user
  preferences", "cross-session recall", "inject user profile", or any
  equivalent question about layered state in Microsoft Agent Framework agents.
---

# Microsoft Agent Framework — Memory & Context Providers

Use the smallest state mechanism that matches the product need. Start with MAF sessions for one conversation; add memory, context providers, or retrieval only when the agent needs state beyond the current thread.

This skill builds on the sessions guidance in `maf-csharp-implementation`. Do not hand-roll a `List<ChatMessage>` history; use the MAF session APIs and persistence patterns from the official docs.

## Layered state model

| Mechanism | Use for | Scope | Notes |
|---|---|---|---|
| **Sessions** | Conversation history within the current thread | Short-term, per conversation | MAF session APIs manage history and state. Persist serialized session state for web/event-driven hosts. |
| **Memory providers** | Facts, preferences, and remembered context across conversations | Long-term, per user/tenant/product policy | Examples include Foundry-managed memory (`FoundryMemoryProvider`), mem0, and Redis-backed memory. Check current docs for provider-specific APIs. |
| **Context providers** | Dynamic structured context that should always be present | Runtime injection, usually per request | Useful for policies, user profile, entitlement state, tenant settings, or snippets that should not be model-selected as a tool. |
| **RAG / structured retrieval** | Large corpora and semantic search | External knowledge store | Often exposed as a tool such as File Search or Azure AI Search; hand off to `maf-hosted-tools`. |

## Decision guidance

| Need | Use | Why |
|---|---|---|
| Recall within the current conversation | **Sessions** | Keeps turn history and conversation state without custom chat-history plumbing. |
| Remember user facts/preferences across conversations | **Memory provider** | Stores cross-session memory that can survive beyond one conversation. |
| Always-present dynamic context not chosen by the model | **Context provider** | Injects deterministic context at run time without making it a model-called tool. |
| Query a large document corpus semantically | **RAG / retrieval tool** | Keeps large knowledge outside the prompt and retrieves only relevant material. |

Do not use memory as a substitute for sessions. Do not use RAG as a dumping ground for every per-user preference. Keep each layer's retention, latency, privacy, and ownership model explicit.

## When to use sessions

Use [conversations](https://learn.microsoft.com/en-us/agent-framework/concepts/agents/conversations/) and the [multi-turn](https://learn.microsoft.com/en-us/agent-framework/get-started/multi-turn) guidance when the agent only needs to remember what happened in the active thread.

Good fits: chat or task flow state inside one conversation, follow-up questions that depend on earlier turns, short-lived workflow state, and web/bot/event-driven hosts that resume a known conversation id.

Persistence pattern:

- Key session state by user id + conversation id.
- Serialize the MAF session state rather than reconstructing message lists manually.
- Store it in Redis, Cosmos DB, SQL, blob storage, or another service based on latency, durability, and cost needs.
- Self-hosted apps own this persistence. Foundry Hosted Agents can persist `$HOME`/files automatically; use `foundry-hosted-agents` for that hosting model.

## When to use memory providers

Use [memory](https://learn.microsoft.com/en-us/agent-framework/get-started/memory) when the product explicitly needs recall beyond one conversation: user preferences, durable facts the user asked the agent to remember, cross-session personalization, or team/tenant facts that are safe to reuse under the same isolation boundary.

| Provider style | Good fit | Watch for |
|---|---|---|
| Foundry-managed memory, such as `FoundryMemoryProvider` | You already use Foundry and want a managed memory path | Provider capabilities, identity, project boundary, and retention controls. |
| mem0 | You want an external memory service with its own memory semantics | Data residency, auth model, operational ownership, and SDK maturity. |
| Redis-backed memory | Low-latency app-owned memory or cache-like recall | Eviction, durability, encryption, tenant isolation, and backups. |

Start without long-term memory unless cross-session recall is a real product requirement. Add explicit UX for what can be remembered, forgotten, exported, or corrected.

## When to use context providers

Use a context-provider pattern when the agent must receive dynamic structured context deterministically, without waiting for the model to choose a tool call.

Good fits: current user profile, tenant id, role, entitlement summary, locale, channel metadata, policies, feature flags, or small retrieved snippets that are always relevant to this request.

Rules:

- Keep injected context concise and structured.
- Avoid placing secrets in context unless the model must see them, which is rare.
- Prefer context providers over tools when the model should not decide whether the context is loaded.
- Prefer tools or RAG when the context is optional, large, or query-dependent.

## When to use RAG / structured retrieval

Use retrieval when the knowledge corpus is too large, fast-changing, or semantically searched. In MAF designs this is commonly surfaced as a tool, especially for File Search, Azure AI Search, or Memory Search capabilities.

Good fits: product documentation, policies, tickets, knowledge bases, code repositories, and queries that need citations, document ids, freshness, or corpus-level access control.

Hand off retrieval-as-a-tool design to `maf-hosted-tools` rather than embedding large documents in memory or session state.

## Safety and governance

Memory and context can contain user data. Treat retention, PII handling, per-user/tenant isolation, store identity, RBAC, and audit logging as explicit product and architecture decisions. The memory store's managed identity, service principal, or user identity matters because it defines who can read, write, correct, and delete remembered data.

For identity and roles, hand off to `agent-secrets-identity` and `azure-rbac`.

## Hand-off

- Sessions basics, C# implementation shape, and DI wiring -> `maf-csharp-implementation`.
- Retrieval-as-a-tool (File Search / Azure AI Search / Memory Search) -> `maf-hosted-tools`.
- Automatic session persistence on managed hosting -> `foundry-hosted-agents`.
- Identity and roles for memory stores -> `agent-secrets-identity` and `azure-rbac`.

## What this skill does NOT cover

- Basic C# agent setup, tool registration, or session wiring -> `maf-csharp-implementation`.
- Hosted retrieval tool setup -> `maf-hosted-tools`.
- Managed Foundry hosting behavior -> `foundry-hosted-agents`.
- Secrets, managed identity, and RBAC implementation -> `agent-secrets-identity` + `azure-rbac`.
- Evaluation strategy for memory quality or personalization -> `agent-evaluation-strategy`.

## Official Documentation

- [Memory](https://learn.microsoft.com/en-us/agent-framework/get-started/memory)
- [Conversations & Memory](https://learn.microsoft.com/en-us/agent-framework/concepts/agents/conversations/)
- [Multi-turn conversations](https://learn.microsoft.com/en-us/agent-framework/get-started/multi-turn)
- [Microsoft Foundry model provider](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/microsoft-foundry)
