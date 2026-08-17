---
name: agent-guardrails-safety
description: Implement security-first input, output, and tool-call guardrails for code-first C# agents using current Microsoft Agent Framework middleware, Azure AI Content Safety Prompt Shields, PII redaction, native tool approval and human-in-the-loop workflows, function-calling policy middleware, and audited AIFunction wrappers. Use this skill when the user asks "add guardrails to my agent", "implement prompt injection protection", "PII redaction for my MAF agent", "content safety for agent inputs/outputs", "tool approval", "human approval for tools", "audit log for tool calls", or anything about agent safety / responsible-AI controls in C#.
---

# Agent Guardrails & Safety

Implement layered guardrails for a code-first agent. Keep the detailed API mechanics link-first with the official [Agent Framework middleware](https://learn.microsoft.com/en-us/agent-framework/concepts/agents/middleware/) and [tool approval](https://learn.microsoft.com/en-us/agent-framework/agents/tools/tool-approval) docs; use the local reference only as small glue: [references/guardrail-middleware.cs](references/guardrail-middleware.cs).

## The three layers

| Layer | Surface | What it does |
|---|---|---|
| **Input** | Agent-run middleware and/or `IChatClient` middleware | PII redaction; prompt-injection / jailbreak detection on user and external-content messages. |
| **Output** | Agent-run middleware and/or `IChatClient` middleware | Content safety check on assistant messages and tool results before they re-enter context. |
| **Tool-call** | Native tool approval, function-calling middleware, and optional `AIFunction` wrappers | Human approval for sensitive tools; allow/deny policy; audit each invocation with hashed args/results. |

All three should run on every prod agent. Skipping any is a deliberate, documented decision.

## Current MAF middleware pattern

For `IChatClient` middleware, compose with `.AsBuilder().Use(...)` before the client is passed to a `ChatClientAgent`, or use the `clientFactory` hook when constructing a Foundry `AsAIAgent`:

```csharp
var guardedClient = rawChatClient
    .AsBuilder()
    .Use(getResponseFunc: InputGuardrailAsync, getStreamingResponseFunc: null)
    .Use(getResponseFunc: OutputGuardrailAsync, getStreamingResponseFunc: null)
    .Build();

var agent = aiProjectClient.AsAIAgent(
    model: deploymentName,
    instructions: instructions,
    clientFactory: chatClient => chatClient
        .AsBuilder()
        .Use(getResponseFunc: InputGuardrailAsync, getStreamingResponseFunc: null)
        .Use(getResponseFunc: OutputGuardrailAsync, getStreamingResponseFunc: null)
        .Build());
```

For agent-wide and tool-call controls, use the agent builder middleware chain:

```csharp
var guardedAgent = agent
    .AsBuilder()
    .Use(runFunc: AgentRunGuardrailAsync, runStreamingFunc: AgentRunStreamingGuardrailAsync)
    .Use(FunctionPolicyMiddlewareAsync)
    .Build();
```

Provide streaming middleware for streaming agents; using only non-streaming middleware can force streaming calls through a non-streaming path.

## Tool-call gating: prefer native approval for sensitive actions

Use MAF's native function approval flow for tools that need human consent before execution:

```csharp
AIFunction raw = AIFunctionFactory.Create(DeleteBranch);
AIFunction requiresApproval = new ApprovalRequiredAIFunction(raw);
```

After each agent run, inspect `ToolApprovalRequestContent`, show the tool name/arguments to an authorized reviewer, and continue the same session with `request.CreateResponse(true)` or `request.CreateResponse(false)`. This is the first-choice pattern for destructive or externally visible actions.

For broader business approvals that are not tied to one function call, model them as workflow request/response steps with [human-in-the-loop workflows](https://learn.microsoft.com/en-us/agent-framework/workflows/human-in-the-loop).

Keep the existing `AuditedAIFunction` / function-calling middleware pattern for non-interactive policy:
1. Start an `Activity` span: `tool.name`, `tool.success`, `tool.duration_ms`, `tool.error_type` (no args/result in prod by default).
2. Enforce policy: deny if `policy.IsBlocked(toolName, principal, argsHash)`.
3. Log to audit sink: caller, time, tool, success, latency, **argsHash** (not args) and **resultHash** (not result) in prod; full args/result only in approved dev environments.

Policy examples:
- `WriteWorkItemComment` requires the caller's identity be present on the work item.
- `DeleteBranch` requires native approval and is blocked outright in agents that are not assigned that role.
- Rate-limit destructive tools per session.

**Sandbox tools count as tool calls.** When the agent executes model-generated code in a sandbox, `run_command` / `read_file` / `write_file` / `git` tools land on this same approval and audit trail. Treat command execution as destructive by default.

## Concrete services to plug in

| Concern | Default | Alternatives |
|---|---|---|
| PII detection | **Presidio** (open-source, runnable as a sidecar) | Azure AI Language PII detection (managed). |
| Prompt injection | **Azure AI Content Safety — Prompt Shields** | Custom classifier; regex only as a last-resort coarse block. |
| Output content filter | **Azure AI Content Safety — text analysis** | Built-in Azure OpenAI content filter on the deployment. |
| Tool approval | **MAF ApprovalRequiredAIFunction + HITL session continuation** | Workflow request/response for multi-step approvals. |
| Audit log sink | **Azure Monitor (Application Insights)** custom events | Blob storage with append-only policy for regulated workloads. |

The built-in Azure OpenAI deployment content filter handles many output cases. Layer Content Safety on top when you need finer-grained categories, custom block-list rules, or a consistent safety API across providers.

## PII strategy — decide once

For each PII category (name, email, phone, financial, gov-id):
- **Block**: refuse the request.
- **Redact**: replace with `<TOKEN>` and continue.
- **Allow + log**: pass through but record.

Whichever you pick, do it at the **input** layer. The model should never see raw PII unless policy explicitly permits it.

## Prompt injection — what to actually check

- User messages from external sources (issue bodies, PR descriptions, emails, websites) are **untrusted**. System / developer messages are trusted.
- Run Prompt Shields on every untrusted message. Treat a positive shield result as a 4xx error returned to the caller — do not proceed silently.
- For tool results that contain external content, shield those too **before** they re-enter the model context.

## Output filtering

Most outputs are fine. The cases that matter:
- Hallucinated PII (model emits an email address that was not needed).
- Responses to jailbroken prompts that slipped past the input layer.
- Tool outputs leaking secrets (a misconfigured tool returning a connection string).

Apply Content Safety on assistant messages and on each tool-result chunk that re-enters the loop.

## Audit retention

- 30 days minimum, 1 year typical, 7 years if regulated. Decide explicitly per agent.
- Audit events are **append-only**. If you can edit them, they are not audit events.
- Include enough to reconstruct an incident: conversation id, message id, model deployment, prompt hash, approval request/decision, tool calls, final response hash.

## Configuration shape

Per-environment config keys (bound with `IOptions<T>`):

```json
{
  "Guardrails": {
    "Pii":          { "Mode": "Redact", "Categories": ["Email", "Phone"] },
    "PromptShield": { "Enabled": true,  "Endpoint": "<content-safety-endpoint>", "BlockThreshold": "Medium" },
    "OutputFilter": { "Enabled": true,  "Categories": ["Hate", "Sexual", "Violence", "SelfHarm"] },
    "Tools":        { "RequireApproval": ["DeleteBranch"], "DenyList": [], "RateLimits": { "WriteWorkItemComment": "10/min" } },
    "Audit":        { "Sink": "AppInsights", "IncludePayloadsInEnvironments": ["Development"] }
  }
}
```

## Hand-off

- Where middleware fits in the agent pipeline -> `maf-csharp-implementation`.
- Provisioning Azure AI Content Safety -> `azure-prepare`.
- Audit sink wiring (Application Insights connection) -> `appinsights-instrumentation`.
- AI gateway-level rate limits and jailbreak detection -> `azure-aigateway`.
- Identity that the audit log records -> `agent-secrets-identity`.

## Official Documentation

- [Agent Framework middleware](https://learn.microsoft.com/en-us/agent-framework/concepts/agents/middleware/)
- [Function tools with human-in-the-loop approvals](https://learn.microsoft.com/en-us/agent-framework/agents/tools/tool-approval)
- [Workflow human-in-the-loop request/response](https://learn.microsoft.com/en-us/agent-framework/workflows/human-in-the-loop)
- [Azure AI Content Safety overview](https://learn.microsoft.com/en-us/azure/ai-services/content-safety/overview)
- [Prompt Shields (jailbreak / prompt injection detection)](https://learn.microsoft.com/en-us/azure/ai-services/content-safety/concepts/jailbreak-detection)
- [Azure AI Language PII detection](https://learn.microsoft.com/en-us/azure/ai-services/language-service/personally-identifiable-information/overview)
- [Presidio (open-source PII detection)](https://presidio.dataprivacystack.org/)
