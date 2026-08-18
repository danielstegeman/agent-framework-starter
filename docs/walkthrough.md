# Walkthrough: From zero to a deployed code-first agent

A worked example using the two agents in this starter: **`agent-architect`** (decides and documents) and **`agent-builder`** (implements). Skill names in backticks are the leaves each agent delegates to.

## Prerequisites

Before you start, install the **Azure VS Code extensions** — the architect recommends several implementation-backed options that depend on the companion skills these extensions ship:

- **Azure Tools** extension pack (or at minimum **Azure Resources**, **Container Apps**, **Bicep**).
- **Azure Developer CLI (azd)** support.
- **GitHub Copilot for Azure** (surfaces `azure-prepare` / `azure-validate` / `azure-deploy` / `azure-rbac` / `appinsights-instrumentation` / `entra-app-registration`).

Without these, some recommended options downgrade to "alternatives" and the architect will grill you on them instead of recommending them.

## Setup

Install the [APM CLI](https://microsoft.github.io/apm/), then in any project where you want the skills available:

```bash
apm install <owner>/code-first-agent-starter
```

This curated aggregator pulls all five sub-packages (`maf-core`, `agent-design`, `dotnet-implementation`, `azure-infrastructure`, `quality-safety`) — both agents and all skills. Open a fresh Copilot Chat or Claude conversation in that workspace.

## The journey

**You:** *"Help me design a code-first agent that summarises Azure DevOps work items."*

The **`agent-architect`** picks up the request, discovers which sub-packages and skills are actually installed in this workspace, confirms the Azure extensions are present, then asks:
> Greenfield or expansion? Where would you like to start?

**You:** *"Greenfield. Walk me through the decisions."*

### 1. Decisions (`agent-architecture-decisions`, driven by `agent-architect`)

The architect recommends only what it found installed in step 0, and grills you on any alternative. Starting from a few capability-shaping questions (trigger, what it needs to do, single vs multi-agent), it opens only the branches those answers imply:
- **Trigger model**: webhook from ADO on work-item update + a CLI for testing.
- **Observability**: OTel -> App Insights (prod), Aspire dashboard (local).
- **Hosting**: Azure Container Apps, public ingress (recommended starting point — chosen after comparing App Service / Functions / Foundry Hosted Agents).
- **Tools**: in-process tools class talking to ADO REST API (recommended — an MCP tool surface was considered and deferred).
- **Context sources**: tool-fetched only — no RAG.
- **Sandbox**: the agent runs no model-generated code — recorded as "no execution", so this branch closes immediately.
- **Flexibility vs determinism**: single agent, one tool call per run — no workflow/orchestrator branch opened.
- **Guardrails**: PII redaction on the work-item body before it hits the model.
- **Identity**: UAMI for the workload, federated MI for the ADO pipeline.

Output: a single `docs/decisions.md` capturing each decision actually made (chosen option, backed skill or custom alternative, one-line rationale, revisit trigger).

### Hand-off to the builder

The architect confirms shared understanding and **hands off to `agent-builder`** with the decisions summary as input. From here the builder owns the conversation.

### 2. Scaffold (`dotnet-agent-bootstrap`, driven by `agent-builder`)

```bash
cd ~/work
mkdir work-item-summariser && cd work-item-summariser
```

The bootstrap skill runs:
- `dotnet new sln -n WorkItemSummariser`
- Creates a `Host` project and a `WorkItemSummariser` agent library (plus `Tests`, `Evaluation.Tests`, `AppHost`). Start minimal — split tools into their own project or adopt per-agent slices later only if the solution grows to need it.
- Adds packages: `Microsoft.Agents.AI` (GA), `Microsoft.Agents.AI.Foundry` + `Azure.AI.Projects` (`--prerelease`), `Azure.Identity`, OTel + Azure Monitor exporter.
- Writes `Directory.Build.props`, `global.json`, `.editorconfig`, `.gitignore`.
- Follows the current patterns from the [Microsoft Agent Framework get-started docs](https://learn.microsoft.com/en-us/agent-framework/get-started/your-first-agent) — the agent is created from `AIProjectClient.AsAIAgent(...)`, with telemetry via native `.UseOpenTelemetry()` / `.WithOpenTelemetry()`.
- Runs `dotnet build && dotnet test` — both green.
- `git init -b main && git add . && git commit -m "chore: scaffold via dotnet-agent-bootstrap"`.

You can now run:
```bash
dotnet run --project src/WorkItemSummariser.Host -- "Hello!"
```

### 3. Add the real tool (`maf-csharp-implementation`)

The implementation skill shows how to author in-process function tools. You write `WorkItemTools.GetWorkItem(int id, ...)` with `[Description]` attributes and register the tool with the agent. (Keep tools in the agent project to start; move them into a dedicated tools project only when more than one agent shares them. If you'd rather expose tools over a protocol, see `maf-mcp-tools`.) Add `Instructions/Summariser.md` describing the persona.

```bash
dotnet test            # still green
dotnet run --project src/WorkItemSummariser.Host -- "Summarise work item 12345"
```

> Coordinating several agents or a fixed multi-step flow later? `maf-workflows-orchestration` covers graph workflows and the sequential/concurrent/handoff/group-chat/magentic patterns.

### 4. Local dev with Aspire + DevUI (`dotnet-aspire-apphost`)

```bash
dotnet new aspire-apphost -n WorkItemSummariser.AppHost -o src/WorkItemSummariser.AppHost
dotnet sln add src/WorkItemSummariser.AppHost
dotnet add src/WorkItemSummariser.AppHost reference src/WorkItemSummariser.Host
```

`AppHost/Program.cs` declares the host project. F5 in VS opens the Aspire dashboard with live OTel traces — a span per tool call. DevUI gives you an interactive chat surface to exercise the agent locally.

### 5. Model deployment (`foundry-model-deployment`)

Before provisioning Container Apps, ensure a Foundry model deployment exists. If you already have one, skip to step 6 and record your endpoint and deployment name.

If not, provision the Foundry resource:

```bash
az deployment group create \
  --resource-group rg-agent-dev \
  --template-file infra/azure-ai-foundry.bicep \
  --parameters \
      accountName=work-item-summariser-ai \
      modelPublisher=OpenAI \
      modelName=<model-deployment> \
      capacityTpu=10
```

Record the outputs for use in the next step and in local `appsettings.Development.json`:
- `projectEndpoint` → `AZURE_AI_PROJECT_ENDPOINT` (`https://<account>.services.ai.azure.com/api/projects/<project>`)
- `deploymentName` → `AZURE_AI_MODEL_DEPLOYMENT_NAME`

Grant your developer identity (and later the UAMI) `Cognitive Services User` on the Foundry account.

### 6. Infrastructure (`agent-infrastructure-overview` → leaves)

Walk the 10-item checklist. Then:
- `foundry-model-deployment` was completed in step 5 above — pass its outputs to `azure-container-apps-bicep` as `AZURE_AI_PROJECT_ENDPOINT` and `AZURE_AI_MODEL_DEPLOYMENT_NAME` container env vars.
- `azure-container-apps-bicep` produces `infra/container-apps.bicep` + `infra/rbac.bicep` (include `Cognitive Services User` assignment for the UAMI on the Foundry account).
- `azure-devops-pipelines-for-agents` produces `azure-pipelines.yml`.
- `agent-secrets-identity` makes sure the UAMI exists, the federated service connection is wired, and the App Insights connection string is in Key Vault.

Create the ADO service connection (workload identity federation), the variable group `agent-dev`, and push:

```bash
git remote add origin <ado-url>
git push -u origin main
```

The pipeline builds, pushes the image, and deploys. The agent is live.

### 7. Evals (`agent-evaluation-strategy`)

You add three case files under `tests/WorkItemSummariser.Evaluation.Tests/Datasets/summarise-basic/`:
- `case-001.json` — typical work item.
- `case-002.json` — work item with code-snippet noise (should be ignored).
- `case-003.json` — work item with PII in the description (should be redacted in the output).

`EvalFixture.cs` wires `RelevanceEvaluator` + `CoherenceEvaluator` + a custom `MentionsWorkItemIdEvaluator`. Pipeline gains a `Eval` stage that runs the smoke subset on PR.

### 8. Guardrails (`agent-guardrails-safety`)

You add `InputRedactionMiddleware` (Microsoft Presidio sidecar in ACA), `PromptInjectionGuardMiddleware` (Azure AI Content Safety Prompt Shields), and an `AuditedAIFunction` wrapper for the tool. Audit events flow to App Insights.

### Done

You have:
- A documented set of architectural decisions.
- A buildable solution with a real tool, real instructions, real telemetry.
- A live deployment via federated CI/CD.
- An eval suite running on PRs.
- Guardrails on input, output, and tool calls.

Elapsed: a focused day if you already know the patterns; a few days if you're learning as you go.
