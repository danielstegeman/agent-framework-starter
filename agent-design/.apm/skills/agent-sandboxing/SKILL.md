---
name: agent-sandboxing
description: Decide how to safely execute model-generated code or shell commands from a code-first AI agent — whether to execute at all, which isolated runtime to use (e2b, Daytona, Azure Container Instance, Container Apps job-per-run), the network egress policy, credential isolation, resource and time limits, and audit logging. Use this skill when an agent will run LLM-authored code, when the user asks "how do I sandbox my agent", "is it safe to let the agent run code", "which sandbox runtime should I use", "how do I isolate code execution", or when the architecture interview reaches the code-execution branch. Security-first — treat untrusted, model-generated code as hostile by default.
---

# Agent Sandboxing

Decide how a code-first agent executes **model-generated code or shell commands** without compromising the host, the network, or credentials. The output is a set of decisions captured in the architecture decisions document — not code.

This skill is a branch of `agent-architecture-decisions`. Return its output to that decision set.

## Threat model — start here

Treat any code or command produced by an LLM as **untrusted and potentially hostile**, even when the prompt is benign. Prompt injection, hallucinated destructive commands, and data-exfiltration attempts are all in scope. The single highest-risk mistake is letting untrusted code reach the network or real credentials. Design from "deny everything," then open the minimum the agent needs.

## Decision 1 — execute at all?

Before choosing a runtime, challenge the need to execute:

- **Can the task be done with structured tools instead?** A typed tool with a fixed contract is safer and more predictable than arbitrary code. Prefer it when it fits.
- **Does the user need to see/run real output, or just a generated artifact?** If you only need the code text, don't execute it — return it.
- If execution is genuinely required (data analysis, code interpreter, build/test loops), continue.

Record: *yes/no, and why execution is required.*

## Decision 2 — isolated runtime

Never execute in the agent's own process or host. Pick an isolated runtime:

| Runtime | When to pick | Notes |
|---|---|---|
| **e2b** | Hosted code-interpreter sandboxes, fast cold start, per-session microVMs. | SaaS; data leaves your tenant unless self-hosted. Good DX for code-interpreter patterns. |
| **Daytona** | Self-hostable dev-environment sandboxes; you control the infra. | More ops; keeps execution in your tenant. |
| **Azure Container Instance (ACI)** | Per-run throwaway container in your subscription, no cluster. | Good isolation, pay-per-run, integrates with Azure networking + identity. |
| **Container Apps job-per-run** | You already run on Container Apps; one job execution per agent run. | Reuses the backed hosting stack; scales to zero; bounded lifetime. |
| **gVisor / Firecracker microVM (self-managed)** | Maximum isolation, you operate the kernel boundary. | Highest ops burden; only if the above don't satisfy a hard requirement. |

Default for an Azure-hosted agent: **Container Apps job-per-run** or **ACI** (stays in tenant, reuses Azure identity + networking). Reach for **e2b** when you want a turnkey code-interpreter and accept SaaS data flow.

Record: *chosen runtime + why.*

## Decision 3 — network egress policy

The most important control. From most to least safe:

- **No egress (default).** The sandbox cannot reach the network at all. Choose this unless a concrete need exists.
- **Allow-list egress.** Only named hosts/CIDRs (e.g. a package registry mirror). Everything else denied.
- **Open egress.** Avoid. Only with explicit sign-off and only for trusted-input scenarios — never for untrusted model-generated code.

Record: *egress mode + the exact allow-list if any + who approved open egress.*

## Decision 4 — credential isolation

Untrusted code must never see real secrets or a privileged identity.

- **No ambient credentials in the sandbox.** No managed identity token, no env-var secrets, no mounted Key Vault.
- If the executed code needs data, **pass scoped inputs in, collect outputs out** — don't hand it a credential to fetch its own data.
- If a downstream call is unavoidable, broker it **outside** the sandbox through a narrow, audited tool the host controls.
- Run as a **non-root, least-privilege** user inside the runtime.

Record: *what (if anything) the sandbox can authenticate as, and how data crosses the boundary.*

## Decision 5 — resource & time limits

Bound every execution so a runaway or malicious program can't exhaust resources:

- **Wall-clock timeout** per execution (kill on exceed).
- **CPU / memory caps.**
- **Disk quota** and an ephemeral, per-run filesystem that is destroyed afterward.
- **No persistence between runs** unless explicitly required and isolated per session.

Record: *the concrete limits.*

## Decision 6 — audit & observability

- Log every execution: the code/command, inputs, outputs, exit code, duration, and which agent run triggered it.
- Redact or hash anything sensitive in the logs per the observability PII policy.
- Retain audit logs long enough to investigate an incident; define who can read them.
- Emit a trace span per execution so it's visible alongside the agent run.

Record: *what's logged, retention, access.*

## Output

Add to the architecture decisions document, under the sandbox decision:
- execute? (yes/no + why)
- runtime + rationale
- egress mode (+ allow-list, + approver if open)
- credential isolation approach
- resource/time limits
- audit/retention policy
- revisit trigger

## Hand-off

Implementation of the chosen runtime belongs to the `agent-builder` journey (e.g. a Container Apps job in `azure-container-apps-bicep`, identity boundaries in `agent-secrets-identity`, audit wiring in `agent-guardrails-safety`). This skill decides; it does not build.
