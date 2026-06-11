---
name: agent-guardrails-safety
description: Implement input / output / tool-call guardrails for a code-first C# agent — PII detection and redaction, prompt-injection / jailbreak detection (Azure AI Content Safety prompt-shield), output content filtering, tool-call allow/deny lists, and audit-log retention — using IChatClient delegating middleware and AIFunction wrappers. Use this skill when the user asks "add guardrails to my agent", "implement prompt injection protection", "PII redaction for my MAF agent", "content safety for agent inputs/outputs", "audit log for tool calls", or anything about agent safety / responsible-AI controls in C#.
---

# Agent Guardrails & Safety

Implement layered guardrails for a code-first agent. Reference: [references/guardrail-middleware.cs](references/guardrail-middleware.cs).

## The three layers

| Layer | Surface | What it does |
|---|---|---|
| **Input** | `DelegatingChatClient` before `GetResponseAsync` | PII redaction; prompt-injection / jailbreak detection on user messages. |
| **Output** | `DelegatingChatClient` after `GetResponseAsync` | Content safety check on the model's reply; redact or block. |
| **Tool-call** | `AIFunction` wrapper at registration time | Allow/deny list per call site; audit each invocation with args + result. |

All three should run on every prod agent. Skipping any is a deliberate, documented decision.

## Implementation pattern

Compose middleware via `ChatClientBuilder`:

```csharp
var chatClient = new ChatClientBuilder(rawOpenAiChatClient)
    .Use(next => new InputRedactionMiddleware(next, pii))
    .Use(next => new PromptInjectionGuardMiddleware(next, contentSafety))
    .Use(next => new OutputContentFilterMiddleware(next, contentSafety))
    .Build();

var agent = new ChatClientAgent(chatClient, options);
```

Wrap tools at registration:

```csharp
var aiTools = toolMethods
    .Select(m => AIFunctionFactory.Create(m, instance))
    .Select(f => new AuditedAIFunction(f, policy))   // <- wrapper
    .Cast<AITool>()
    .ToList();
```

## Concrete services to plug in

| Concern | Default | Alternatives |
|---|---|---|
| PII detection | **Microsoft Presidio** (open-source, runnable as a sidecar) | Azure AI Language PII detection (managed). |
| Prompt injection | **Azure AI Content Safety — Prompt Shields** | Custom regex (last-resort only). |
| Output content filter | **Azure AI Content Safety — text analyse** | Built-in Azure OpenAI content filter (set on the deployment). |
| Audit log sink | **Azure Monitor (App Insights)** as custom events | Blob storage with append-only policy for regulated workloads. |

The built-in Azure OpenAI deployment content filter handles most output cases. Layer Content Safety on top only if you need finer-grained categories or custom block-list rules.

## PII strategy — decide once

For each PII category (name, email, phone, financial, gov-id):
- **Block**: refuse the request.
- **Redact**: replace with `<TOKEN>` and continue.
- **Allow + log**: pass through but record.

Whichever you pick, do it at the **input** layer. The model should never see raw PII unless policy permits.

## Prompt injection — what to actually check

- User messages from external sources (issue bodies, PR descriptions, emails) are **untrusted**. System / developer messages are trusted.
- Run Prompt Shields on every untrusted message. Treat a positive shield result as a 4xx error returned to the caller — don't proceed silently.
- For tool results that contain external content (web pages, third-party API responses), shield those too **before** they re-enter the model context.

## Output filtering

Most outputs are fine. The cases that matter:
- Hallucinated PII (model emits an email address that wasn't asked for).
- Responses to jailbroken prompts that slipped past the input layer.
- Tool outputs leaking secrets (a misconfigured tool returning a connection string).

Apply Content Safety on the assistant message and on each tool-result chunk that re-enters the loop.

## Tool-call audit & policy

`AuditedAIFunction` wrapper responsibilities:
1. Start an `Activity` span: `tool.name`, `tool.success`, `tool.duration_ms`, `tool.error_type` (no args/result in prod by default).
2. Enforce policy: deny if `policy.IsBlocked(toolName, principal, argsHash)`.
3. Log to audit sink: caller, time, tool, success, latency, **argsHash** (not args) and **resultHash** (not result) in prod; full args/result in dev.

Policy examples:
- `WriteWorkItemComment` requires the caller's identity be present on the work item.
- `DeleteBranch` is blocked outright in agents that aren't given that role.
- Rate-limit destructive tools per session.

## Audit retention

- 30 days minimum, 1 year typical, 7 years if regulated. Decide explicitly per agent.
- Audit events are **append-only**. If you can edit them, they're not audit events.
- Include enough to reconstruct an incident: conversation id, message id, model deployment, prompt hash, tool calls, final response hash.

## Configuration shape

Per-environment config keys (bound with `IOptions<T>`):

```json
{
  "Guardrails": {
    "Pii":          { "Mode": "Redact", "Categories": ["Email", "Phone"] },
    "PromptShield": { "Enabled": true,  "Endpoint": "https://...", "BlockThreshold": "Medium" },
    "OutputFilter": { "Enabled": true,  "Categories": ["Hate", "Sexual", "Violence", "SelfHarm"] },
    "Tools":        { "DenyList": ["DeleteBranch"], "RateLimits": { "WriteWorkItemComment": "10/min" } },
    "Audit":        { "Sink": "AppInsights", "IncludePayloadsInEnvironments": ["Development"] }
  }
}
```

## Hand-off

- Where middleware fits in the agent pipeline -> `maf-csharp-implementation`.
- Provisioning Azure AI Content Safety -> `azure-prepare`.
- Audit sink wiring (App Insights connection) -> `appinsights-instrumentation`.
- AI gateway-level rate limits and jailbreak detection -> `azure-aigateway`.
- Identity that the audit log records -> `agent-secrets-identity`.
