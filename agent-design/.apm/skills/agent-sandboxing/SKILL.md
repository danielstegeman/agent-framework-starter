---
name: agent-sandboxing
description: Decide whether and how to safely execute model-generated code or shell commands from a code-first AI agent — when a sandbox is required at all, which isolated runtime to use (Azure Container Apps dynamic sessions custom container as the default, plus built-in code interpreter, e2b, Daytona, ACI), the execution model (thin sandbox vs agent-in-sandbox), per-environment images, network egress policy, credential isolation, resource/time/scaling limits, audit logging, and the local development runtime. Use this skill when an agent will run LLM-authored code, when the user asks "do I need a sandbox", "how do I sandbox my agent", "is it safe to let the agent run code", "which sandbox runtime should I use", "how do I isolate code execution", or when the architecture interview reaches the code-execution branch. Security-first — treat untrusted, model-generated code as hostile by default.
---

# Agent Sandboxing

Decide how a code-first agent executes **model-generated code or shell commands** without compromising the host, the network, or credentials. The output is a set of decisions captured in the architecture decisions document — not code.

This skill is a branch of `agent-architecture-decisions`. Return its output to that decision set.

## When is a sandbox required?

**A sandbox is required whenever the agent executes any form of agent-generated code or command** — Python/JS snippets, shell/bash, a `run_command` tool, build/test loops, or LLM-authored SQL run as a script. If an LLM can decide *what* runs, the runtime must be isolated. Not every agent needs this.

| The agent... | Sandbox? |
|---|---|
| Executes model-generated code or shell commands (code interpreter, data analysis, coding agent, build/test). | **Required.** |
| Reads/writes a workspace filesystem or runs `git` on LLM-chosen paths. | **Required.** |
| Only calls typed, fixed-contract tools (REST/SDK calls with validated args). | Not required — the tool contract is the boundary. |
| Only generates code as text and returns it without running it. | Not required — nothing executes. |
| Only retrieves context (RAG, structured queries) and produces text. | Not required. |

If none of the "Required" rows apply, record *"no code execution — sandbox not needed"* in the decision set and stop here. Otherwise continue.

## Threat model

Treat any code or command produced by an LLM as **untrusted and potentially hostile**, even when the prompt is benign. Prompt injection, hallucinated destructive commands, and data-exfiltration attempts are all in scope. The single highest-risk mistake is letting untrusted code reach the network or real credentials. Design from "deny everything," then open the minimum the agent needs.

## Decision 1 — execute at all?

Before choosing a runtime, challenge the need to execute (see the table above):

- **Can the task be done with structured tools instead?** A typed tool with a fixed contract is safer and more predictable than arbitrary code. Prefer it when it fits.
- **Does the user need to see/run real output, or just a generated artifact?** If you only need the code text, don't execute it — return it.
- If execution is genuinely required (data analysis, code interpreter, build/test loops, coding agent), continue.

Record: *yes/no, and why execution is required.*

## Decision 2 — isolated runtime

Never execute in the agent's own process or host. Pick an isolated runtime:

| Runtime | When to pick | Notes |
|---|---|---|
| **Azure Container Apps dynamic sessions — custom container** *(default for the container-hosted starter)* | You run on Container Apps and need per-conversation sandboxes with your own toolchain (bash, git, compilers, language runtimes). | Hyper-V isolation per session; no egress by default; managed-identity-in-session off by default; sub-second allocation from a pre-warmed pool; scales to thousands of concurrent sessions. Bring your own image per coding environment. |
| **Azure Container Apps dynamic sessions — built-in code interpreter** | You only need Python execution and want zero image management. | Simplest option; `PythonLTS` image with common data libraries preinstalled. No custom bash/tooling; Python only. |
| **e2b** | Hosted code-interpreter sandboxes, fast cold start, per-session microVMs. | SaaS; data leaves your tenant unless self-hosted. Good DX for code-interpreter patterns. |
| **Daytona** | Self-hostable dev-environment sandboxes; you control the infra. | More ops; keeps execution in your tenant. |
| **Azure Container Instance (ACI)** | Per-run throwaway container in your subscription, no cluster. | Good isolation, pay-per-run, integrates with Azure networking + identity. |
| **gVisor / Firecracker microVM (self-managed)** | Maximum isolation, you operate the kernel boundary. | Highest ops burden; only if the above don't satisfy a hard requirement. |

