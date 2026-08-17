// references/local-docker-sandbox.cs
//
// LOCAL (development) implementation of ISandbox over the Docker Engine API,
// using Docker.DotNet. Runs the SAME session-container image the cloud uses, so
// local behaviour matches production: per-conversation container, allocate on
// demand, idle cooldown eviction, hard cleanup on dispose.
//
// FILE LOCATION: src/<Solution>.Tools.Sandbox/Local/LocalDockerSandbox.cs
//
// Why Docker.DotNet (not a test-harness library): it speaks the Engine API
// directly, so we get honest container lifecycle semantics — the same shape the
// dynamic-sessions pool gives us — without pulling test-only framing into a
// production-shaped abstraction.
//
// This is a DEV runtime. It does not provide Hyper-V isolation; it exists so F5
// works against the real image and the real ISandbox contract.

using System.Collections.Concurrent;
using System.Net.Http.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Options;

namespace Sandbox.Local;

public sealed class LocalDockerOptions
{
    // Image the session runs. SAME image as the cloud pool (built from
    // session-executor/Dockerfile and pushed to ACR; pulled locally too).
    public string Image { get; init; } = "agent-session-executor:dev";

    // Port the executor listens on inside the container (see Executor.cs).
    public int ExecutorPort { get; init; } = 8080;

    // Evict a session after this much idle time, mirroring the pool cooldown.
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMinutes(5);
}

public sealed class LocalDockerSandbox : ISandbox, IAsyncDisposable
{
    private readonly DockerClient _docker = new DockerClientConfiguration().CreateClient();
    private readonly LocalDockerOptions _opts;
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, LocalSession> _sessions = new();

    public LocalDockerSandbox(IOptions<LocalDockerOptions> options, HttpClient http)
    {
        _opts = options.Value;
        _http = http;
    }

    public async Task<ISandboxSession> GetOrCreateSessionAsync(string conversationId, CancellationToken ct)
    {
        if (_sessions.TryGetValue(conversationId, out var existing) && !existing.IsExpired(_opts.Cooldown))
        {
            existing.Touch();
            return existing;
        }

        // Evict an expired one if present before re-creating.
        if (existing is not null)
        {
            await existing.DisposeAsync();
            _sessions.TryRemove(conversationId, out _);
        }

        var session = await StartContainerAsync(conversationId, ct);
        _sessions[conversationId] = session;
        return session;
    }

    private async Task<LocalSession> StartContainerAsync(string conversationId, CancellationToken ct)
    {
        var create = await _docker.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = _opts.Image,
            // Run as the non-root user baked into the image; no extra capabilities.
            HostConfig = new HostConfig
            {
                PublishAllPorts = true,
                // Local approximations of the cloud caps. The pool enforces the
                // real limits in production.
                Memory = 512L * 1024 * 1024,
                NanoCPUs = 1_000_000_000, // 1 vCPU
                NetworkMode = "none",     // no egress by default, like the pool
            },
            ExposedPorts = new Dictionary<string, EmptyStruct>
            {
                [$"{_opts.ExecutorPort}/tcp"] = default
            },
        }, ct);

        await _docker.Containers.StartContainerAsync(create.ID, new ContainerStartParameters(), ct);

        var inspect = await _docker.Containers.InspectContainerAsync(create.ID, ct);
        var hostPort = inspect.NetworkSettings.Ports[$"{_opts.ExecutorPort}/tcp"][0].HostPort;
        var baseUri = new Uri($"http://localhost:{hostPort}");

        var session = new LocalSession(conversationId, create.ID, baseUri, _docker, _http);
        await session.WaitUntilReadyAsync(ct);
        return session;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var s in _sessions.Values)
            await s.DisposeAsync();
        _sessions.Clear();
        _docker.Dispose();
    }

    private sealed class LocalSession(
        string identifier,
        string containerId,
        Uri baseUri,
        DockerClient docker,
        HttpClient http) : ISandboxSession, IAsyncDisposable
    {
        private DateTimeOffset _lastUsed = DateTimeOffset.UtcNow;

        public string Identifier => identifier;
        public bool IsExpired(TimeSpan cooldown) => DateTimeOffset.UtcNow - _lastUsed > cooldown;
        public void Touch() => _lastUsed = DateTimeOffset.UtcNow;

        public async Task WaitUntilReadyAsync(CancellationToken ct)
        {
            // Poll the executor health endpoint until it responds or we cancel.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    using var res = await http.GetAsync(new Uri(baseUri, "health"), ct);
                    if (res.IsSuccessStatusCode) return;
                }
                catch (HttpRequestException) when (!ct.IsCancellationRequested) { }
                await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Min(attempt + 1, 10)), ct);
            }
        }

        public async Task<CommandResult> ExecuteCommandAsync(string command, CancellationToken ct)
        {
            Touch();
            var res = await PostAsync<ExecuteResponse>("execute", new { command }, ct);
            return new CommandResult(res.ExitCode, res.Stdout, res.Stderr);
        }

        public async Task<string> ReadFileAsync(string path, CancellationToken ct)
        {
            Touch();
            return (await PostAsync<ReadResponse>("files/read", new { path }, ct)).Content;
        }

        public Task WriteFileAsync(string path, string content, CancellationToken ct)
        {
            Touch();
            return PostAsync<object>("files/write", new { path, content }, ct);
        }

        public async Task<IReadOnlyList<FileEntry>> ListFilesAsync(string path, CancellationToken ct)
        {
            Touch();
            var r = await PostAsync<ListResponse>("files/list", new { path }, ct);
            return r.Files.Select(f => new FileEntry(f.Path, f.SizeBytes, f.LastModified)).ToList();
        }

        private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
        {
            using var res = await http.PostAsJsonAsync(new Uri(baseUri, path), body, ct);
            res.EnsureSuccessStatusCode();
            return (await res.Content.ReadFromJsonAsync<T>(cancellationToken: ct))!;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await docker.Containers.RemoveContainerAsync(
                    containerId, new ContainerRemoveParameters { Force = true });
            }
            catch (DockerApiException) { /* already gone */ }
        }

        private sealed record ExecuteResponse(int ExitCode, string Stdout, string Stderr);
        private sealed record ReadResponse(string Content);
        private sealed record ListResponse(IReadOnlyList<ListEntry> Files);
        private sealed record ListEntry(string Path, long SizeBytes, DateTimeOffset LastModified);
    }
}
