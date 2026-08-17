// references/ISandbox.cs
//
// The sandbox abstraction. The agent's tools depend ONLY on this interface;
// they never know whether code runs in an Azure Container Apps dynamic session
// (cloud) or a local Docker container (dev). Both implementations drive the
// SAME session-container image.
//
// FILE LOCATION: src/<Solution>.Tools.Sandbox/ISandbox.cs
//
// Design notes:
//   - Sessions are keyed by a free-form identifier. Use the CONVERSATION ID so
//     follow-up turns reuse the same session (files persist within a
//     conversation) and different conversations stay isolated.
//   - Every method takes a CancellationToken. The caller supplies a wall-clock
//     timeout; a runaway execution is cancelled, not awaited forever.
//   - The session holds NO credentials. Data crosses the boundary as scoped
//     inputs/outputs only.

namespace Sandbox;

/// <summary>An isolated, ephemeral code-execution environment for one conversation.</summary>
public interface ISandbox
{
    /// <summary>
    /// Ensures a session exists for <paramref name="conversationId"/> and returns a
    /// handle to it. If one is already running it is reused; otherwise one is
    /// allocated. The identifier becomes the dynamic-sessions routing identifier.
    /// </summary>
    Task<ISandboxSession> GetOrCreateSessionAsync(string conversationId, CancellationToken ct);
}

/// <summary>A handle to one allocated session. Operations proxy into the isolated runtime.</summary>
public interface ISandboxSession
{
    /// <summary>The conversation-scoped identifier this session is bound to.</summary>
    string Identifier { get; }

    /// <summary>Runs a shell command in the session and returns its result.</summary>
    Task<CommandResult> ExecuteCommandAsync(string command, CancellationToken ct);

    /// <summary>Reads a UTF-8 text file from the session workspace.</summary>
    Task<string> ReadFileAsync(string path, CancellationToken ct);

    /// <summary>Writes a UTF-8 text file into the session workspace.</summary>
    Task WriteFileAsync(string path, string content, CancellationToken ct);

    /// <summary>Lists files under a workspace directory (defaults to the workspace root).</summary>
    Task<IReadOnlyList<FileEntry>> ListFilesAsync(string path, CancellationToken ct);
}

public sealed record CommandResult(int ExitCode, string Stdout, string Stderr);

public sealed record FileEntry(string Path, long SizeBytes, DateTimeOffset LastModified);