**Default for an Azure-hosted agent: Container Apps dynamic sessions with a custom container.** It stays in your tenant, reuses Azure identity + networking, gives Hyper-V isolation per session, and lets each *coding environment* be a different image (the devcontainer story — see Decision 3a). Drop to the **built-in code interpreter** when Python-only execution is enough and you want no image to manage. Reach for **e2b** when you want a turnkey SaaS code-interpreter and accept the external data flow.

Implementation hand-off: session-pool IaC → `azure-container-apps-sessions-bicep`; C# sandbox tools + local runtime → `agent-sandbox-csharp`.

Record: *chosen runtime + why.*

## Decision 2a — execution model

Decide **where the agent's reasoning runs** relative to the sandbox:

| Model | Shape | When to pick |
|---|---|---|
| **Model A — thin sandbox** *(default)* | The agent (the "brain") stays on the host. `run_command` / `read_file` / `write_file` / `git` are host-side tools that proxy each operation into the session over HTTP. The session image runs a minimal executor. | Code interpreter, data analysis, running snippets, moderate file/command work. Keeps the model credentials, guardrails, and observability on the host. |
| **Model B — agent in the sandbox** *(upgrade)* | The host delegates a whole task to a coding sub-agent running *inside* the session, which has direct local filesystem/bash/git access and its own tight loop. | Autonomous multi-step coding / refactors / build-test loops where per-operation HTTP hops are too chatty. Requires brokering model access to the sandbox — never place a raw key inside it. |

Both run on the **same** session pool; the difference is what the image runs (a dumb executor vs. a sub-agent) and where the tools are defined. Start with Model A; move to Model B only when the inner loop justifies it.

Record: *chosen model + why; for Model B, how model access is brokered.*

## Decision 3 — network egress policy

## Decision 2b — per-environment images

Different coding tasks need different toolchains (a Python data environment vs. a Node web environment vs. a .NET build environment). With **custom container** session pools, each environment is simply a **different image** — so model the tool surface as one image per environment rather than one fat image with everything.

- Author each image from a **devcontainer** definition where possible, so local dev and the sandbox share one source of truth for the toolchain.
- Keep images **minimal and non-root**: only the runtimes/tools that environment needs.
- Select the pool/image by task type at runtime; don't let the model pick arbitrary tooling.

Record: *the set of environments and their images; how an environment is selected per request.*

## Decision 3 — network egress policy

The most important control. From most to least safe:

- **No egress (default).** The sandbox cannot reach the network at all. Choose this unless a concrete need exists. (Container Apps dynamic sessions deny egress by default.)
- **Allow-list egress.** Only named hosts/CIDRs (e.g. a package registry mirror, or the git remote a coding agent must reach). Everything else denied.
- **Open egress.** Avoid. Only with explicit sign-off and only for trusted-input scenarios — never for untrusted model-generated code.

A coding agent that must `git clone` / `git push` needs the git remote (and possibly a package registry) on the allow-list — *or*, preferably, have the host broker git operations through a narrow audited tool so the sandbox keeps no egress and no credentials.

Record: *egress mode + the exact allow-list if any + who approved open egress.*

## Decision 4 — credential isolation

Untrusted code must never see real secrets or a privileged identity.

- **No ambient credentials in the sandbox.** No managed identity token, no env-var secrets, no mounted Key Vault.
- If the executed code needs data, **pass scoped inputs in, collect outputs out** — don't hand it a credential to fetch its own data.
- If a downstream call is unavoidable, broker it **outside** the sandbox through a narrow, audited tool the host controls.
- Run as a **non-root, least-privilege** user inside the runtime.
- On Container Apps dynamic sessions, **managed-identity-in-session is off by default — leave it off.** Enabling it lets untrusted code mint Entra tokens for the pool's identity. The host holds the `Azure ContainerApps Session Executor` role and calls the pool *on behalf of* the conversation; the session itself stays credential-less.

