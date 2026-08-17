---
name: agent-evaluation-strategy
description: Design and scaffold an evaluation suite for a code-first C# agent using Microsoft.Extensions.AI.Evaluation — dataset folder convention, fixture pattern, ground-truth case files, quality evaluators (relevance, coherence, groundedness), custom domain evaluators, and CI wiring. Use this skill when the user asks "how do I test my agent", "set up evals for my MAF agent", "evaluation strategy for a code-first agent", "add an evaluation test project", "score agent outputs against ground truth", or anything about systematic agent quality measurement (as distinct from unit tests).
---

# Agent Evaluation Strategy

Build an evaluation suite that proves the agent's behaviour, not just that the code compiles. Reference: [references/eval-fixture.cs](references/eval-fixture.cs).

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
├── appsettings.eval.json          # Foundry endpoint, judge deployment (intentionally empty so CI skips model calls)
├── Datasets/
│   ├── <scenario>/
│   │   ├── case-001.json          # simple: all data in one file
│   │   └── case-NNN/              # complex: folder-per-case when input is multi-part
│   │       ├── input.json         #   e.g. PR metadata, changed files
│   │       ├── build-log.txt      #   auxiliary agent input
│   │       ├── case.json          #   ground truth (culprit packages, fix files, escalation flag)
│   │       └── sample-output.json #   recorded good output for the offline deterministic test
│   └── ...
├── Evaluators/                    # custom IEvaluator implementations
├── Fixtures/
│   └── EvalFixture.cs             # shared DI + reporting config (xUnit IClassFixture)
└── <Workflow>EvalTests.cs         # one [Theory] per scenario folder
```

Why folders for datasets:
- Each scenario is a `[Theory]` with one test case per file/folder.
- Adding a case = dropping a file or folder. No code change.
- CI sees one row per case in the test results.

**Choose flat JSON** when the agent takes a single text prompt and returns text — `{ prompt, expected, notes }`.
**Choose folder-per-case** when the agent's input is multi-part (structured context, external files, tool call replays). Ground truth (`case.json`) stays separate from the agent-visible input so the evaluators stay generic.

## Case file shape

Flat (simple agents):

```json
{
  "prompt": "Summarise PR #42 for me.",
  "expected": "PR #42 introduces guardrail middleware...",
  "notes": "Regression for tool-result truncation."
}
```

Folder-per-case ground truth (`case.json`):

```json
{
  "caseId": "case-001",
  "culpritPackages": [{ "name": "SomePackage", "fromVersion": "2.0", "toVersion": "3.0" }],
  "fixFiles": ["src/Project/Project.csproj"],
  "fixSummary": "Remove duplicate registration introduced by v3 of SomePackage.",
  "expectsEscalation": false
}
```

Keep ground truth **invariant-based** (which packages broke, which files change, whether to escalate), not a verbatim expected answer. This makes the eval robust across model versions and agent wording.

For multi-turn cases, an array of `{ role, content }`. For tool-call assertions, an array of expected tool names (`expectedTools: ["GetPullRequest"]`).

## Choosing evaluators

Microsoft.Extensions.AI.Evaluation ships quality evaluators you should default to:

| Evaluator | When |
|---|---|
| `RelevanceEvaluator` | Did the answer address the question? |
| `CoherenceEvaluator` | Is the answer well-formed? |
| `GroundednessEvaluator` | Did the answer stick to retrieved/given context? |
| `EquivalenceEvaluator` | Does the answer match the expected (semantically, not literally)? |
| `RetrievalEvaluator` | RAG-only: did retrieval surface the right chunks? |

Custom evaluators (in `Evaluators/`) for domain rules: "did the agent call `GetPullRequest` exactly once?", "did the output JSON parse?", "did the answer name the right work-item id?".

## Reporting & thresholds

Use `DiskBasedReportingConfiguration` — writes structured results to `bin/.../EvalResults/`. The reporting CLI (`dotnet tool install Microsoft.Extensions.AI.Evaluation.Console`) renders a navigable HTML report.

Assertion strategy:
- **Per-case**: any evaluator returning `Unacceptable` fails the test.
- **Per-scenario aggregate**: pass-rate ≥ baseline (start at 90%, raise over time).
- **Regression gate**: in CI, fail if pass-rate drops below the recorded baseline minus a slack (e.g. 5pp). Store baselines in the repo (`Baselines/<scenario>.json`).

## Offline deterministic test (run without a model)

Ground-truth evaluators (custom `IEvaluator` subclasses that compare a parsed output against `case.json`) don't need an LLM. Add a plain `[Fact]` that feeds a recorded `sample-output.json` through the custom evaluators and asserts no `Unacceptable` metric. This:
- Runs in CI with no credentials and no cost.
- Validates the evaluators themselves are correctly wired — when a judged run later fails, you can trust the verdict.
- Catches prompt/parser regressions early.

## Skip-when-unconfigured (CI-safe model-dependent tests)

Add **Xunit.SkippableFact** (NuGet) so model-dependent tests skip cleanly instead of failing when no endpoint is configured:

```csharp
[SkippableTheory]
[MemberData(nameof(Cases))]
public async Task Agent_meets_ground_truth(string caseName)
{
    Skip.IfNot(_fx.ModelConfigured,
        "Set AzureAIFoundry:Endpoint/DeploymentName to run model-dependent evals.");
    // ...
}
```

`EvalFixture.ModelConfigured` returns `true` only when both endpoint and deployment name are non-empty. Make `Reporting` nullable (`ReportingConfiguration?`) and only build it when configured. Leave `appsettings.eval.json` with an intentionally empty endpoint so CI skips; developers supply the real value via user secrets or environment variables.

## `CreateScenarioRunAsync` — scenario vs iteration naming

The scenario name and iteration name are separate path segments on disk. **Never combine them with `/`** — the framework rejects path separators and throws `ArgumentException`:

```csharp
// Wrong — '/' in scenarioName causes ArgumentException
await Reporting.CreateScenarioRunAsync("renovate-breaking/case-001");

