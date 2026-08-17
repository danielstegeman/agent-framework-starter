---
name: agent-evaluation-strategy
description: Design and scaffold evaluation suites for code-first C# agents using Microsoft.Extensions.AI.Evaluation, Microsoft Agent Framework Foundry agents, CI-safe skip-when-unconfigured tests, custom domain evaluators, and judge deployment configuration. Use this skill when the user asks "how do I test my agent", "set up evals for my MAF agent", "evaluation strategy for a code-first agent", "add an evaluation test project", "score agent outputs against ground truth", or anything about systematic agent quality measurement (as distinct from unit tests).
---

# Agent Evaluation Strategy

Build an evaluation suite that proves the agent's behaviour, not just that the code compiles. Keep this skill link-first: use the official [Microsoft.Extensions.AI.Evaluation overview](https://learn.microsoft.com/en-us/dotnet/ai/conceptual/evaluation-libraries) and [reporting tutorial](https://learn.microsoft.com/en-us/dotnet/ai/tutorials/evaluate-with-reporting) for evaluator/reporting API details, then use the local glue fixture for repo-specific conventions: [references/eval-fixture.cs](references/eval-fixture.cs).

## Unit tests vs evals — pick the right tool

| | Unit tests | Evals |
|---|---|---|
| **Tests what** | Code paths in tools, orchestrators, parsers. | End-to-end agent outputs against scenarios. |
| **Deterministic?** | Yes. | No — LLM-as-judge metrics are statistical. |
| **Run on every commit?** | Yes. | Subset on commit, full suite on PR / nightly. |
| **Asserts** | Equality. | Metric thresholds + regression vs baseline. |

This skill covers **evals**. Tools and orchestrators get plain unit tests in `<Agent>.Tests`.

## Folder convention

```
tests/<Agent>.Evaluation.Tests/
├── appsettings.eval.json          # intentionally empty; devs/CI supply env/user secrets
├── Datasets/
│   ├── <scenario>/
│   │   ├── case-001.json          # simple: prompt/expected/notes in one file
│   │   └── case-NNN/              # complex: folder-per-case when input is multi-part
│   │       ├── input.json
│   │       ├── build-log.txt
│   │       ├── case.json          # ground truth, never shown to the agent
│   │       └── sample-output.json # recorded good output for deterministic evaluator tests
│   └── ...
├── Evaluators/                    # custom IEvaluator implementations
├── Fixtures/
│   └── EvalFixture.cs             # shared config, agent build-up, reporting config
└── <Workflow>EvalTests.cs         # one [Theory] per scenario folder
```

Why folders for datasets:
- Each scenario is a `[Theory]` with one test case per file/folder.
- Adding a case = dropping a file or folder. No code change.
- CI sees one row per case in the test results.

**Choose flat JSON** when the agent takes a single text prompt and returns text. **Choose folder-per-case** when the input is multi-part (structured context, external files, tool-call replays). Keep ground truth invariant-based (IDs, expected tool use, safety decision, escalation flag), not a verbatim expected answer.

## Foundry config and agent-under-test wiring

Use the current Foundry project endpoint convention:

| Purpose | Config key |
|---|---|
| Agent project endpoint | `AZURE_AI_PROJECT_ENDPOINT` |
| Agent model deployment | `AZURE_AI_MODEL_DEPLOYMENT_NAME` |
| Judge project endpoint | `Judge:AZURE_AI_PROJECT_ENDPOINT` (or env `Judge__AZURE_AI_PROJECT_ENDPOINT`) |
| Judge model deployment | `Judge:AZURE_AI_MODEL_DEPLOYMENT_NAME` (or env `Judge__AZURE_AI_MODEL_DEPLOYMENT_NAME`) |

The endpoint is the **project endpoint** (`...services.ai.azure.com/api/projects/...`), not an old `/models` inference endpoint. Build the agent-under-test with the [Microsoft Foundry provider](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/microsoft-foundry) pattern: `new AIProjectClient(...).AsAIAgent(model: ..., instructions: ..., tools: ...)`. Do not use `ChatCompletionsClient` for the agent path.

Leave `appsettings.eval.json` empty in the repo so CI skips model calls by default. Developers and scheduled pipelines supply endpoint/deployment values through user secrets or environment variables.

## Choosing evaluators

Default to the built-in quality evaluators listed in the official evaluation overview, especially relevance, coherence, groundedness/equivalence for text answers, and the agent-focused tool-call metrics when they fit. Add custom evaluators in `Evaluators/` for domain rules such as:
- did the agent call `GetPullRequest` exactly once?
- did the output JSON parse and satisfy the schema?
- did the answer name the right work item and avoid unsafe remediation?

## Offline deterministic test (run without a model)

Ground-truth evaluators that compare parsed output against `case.json` do not need an LLM. Add a plain `[Fact]` that feeds `sample-output.json` through the custom evaluators and asserts no `Unacceptable` metric. This runs in CI without credentials, validates evaluator wiring, and catches parser/prompt regressions early.

## Skip-when-unconfigured model tests

Add [Xunit.SkippableFact](https://www.nuget.org/packages/Xunit.SkippableFact) so model-dependent tests skip cleanly instead of failing when no endpoint is configured:

```csharp
Skip.IfNot(_fx.ModelConfigured,
    "Set AZURE_AI_PROJECT_ENDPOINT and AZURE_AI_MODEL_DEPLOYMENT_NAME to run model-dependent evals.");
```

`EvalFixture.ModelConfigured` returns `true` only when the agent endpoint and deployment are non-empty. Make reporting nullable (`ReportingConfiguration?`) and build it only when configured.

## `CreateScenarioRunAsync` — scenario vs iteration naming

The scenario name and iteration name are separate path segments on disk. **Never combine them with `/`** — the framework rejects path separators and throws `ArgumentException`:

```csharp
await Reporting.CreateScenarioRunAsync("renovate-breaking", iterationName: caseName);
```

## CI wiring

- **Per commit (fast)**: smoke subset — 1-2 cases per scenario, tagged `[Trait("eval", "smoke")]`.
- **Per PR (medium)**: full eval suite, run on a separate stage that posts a status check.
- **Nightly**: full suite + report publish to a known location (blob, pipeline artifact, internal site).

Eval runs use a **judge model deployment**. Default the judge to the same deployment during initial setup, then split to a dedicated deployment as usage grows.

## Cost control

- Cache LLM responses in eval runs with the reporting library's response caching.
- Limit `[InlineData]` cases per smoke scenario; full suite gates by PR not commit.
- Use a dedicated judge deployment/quota for evals when volume grows.

## Hand-off

- Implementing the agent under test -> `maf-csharp-implementation`.
- The Azure AI Foundry judge resource -> `azure-prepare` (one-time).
- Pipeline stage for eval runs -> `azure-devops-pipelines-for-agents`.
- Token budget concerns -> `azure-aigateway` (semantic caching, token limits).

## Official Documentation

- [Microsoft.Extensions.AI.Evaluation conceptual overview](https://learn.microsoft.com/en-us/dotnet/ai/conceptual/evaluation-libraries)
- [AI evaluation with reporting (tutorial)](https://learn.microsoft.com/en-us/dotnet/ai/tutorials/evaluate-with-reporting)
- [Microsoft Foundry model provider for Agent Framework](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/microsoft-foundry)
- [Xunit.SkippableFact (NuGet)](https://www.nuget.org/packages/Xunit.SkippableFact)
