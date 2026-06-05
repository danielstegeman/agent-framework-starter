# Walkthrough: From zero to a deployed code-first agent

A worked example of using the `code-first-agent` orchestrator on a fresh project. Skill names in backticks are the leaves the orchestrator delegates to.

## Setup

Install the [APM CLI](https://microsoft.github.io/apm/), then in any project where you want the skills available:

```bash
apm install <owner>/code-first-agent-starter
```

Open a fresh Copilot Chat or Claude conversation in that workspace.

## The journey

**You:** *"Help me build a code-first agent that summarises Azure DevOps work items."*

The orchestrator picks up the request and asks two questions:
> Greenfield or expansion? Where would you like to start?

**You:** *"Greenfield. Walk me through all of it."*

### 1. Decisions (`agent-architecture-decisions`)

The orchestrator invokes the decisions skill. You're walked through:
- **Trigger model**: webhook from ADO on work-item update + a CLI for testing.
- **Observability**: OTel -> App Insights (prod), Aspire dashboard (local).
- **Hosting**: Azure Container Apps, public ingress.
- **Tools**: in-process tools class talking to ADO REST API.
- **Context sources**: tool-fetched only — no RAG.
- **Flexibility vs determinism**: single agent, one tool call per run, no orchestrator.
- **Guardrails**: PII redaction on the work-item body before it hits the model.
- **Identity**: UAMI for the workload, federated MI for the ADO pipeline.

Output: `docs/adr/0001-..0008-*.md` capturing each decision.

### 2. Scaffold (`dotnet-agent-bootstrap`)

```bash
cd ~/work
mkdir work-item-summariser && cd work-item-summariser
```

The bootstrap skill runs:
- `dotnet new sln -n WorkItemSummariser`
- Creates `Host`, `WorkItemSummariser`, `WorkItemSummariser.Tools.AzureDevOps`, `Tests`, `Evaluation.Tests`, `AppHost`.
- Adds packages (`Microsoft.Agents.AI`, `Azure.AI.OpenAI`, `Azure.Identity`, OTel + Azure Monitor exporter, etc.).
- Writes `Directory.Build.props`, `global.json`, `.editorconfig`, `.gitignore`.
- Copies the patterns from `references/builder-and-tools.cs`, `instructions-embedded.cs`, `otel-azuremonitor.cs` into the right projects, renaming types.
- Runs `dotnet build && dotnet test` — both green.
- `git init -b main && git add . && git commit -m "chore: scaffold via dotnet-agent-bootstrap"`.

You can now run:
```bash
dotnet run --project src/WorkItemSummariser.Host -- "Hello!"
```

### 3. Add the real tool (`maf-csharp-implementation`)

The implementation skill explains the tools-project pattern. You author `WorkItemTools.GetWorkItem(int id, ...)` with `[Description]` attributes. Register in `ServiceCollectionExtensions` of the tools project. Update the agent's `AddTools` reflection list automatically picks it up. Add `Instructions/Summariser.md` describing the persona.

```bash
dotnet test            # still green
dotnet run --project src/WorkItemSummariser.Host -- "Summarise work item 12345"
```

### 4. Local dev with Aspire (`dotnet-aspire-apphost`)

```bash
dotnet new aspire-apphost -n WorkItemSummariser.AppHost -o src/WorkItemSummariser.AppHost
dotnet sln add src/WorkItemSummariser.AppHost
dotnet add src/WorkItemSummariser.AppHost reference src/WorkItemSummariser.Host
```

`AppHost/Program.cs` declares the host project. F5 in VS now opens the Aspire dashboard with live OTel traces — including a span per tool call.

### 5. Infrastructure (`agent-infrastructure-overview` -> leaves)

Walk the 10-item checklist. Then:
- `azure-container-apps-bicep` produces `infra/container-apps.bicep` + `infra/rbac.bicep`.
- `azure-devops-pipelines-for-agents` produces `azure-pipelines.yml`.
- `agent-secrets-identity` makes sure the UAMI exists, the federated service connection is wired, and the App Insights connection string is in Key Vault.

Create the ADO service connection (workload identity federation), the variable group `agent-dev`, and push:

```bash
git remote add origin <ado-url>
git push -u origin main
```

The pipeline builds, pushes the image, and deploys. The agent is live.

### 6. Evals (`agent-evaluation-strategy`)

You add three case files under `tests/WorkItemSummariser.Evaluation.Tests/Datasets/summarise-basic/`:
- `case-001.json` — typical work item.
- `case-002.json` — work item with code-snippet noise (should be ignored).
- `case-003.json` — work item with PII in the description (should be redacted in the output).

`EvalFixture.cs` wires `RelevanceEvaluator` + `CoherenceEvaluator` + a custom `MentionsWorkItemIdEvaluator`. Pipeline gains a `Eval` stage that runs the smoke subset on PR.

### 7. Guardrails (`agent-guardrails-safety`)

You add `InputRedactionMiddleware` (Microsoft Presidio sidecar in ACA), `PromptInjectionGuardMiddleware` (Azure AI Content Safety Prompt Shields), and an `AuditedAIFunction` wrapper for the tool. Audit events flow to App Insights.

### Done

You have:
- A documented set of architectural decisions.
- A buildable solution with a real tool, real instructions, real telemetry.
- A live deployment via federated CI/CD.
- An eval suite running on PRs.
- Guardrails on input, output, and tool calls.

Elapsed: a focused day if you already know the patterns; a few days if you're learning as you go.
