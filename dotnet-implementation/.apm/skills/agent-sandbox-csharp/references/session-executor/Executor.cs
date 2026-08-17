// references/session-executor/Executor.cs
//
// The minimal ASP.NET Core executor that runs INSIDE the session container.
// It is the only thing listening in the sandbox. The host proxies one operation
// per request; this process shells out within /workspace and returns the result.
//
// FILE LOCATION (inside the image): session-executor/Executor.cs  (=> Executor.dll)
//
// Security posture:
//   - Runs as the non-root 'sandbox' user (set in the Dockerfile).
//   - Holds NO credentials — the host never forwards a token into the session.
//   - No outbound network by default (NetworkMode=none locally; pool egress off).
//   - All paths are confined under /workspace; traversal outside is rejected.
//
// A .NET executor keeps the stack uniform with the rest of the solution. (The
// official Azure-Samples dynamic-sessions custom-container precedent uses a tiny
// Python/Flask executor — either works; the HTTP contract is what matters.)

using System.Diagnostics;
using System.Text;

var builder = WebApplication.CreateSlimBuilder(args);
var app = builder.Build();

const string Workspace = "/workspace";

app.MapGet("/health", () => Results.Ok("ok"));

// Runs a shell command in /workspace and returns exit code + captured streams.
app.MapPost("/execute", async (ExecuteRequest req, CancellationToken ct) =>
{
    var psi = new ProcessStartInfo("/bin/bash", $"-lc \"{req.Command.Replace("\"", "\\\"")}\"")
    {
        WorkingDirectory = Workspace,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    using var proc = Process.Start(psi)!;
    var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
    var stderrTask = proc.StandardError.ReadToEndAsync(ct);
    await proc.WaitForExitAsync(ct);

    return Results.Ok(new ExecuteResponse(
        proc.ExitCode,
        Truncate(await stdoutTask),
        Truncate(await stderrTask)));
});

app.MapPost("/files/read", (FileRef req) =>
{
    var full = Resolve(req.Path);
    return full is null ? Results.BadRequest("Path escapes workspace.")
        : !File.Exists(full) ? Results.NotFound()
        : Results.Ok(new ReadResponse(File.ReadAllText(full)));
});

app.MapPost("/files/write", (WriteRequest req) =>
{
    var full = Resolve(req.Path);
    if (full is null) return Results.BadRequest("Path escapes workspace.");
    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
    File.WriteAllText(full, req.Content);
    return Results.Ok();
});

app.MapPost("/files/list", (FileRef req) =>
{
    var full = Resolve(string.IsNullOrEmpty(req.Path) ? "." : req.Path);
    if (full is null) return Results.BadRequest("Path escapes workspace.");
    if (!Directory.Exists(full)) return Results.NotFound();

    var entries = new DirectoryInfo(full)
        .EnumerateFiles("*", SearchOption.AllDirectories)
        .Select(f => new ListEntry(
            Path.GetRelativePath(Workspace, f.FullName),
            f.Length,
            f.LastWriteTimeUtc))
        .ToList();
    return Results.Ok(new ListResponse(entries));
});

app.Run();

// Confines a caller-supplied path to /workspace; returns null on traversal.
static string? Resolve(string path)
{
    var full = Path.GetFullPath(Path.Combine(Workspace, path));
    return full.StartsWith(Workspace + Path.DirectorySeparatorChar) || full == Workspace
        ? full : null;
}

static string Truncate(string s, int max = 64 * 1024) =>
    s.Length <= max ? s : s[..max] + $"\n…[truncated {s.Length - max} chars]";

record ExecuteRequest(string Command);
record ExecuteResponse(int ExitCode, string Stdout, string Stderr);
record FileRef(string Path);
record ReadResponse(string Content);
record WriteRequest(string Path, string Content);
record ListEntry(string Path, long SizeBytes, DateTime LastModified);
record ListResponse(IReadOnlyList<ListEntry> Files);
