// references/eval-fixture.cs
//
// Evaluation test pattern using Microsoft.Extensions.AI.Evaluation + xUnit.
// Folder convention:
//
//   tests/<YourAgent>.Evaluation.Tests/
//     Datasets/<scenario-name>/case-001.json
//     Evaluators/                          # custom IEvaluator implementations
//     Fixtures/EvalFixture.cs              # shared DI + agent build-up
//     <Workflow>EvalTests.cs               # one [Theory] per dataset folder

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
    public ReportingConfiguration Reporting { get; private set; } = null!;

    public Task InitializeAsync()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.eval.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        // Register agent + tools the same way the production host does.
        // services.AddWeatherAgent();
        services.AddSingleton<IConfiguration>(config);
        Services = services.BuildServiceProvider();

        var chatClient = Services.GetRequiredService<IChatClient>();
        Reporting = DiskBasedReportingConfiguration.Create(
            storageRootPath: Path.Combine(AppContext.BaseDirectory, "EvalResults"),
            evaluators: [new RelevanceEvaluator(), new CoherenceEvaluator()],
            chatConfiguration: new ChatConfiguration(chatClient));

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public static IEnumerable<object[]> LoadCases(string scenarioFolder)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Datasets", scenarioFolder);
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            var json = File.ReadAllText(file);
            var c = System.Text.Json.JsonSerializer.Deserialize<EvalCase>(json)!;
            yield return new object[] { Path.GetFileNameWithoutExtension(file), c };
        }
    }
}

public sealed class WeatherAgentEvalTests : IClassFixture<EvalFixture>
{
    private readonly EvalFixture _fx;
    public WeatherAgentEvalTests(EvalFixture fx) => _fx = fx;

    [Theory]
    [MemberData(nameof(EvalFixture.LoadCases), "weather-basic", MemberType = typeof(EvalFixture))]
    public async Task Weather_basic_cases(string caseName, EvalCase c)
    {
        var agent = _fx.Services.GetRequiredService<AIAgent>();
        await using var run = _fx.Reporting.CreateScenarioRun(scenarioName: caseName);

        var response = await agent.RunAsync(c.Prompt);

        var result = await run.EvaluateAsync(
            [new ChatMessage(ChatRole.User, c.Prompt)],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, response.Text)));

        Assert.DoesNotContain(result.Metrics.Values, m => m.Interpretation?.Rating == EvaluationRating.Unacceptable);
    }
}
