// references/sandbox-tools.cs
//
// MAF tools that expose the sandbox to the agent. Each method maps to exactly
// ONE ISandbox operation — the tool is the thin boundary, ISandbox is the
// transport, the session is where code runs. No business logic lives here.
//
// FILE LOCATION: src/<Solution>.Tools.Sandbox/SandboxTools.cs
//
// Demonstrates:
//   1. Tool authoring per maf-csharp-implementation: public methods with
//      [Description] on the method and every parameter; constructor injection
//      only. The agent build-up reflects these into AIFunctions.
//   2. Conversation-scoped routing: the conversation id selects the session, so
//      files written in one turn are visible in the next.
//   3. Per-call audit + a trace span on the same trail as other tools
//      (see agent-guardrails-safety). Command text / paths are hashed in prod.
//   4. Bounded execution: a wall-clock timeout is layered onto the incoming
//      CancellationToken so a runaway command is cancelled.

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Sandbox;

public sealed class SandboxToolsOptions
{
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(60);

    // When true, log raw command text and file paths. Keep OFF in production —
    // they may contain sensitive data; the audit trail logs hashes instead.
    public bool LogRawArguments { get; init; }
}

public sealed class SandboxTools(
    ISandbox sandbox,
    IConversation conversation,           // supplies the current conversation id
    ILogger<SandboxTools> logger,
    Microsoft.Extensions.Options.IOptions<SandboxToolsOptions> options)
{
    private static readonly ActivitySource Activity = new("Agent.Sandbox");
    private readonly SandboxToolsOptions _opts = options.Value;

    [Description("Runs a shell command inside the isolated sandbox for this conversation and returns its exit code, standard output, and standard error. Use this to execute code, run builds, or run tests. The command cannot reach the network unless an allow-list was configured.")]
    public async Task<CommandResult> RunCommand(
        [Description("The shell command to run, e.g. 'dotnet test' or 'python main.py'.")] string command,
        CancellationToken ct = default)
    {
        using var activity = Activity.StartActivity("sandbox.run_command");
        using var cts = CreateBoundedToken(ct);
        var session = await sandbox.GetOrCreateSessionAsync(conversation.Id, cts.Token);
        var sw = Stopwatch.StartNew();
        var result = await session.ExecuteCommandAsync(command, cts.Token);
        Audit(activity, "run_command", command, result.ExitCode, sw.Elapsed);
        return result;
    }

    [Description("Reads a UTF-8 text file from the sandbox workspace for this conversation.")]
    public async Task<string> ReadFile(
        [Description("Workspace-relative path, e.g. 'src/Program.cs'.")] string path,
        CancellationToken ct = default)
    {
        using var activity = Activity.StartActivity("sandbox.read_file");
        var session = await sandbox.GetOrCreateSessionAsync(conversation.Id, ct);
        var sw = Stopwatch.StartNew();
        var content = await session.ReadFileAsync(path, ct);
        Audit(activity, "read_file", path, exitCode: 0, sw.Elapsed);
        return content;
    }

    [Description("Writes a UTF-8 text file into the sandbox workspace for this conversation, creating or overwriting it.")]
    public async Task<string> WriteFile(
        [Description("Workspace-relative path to write, e.g. 'src/Program.cs'.")] string path,
        [Description("Full UTF-8 file content.")] string content,
        CancellationToken ct = default)
    {
        using var activity = Activity.StartActivity("sandbox.write_file");
        var session = await sandbox.GetOrCreateSessionAsync(conversation.Id, ct);
        var sw = Stopwatch.StartNew();
        await session.WriteFileAsync(path, content, ct);
        Audit(activity, "write_file", path, exitCode: 0, sw.Elapsed);
        return $"Wrote {content.Length} characters to {path}.";
    }

    [Description("Runs a git command inside the sandbox workspace (e.g. 'status', 'diff', 'add .'). The repository is brokered by the host; pushing requires an explicitly allow-listed remote.")]
    public async Task<CommandResult> Git(
        [Description("Git arguments without the leading 'git', e.g. 'status --short'.")] string args,
        CancellationToken ct = default)
    {
        using var activity = Activity.StartActivity("sandbox.git");
        using var cts = CreateBoundedToken(ct);
        var session = await sandbox.GetOrCreateSessionAsync(conversation.Id, cts.Token);
        var sw = Stopwatch.StartNew();
        var result = await session.ExecuteCommandAsync($"git {args}", cts.Token);
        Audit(activity, "git", args, result.ExitCode, sw.Elapsed);
        return result;
    }

    private CancellationTokenSource CreateBoundedToken(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_opts.CommandTimeout);
        return cts;
    }

    private void Audit(Activity? activity, string tool, string argument, int exitCode, TimeSpan duration)
    {
        var safeArg = _opts.LogRawArguments ? argument : Hash(argument);
        activity?.SetTag("sandbox.tool", tool);
        activity?.SetTag("sandbox.conversation_id", conversation.Id);
        activity?.SetTag("sandbox.exit_code", exitCode);
        activity?.SetTag("sandbox.duration_ms", duration.TotalMilliseconds);
        logger.LogInformation(
            "Sandbox tool {Tool} conversation={ConversationId} arg={Arg} exit={ExitCode} duration={DurationMs}ms",
            tool, conversation.Id, safeArg, exitCode, duration.TotalMilliseconds);
    }

    private static string Hash(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return "sha256:" + Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}

/// <summary>Supplies the current conversation/thread id. Implement over your
/// agent session/thread so tools route to the right sandbox. Registered scoped.</summary>
public interface IConversation
{
    string Id { get; }
}
