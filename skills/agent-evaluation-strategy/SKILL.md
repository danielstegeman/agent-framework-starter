---
name: agent-evaluation-strategy
description: Design and scaffold an evaluation suite for a code-first C# agent using Microsoft.Extensions.AI.Evaluation — dataset folder convention, fixture pattern, ground-truth case files, quality evaluators (relevance, coherence, groundedness), custom domain evaluators, and CI wiring. Use this skill when the user asks "how do I test my agent", "set up evals for my MAF agent", "evaluation strategy for a code-first agent", "add an evaluation test project", "score agent outputs against ground truth", or anything about systematic agent quality measurement (as distinct from unit tests).
---

# Agent Evaluation Strategy

Build an evaluation suite that proves the agent's behaviour, not just that the code compiles. Reference: [references/eval-fixture.cs](../../references/eval-fixture.cs).

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
├── appsettings.eval.json          # AOAI endpoint, judge model deployment, KV ref if needed
├── Datasets/
│   ├── <scenario>/
│   │   ├── case-001.json
│   │   └── case-NNN.json
│   └── ...
├── Evaluators/                    # custom IEvaluator implementations
├── Fixtures/
│   └── EvalFixture.cs             # shared DI + reporting config (xUnit IClassFixture)
└── <Workflow>EvalTests.cs         # one [Theory] per scenario folder
```

Why folders for datasets:
- Each scenario is a `[Theory]` with one `[InlineData]` per case file.
- Adding a case = dropping a file. No code change.
- CI sees one row per case in the test results.

## Case file shape

Keep it minimal and stable:

```json
{
  "prompt": "Summarise PR #42 for me.",
  "expected": "PR #42 introduces guardrail middleware...",
  "notes": "Regression for tool-result truncation."
}
```

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

## CI wiring

- **Per commit (fast)**: smoke subset — 1-2 cases per scenario, tagged `[Trait("eval", "smoke")]`.
- **Per PR (medium)**: full eval suite, run on a separate ADO stage that doesn't block merge but posts a status check.
- **Nightly**: full suite + report publish to a known location (blob, ADO artifact, internal site).

Eval runs use a **judge model deployment** — usually a stronger / cheaper model than the agent itself uses. Keep that deployment separate so you can swap judges without re-deploying the agent.

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
