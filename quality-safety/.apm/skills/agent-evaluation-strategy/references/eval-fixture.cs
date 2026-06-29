// references/eval-fixture.cs
//
// Evaluation test pattern using Microsoft.Extensions.AI.Evaluation + xUnit.
// Folder convention:
//
//   tests/<YourAgent>.Evaluation.Tests/
//     Datasets/<scenario-name>/case-001.json        # flat: prompt/expected/notes
//     Datasets/<scenario-name>/case-002/             # folder-per-case: multi-part input
//       input.json, build-log.txt, case.json, sample-output.json
//     Evaluators/                                    # custom IEvaluator implementations
//     Fixtures/EvalFixture.cs                        # shared DI + agent build-up
//     <Workflow>EvalTests.cs                         # one [Theory] per dataset folder
//
// NuGet packages required (in addition to the agent's own dependencies):
//   Microsoft.Extensions.AI.Evaluation.Reporting
//   Xunit.SkippableFact                             # for CI-safe skip-when-unconfigured

using Azure.Core;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// Simple flat-file case shape (use when the agent takes a single prompt):
public sealed record EvalCase(string Prompt, string Expected, string? Notes = null);

public sealed class EvalFixture : IAsyncLifetime
{
    public IServiceProvider Services { get; private set; } = null!;

    /// <summary>Null when no model endpoint is configured — model-dependent tests must skip.</summary>
    public ReportingConfiguration? Reporting { get; private set; }

    /// <summary>True when an Azure AI Foundry endpoint and deployment are configured.</summary>
    public bool ModelConfigured { get; private set; }

    public Task InitializeAsync()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.eval.json", optional: true)
            // Allow developers to supply real endpoint values without editing the committed file.
            .AddUserSecrets(typeof(EvalFixture).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var agentEndpoint   = config["AzureAIFoundry:Endpoint"];
        var agentDeployment = config["AzureAIFoundry:DeploymentName"];
        ModelConfigured = !string.IsNullOrWhiteSpace(agentEndpoint)
                       && !string.IsNullOrWhiteSpace(agentDeployment);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();

        // Exclude the managed-identity IMDS probe when not running on Azure — it hangs for
        // minutes before failing, blocking the credential chain from reaching Visual Studio /
        // Azure CLI login on a developer machine.
        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential(
            new DefaultAzureCredentialOptions
            {
                ExcludeManagedIdentityCredential = !IsRunningOnAzure(),
            }));

        // Register agent + tools exactly the way the production host does.
        // services.AddYourAgent();
        Services = services.BuildServiceProvider();

        if (ModelConfigured)
        {
            // Judge defaults to the same deployment as the agent ("start cheap, split later").
            // Override with Judge:Endpoint / Judge:DeploymentName in user secrets or env vars
            // to point at a dedicated judge deployment.
            var judgeEndpoint   = config["Judge:Endpoint"]       ?? agentEndpoint!;
            var judgeDeployment = config["Judge:DeploymentName"] ?? agentDeployment!;
            // Build a judge IChatClient (same way the agent registration builds one):
            // var judge = AgentRegistration.CreateChatClient(judgeEndpoint, judgeDeployment, credential);

            Reporting = DiskBasedReportingConfiguration.Create(
                storageRootPath: Path.Combine(AppContext.BaseDirectory, "EvalResults"),
                evaluators: [new RelevanceEvaluator(), new CoherenceEvaluator(), new EquivalenceEvaluator()],
                // chatConfiguration: new ChatConfiguration(judge),
                enableResponseCaching: true);
        }

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        (Services as IDisposable)?.Dispose();
        return Task.CompletedTask;
    }

    // Scenario and iteration are separate path segments — never join with '/'.
    // Call: await Reporting!.CreateScenarioRunAsync(scenarioName, iterationName: caseName)

    public static IEnumerable<string> EnumerateCases(string scenario)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Datasets", scenario);
        return Directory.EnumerateFiles(root, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Order()!;
    }

    private static bool IsRunningOnAzure() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID")) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CONTAINER_APP_NAME"));
}

public sealed class MyAgentEvalTests : IClassFixture<EvalFixture>
{
    private const string Scenario = "my-scenario";

    private readonly EvalFixture _fx;
    public MyAgentEvalTests(EvalFixture fx) => _fx = fx;

    // -------------------------------------------------------------------------
    // Offline deterministic test — runs in CI with no model, no credentials.
    // Feed a recorded sample-output.json through your custom IEvaluators and
    // assert none are Unacceptable. This validates the evaluator logic itself.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Deterministic_evaluators_pass_on_recorded_sample_output()
    {
        var sampleJson = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Datasets", Scenario, "case-001", "sample-output.json"));
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, sampleJson));

        IEvaluator[] evaluators = [/* new MyCustomEvaluator() */];
        foreach (var evaluator in evaluators)
        {
            var result = await evaluator.EvaluateAsync([], response);
            Assert.DoesNotContain(result.Metrics.Values,
                m => m.Interpretation?.Rating == EvaluationRating.Unacceptable);
        }
    }

    // -------------------------------------------------------------------------
    // Judged theory — skips cleanly when no model endpoint is configured so CI
    // doesn't fail when running without credentials.
    //
    // Tag a fast subset [Trait("eval","smoke")] (1-2 cases per scenario) for
    // per-commit runs; leave the full theory for PR-stage / nightly.
    // -------------------------------------------------------------------------
    [SkippableTheory]
    [MemberData(nameof(Cases))]
    [Trait("eval", "full")]
    public async Task Agent_meets_ground_truth(string caseName)
    {
        Skip.IfNot(_fx.ModelConfigured,
            "Set AzureAIFoundry:Endpoint/DeploymentName (user secrets or env) to run model-dependent evals.");

        var evalCase = LoadCase(caseName);
        var agent = _fx.Services.GetRequiredService<AIAgent>();

        // Scenario and iteration are separate path segments — never join with '/'.
        await using var run = await _fx.Reporting!.CreateScenarioRunAsync(Scenario, iterationName: caseName);

        var response = await agent.RunAsync(evalCase.Prompt);

        var result = await run.EvaluateAsync(
            [new ChatMessage(ChatRole.User, evalCase.Prompt)],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, response.Text)),
            additionalContext: [new EquivalenceEvaluatorContext(evalCase.Expected)]);

        var unacceptable = result.Metrics.Values
            .Where(m => m.Interpretation?.Rating == EvaluationRating.Unacceptable)
            .Select(m => $"{m.Name}: {m.Interpretation?.Reason}")
            .ToList();

        Assert.True(unacceptable.Count == 0,
            $"Unacceptable metrics for {caseName}:{Environment.NewLine}{string.Join(Environment.NewLine, unacceptable)}");
    }

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var name in EvalFixture.EnumerateCases(Scenario))
            data.Add(name);
        return data;
    }

    private static EvalCase LoadCase(string caseName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Datasets", Scenario, $"{caseName}.json");
        var json = File.ReadAllText(path);
        return System.Text.Json.JsonSerializer.Deserialize<EvalCase>(json,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
    }
}
