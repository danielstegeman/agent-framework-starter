// references/eval-fixture.cs
//
// Repo glue for Microsoft.Extensions.AI.Evaluation + xUnit. Use the official
// evaluation docs for API details; keep this fixture focused on local dataset
// conventions, CI-safe skipping, and Agent Framework Foundry wiring.
//
// NuGet packages required (in addition to the agent's own dependencies):
//   Azure.AI.Projects                     // prerelease as noted by Agent Framework docs
//   Azure.Identity
//   Microsoft.Agents.AI
//   Microsoft.Agents.AI.Foundry           // prerelease as noted by Agent Framework docs
//   Microsoft.Extensions.AI.Evaluation.Reporting
//   Microsoft.Extensions.AI.Evaluation.Quality
//   Xunit.SkippableFact

using Azure.AI.Projects;
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

public sealed record EvalCase(string Prompt, string Expected, string? Notes = null);

public sealed class EvalFixture : IAsyncLifetime
{
    public IServiceProvider Services { get; private set; } = null!;

    /// <summary>Null when no model endpoint is configured — model-dependent tests must skip.</summary>
    public ReportingConfiguration? Reporting { get; private set; }

    /// <summary>True when an Azure AI Foundry project endpoint and deployment are configured.</summary>
    public bool ModelConfigured { get; private set; }

    public Task InitializeAsync()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.eval.json", optional: true)
            .AddUserSecrets(typeof(EvalFixture).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var agentEndpoint = config["AZURE_AI_PROJECT_ENDPOINT"];
        var agentDeployment = config["AZURE_AI_MODEL_DEPLOYMENT_NAME"];
        ModelConfigured = !string.IsNullOrWhiteSpace(agentEndpoint)
                       && !string.IsNullOrWhiteSpace(agentDeployment);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential(
            new DefaultAzureCredentialOptions
            {
                ExcludeManagedIdentityCredential = !IsRunningOnAzure(),
            }));

        if (ModelConfigured)
        {
            services.AddSingleton(sp =>
            {
                var credential = sp.GetRequiredService<TokenCredential>();
                var project = new AIProjectClient(new Uri(agentEndpoint!), credential);

                return project.AsAIAgent(
                    model: agentDeployment!,
                    name: "agent-under-test",
                    instructions: "Replace with the same instructions used by the production host.",
                    tools: [/* AIFunctionFactory.Create(...) production tools */]);
            });
        }

        Services = services.BuildServiceProvider();

        if (ModelConfigured)
        {
            var credential = Services.GetRequiredService<TokenCredential>();
            var judgeEndpoint = config["Judge:AZURE_AI_PROJECT_ENDPOINT"]
                             ?? config["JUDGE_AZURE_AI_PROJECT_ENDPOINT"]
                             ?? agentEndpoint!;
            var judgeDeployment = config["Judge:AZURE_AI_MODEL_DEPLOYMENT_NAME"]
                                ?? config["JUDGE_AZURE_AI_MODEL_DEPLOYMENT_NAME"]
                                ?? agentDeployment!;

            IChatClient judgeClient = new AIProjectClient(new Uri(judgeEndpoint), credential)
                .GetProjectOpenAIClient()
                .GetProjectResponsesClient()
                .AsIChatClient(judgeDeployment);

            Reporting = DiskBasedReportingConfiguration.Create(
                storageRootPath: Path.Combine(AppContext.BaseDirectory, "EvalResults"),
                evaluators: [new RelevanceEvaluator(), new CoherenceEvaluator(), new EquivalenceEvaluator()],
                chatConfiguration: new ChatConfiguration(judgeClient),
                enableResponseCaching: true);
        }

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        (Services as IDisposable)?.Dispose();
        return Task.CompletedTask;
    }

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

    [SkippableTheory]
    [MemberData(nameof(Cases))]
    [Trait("eval", "full")]
    public async Task Agent_meets_ground_truth(string caseName)
    {
        Skip.IfNot(_fx.ModelConfigured,
            "Set AZURE_AI_PROJECT_ENDPOINT and AZURE_AI_MODEL_DEPLOYMENT_NAME to run model-dependent evals.");

        var evalCase = LoadCase(caseName);
        var agent = _fx.Services.GetRequiredService<AIAgent>();

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