// Correct — scenario is the folder, iteration is the case
await Reporting.CreateScenarioRunAsync("renovate-breaking", iterationName: caseName);
```

## CI wiring

- **Per commit (fast)**: smoke subset — 1-2 cases per scenario, tagged `[Trait("eval", "smoke")]`.
- **Per PR (medium)**: full eval suite, run on a separate ADO stage that doesn't block merge but posts a status check.
- **Nightly**: full suite + report publish to a known location (blob, ADO artifact, internal site).

Eval runs use a **judge model deployment** — keep it separate from the agent's own deployment so you can swap judges without re-deploying the agent. Default the judge to the same deployment during initial setup (`Judge:Endpoint` / `Judge:DeploymentName` config keys that fall back to the agent's own endpoint), then split to a dedicated deployment as usage grows.

## Cost control

Evals burn tokens. Defences:
- Cache LLM responses for the agent-under-test in eval runs (`MEAI.IDistributedCache`-backed `IChatClient` middleware) so re-running the suite is mostly free until you change the agent.
- Limit `[InlineData]` cases per smoke scenario; full suite gates by PR not commit.
- Use a dedicated AOAI deployment for evals with its own quota.

## Hand-off

- Implementing the agent under test -> `maf-csharp-implementation`.
- The Azure OpenAI judge resource -> `azure-prepare` (one-time).
- Pipeline stage for eval runs -> `azure-devops-pipelines-for-agents`.
- Token budget concerns -> `azure-aigateway` (semantic caching, token limits).

## Official Documentation

- [Microsoft.Extensions.AI.Evaluation conceptual overview](https://learn.microsoft.com/en-us/dotnet/ai/conceptual/evaluation-libraries)
- [AI evaluation with reporting (tutorial)](https://learn.microsoft.com/en-us/dotnet/ai/tutorials/evaluate-with-reporting)
- [Xunit.SkippableFact (NuGet)](https://www.nuget.org/packages/Xunit.SkippableFact)
