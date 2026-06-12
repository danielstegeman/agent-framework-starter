// references/aca-sessions-sandbox.cs
//
// CLOUD implementation of ISandbox over Azure Container Apps dynamic sessions.
//
// FILE LOCATION: src/<Solution>.Tools.Sandbox/Aca/AcaSessionsSandbox.cs
//
// How it works:
//   - The session pool has a management endpoint. Anything in the path after
//     that endpoint is forwarded to the session's container on its target port.
//   - Requests are authenticated with a Microsoft Entra token whose audience is
//     https://dynamicsessions.io. The host's user-assigned managed identity must
//     hold the "Azure ContainerApps Session Executor" role on the pool
//     (assigned in azure-container-apps-sessions-bicep / agent-secrets-identity).
//   - The session is allocated automatically on first request for an identifier;
//     we pass the conversation id as that identifier.
//
// Credentials: DefaultAzureCredential => 'az login' locally, user-assigned
// managed identity in Azure. No keys anywhere. The SESSION itself never receives
// a token — only the host calls the management API.

using System.Net.Http.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sandbox.Aca;

public sealed class AcaSessionsOptions
{
    // Pool management endpoint, e.g.
    // https://<pool>.<envId>.<region>.azurecontainerapps.io
    [System.ComponentModel.DataAnnotations.Required]
    public required string PoolManagementEndpoint { get; init; }

    // Token audience for the management API. Constant for dynamic sessions.
    public string Audience { get; init; } = "https://dynamicsessions.io/.default";

    // The path the session-executor container exposes (see session-executor/Executor.cs).
    public string ApiVersion { get; init; } = "2025-10-02-preview";
}

public sealed class AcaSessionsSandbox(
    HttpClient http,
    TokenCredential credential,
    IOptions<AcaSessionsOptions> options) : ISandbox
{
    private readonly AcaSessionsOptions _opts = options.Value;

    public Task<ISandboxSession> GetOrCreateSessionAsync(string conversationId, CancellationToken ct)
        // Dynamic sessions allocate lazily on first request, so there is nothing
        // to pre-create. The session is materialised when the first command runs.
        => Task.FromResult<ISandboxSession>(new Session(this, conversationId));

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        var token = await credential.GetTokenAsync(
            new TokenRequestContext([_opts.Audience]), ct);
        return token.Token;
    }

    // Builds a management URL: <endpoint>/<path>?api-version=..&identifier=<convId>
    private Uri BuildUri(string path, string identifier) =>
        new($"{_opts.PoolManagementEndpoint.TrimEnd('/')}/{path.TrimStart('/')}" +
            $"?api-version={_opts.ApiVersion}&identifier={Uri.EscapeDataString(identifier)}");

    private async Task<T> PostAsync<T>(string path, string identifier, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, BuildUri(path, identifier))
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Authorization = new("Bearer", await GetTokenAsync(ct));
        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<T>(cancellationToken: ct))!;
    }

    private sealed class Session(AcaSessionsSandbox parent, string identifier) : ISandboxSession
    {
        public string Identifier => identifier;

        public async Task<CommandResult> ExecuteCommandAsync(string command, CancellationToken ct)
        {
            // The custom session container exposes /execute; the pool forwards the
            // path after the management endpoint into the session container.
            var r = await parent.PostAsync<ExecuteResponse>(
                "execute", identifier, new { command }, ct);
            return new CommandResult(r.ExitCode, r.Stdout, r.Stderr);
        }

        public async Task<string> ReadFileAsync(string path, CancellationToken ct)
        {
            var r = await parent.PostAsync<ReadResponse>(
                "files/read", identifier, new { path }, ct);
            return r.Content;
        }

        public Task WriteFileAsync(string path, string content, CancellationToken ct) =>
            parent.PostAsync<object>("files/write", identifier, new { path, content }, ct);

        public async Task<IReadOnlyList<FileEntry>> ListFilesAsync(string path, CancellationToken ct)
        {
            var r = await parent.PostAsync<ListResponse>(
                "files/list", identifier, new { path }, ct);
            return r.Files
                .Select(f => new FileEntry(f.Path, f.SizeBytes, f.LastModified))
                .ToList();
        }
    }

    // Response DTOs returned by the session-executor (see session-executor/Executor.cs).
    private sealed record ExecuteResponse(int ExitCode, string Stdout, string Stderr);
    private sealed record ReadResponse(string Content);
    private sealed record ListResponse(IReadOnlyList<ListEntry> Files);
    private sealed record ListEntry(string Path, long SizeBytes, DateTimeOffset LastModified);
}