Record: *what (if anything) the sandbox can authenticate as, and how data crosses the boundary.*

## Decision 5 — resource & time limits

Bound every execution so a runaway or malicious program can't exhaust resources:

- **Wall-clock timeout** per execution (kill on exceed).
- **CPU / memory caps.**
- **Disk quota** and an ephemeral, per-run filesystem that is destroyed afterward.
- **No persistence between runs** unless explicitly required and isolated per session.

For dynamic sessions, the **session identifier** scopes persistence and isolation: use the **conversation ID** as the identifier so follow-up turns reuse the same session (state persists within a conversation) while different conversations stay isolated. The identifier is sensitive — generate it so an end user can't forge another conversation's id.

Record: *the concrete limits.*

## Decision 5a — scaling & capacity

A per-conversation sandbox model scales with concurrent conversations, so size the pool deliberately:

- **Max concurrent sessions** — the ceiling on simultaneous sandboxes; cap it to bound cost and blast radius.
- **Ready (pre-warmed) sessions** — how many idle sandboxes are kept hot for sub-second allocation under load.
- **Cooldown / idle timeout** — how long an idle session lives before it's destroyed and its resources reclaimed.
- Ensure the **host** Container App has enough CPU/memory and the right concurrency-based scaling rule so it isn't the bottleneck in front of the pool.

Record: *max sessions, pre-warmed count, cooldown, and the host scaling rule.*

## Decision 6 — audit & observability

- Log every execution: the code/command, inputs, outputs, exit code, duration, and which agent run triggered it.
- Redact or hash anything sensitive in the logs per the observability PII policy.
- Retain audit logs long enough to investigate an incident; define who can read them.
- Emit a trace span per execution so it's visible alongside the agent run.
- Wire each sandbox tool call into the same tool-call audit trail as the rest of the agent's tools (see `agent-guardrails-safety`).

Record: *what's logged, retention, access.*

## Decision 7 — local development runtime

There is no local emulator for Container Apps dynamic sessions, so reproduce the contract locally behind an abstraction instead of taking a hard dependency on the cloud during development:

- Define an **`ISandbox`** abstraction (get-or-create session by conversation id, execute command, read/write/list files). The agent's tools depend only on this.
- **Local:** a small session pool that maps conversation id → a container running the **same image** as the cloud sandbox, with readiness waits and idle cooldown. Drive the Docker engine directly (e.g. via the Engine API) so the local manager honestly mirrors the cloud pool's allocate-on-demand / cooldown semantics.
- **Cloud:** the dynamic-sessions management API.
- Running the **same image** in both places means what you test locally is what runs Hyper-V-isolated in production.

Record: *the abstraction, the local runtime, and that both use the same image.*

## Output

Add to the architecture decisions document, under the sandbox decision:
- sandbox required? (yes/no + why — see "When is a sandbox required?")
- execute? (yes/no + why)
- runtime + rationale
- execution model (A thin sandbox / B agent-in-sandbox + why)
- per-environment images (set + selection)
- egress mode (+ allow-list, + approver if open)
- credential isolation approach
- resource/time limits + session identifier scheme
- scaling (max sessions, pre-warmed, cooldown, host scaling rule)
- audit/retention policy
- local development runtime
- revisit trigger

## Hand-off

Implementation of the chosen runtime belongs to the builder journey:
- Session-pool IaC (custom container, scaling, network, RBAC) → `azure-container-apps-sessions-bicep`.
- C# `ISandbox`, ACA + local implementations, and MAF sandbox tools → `agent-sandbox-csharp`.
- Identity boundaries (host holds the Session Executor role; session stays credential-less) → `agent-secrets-identity`.
- Audit wiring for sandbox tool calls → `agent-guardrails-safety`.

This skill decides; it does not build.
