# references/session-executor/Dockerfile
#
# The custom session-container image. This is what runs INSIDE each isolated
# session (Azure Container Apps dynamic session in the cloud, local Docker
# container in dev). It exposes a tiny HTTP API (see Executor.cs) that the host
# proxies into; it holds NO credentials and has NO network egress by default.
#
# Build once, push to ACR, reference from BOTH the session pool
# (azure-container-apps-sessions-bicep) and the local runtime — ONE image, so
# local behaviour matches production.
#
#   docker build -t <acr>.azurecr.io/agent-session-executor:1.0 -f Dockerfile .
#   az acr login -n <acr> && docker push <acr>.azurecr.io/agent-session-executor:1.0
#
# One image PER coding environment: if you need a Node/web toolchain and a
# Python/data toolchain, build two images (e.g. -node, -python) and two pools.
# Author the toolchain from your devcontainer definition where possible so dev
# and sandbox share one source of truth.

# --- build the minimal executor ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Executor.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app

# --- runtime: ASP.NET + the toolchain the agent's code needs ---
FROM mcr.microsoft.com/dotnet/aspnet:9.0
# Toolchain available to model-generated commands. Add language runtimes/CLIs
# this environment requires; keep it minimal to shrink attack surface.
RUN apt-get update \
    && apt-get install -y --no-install-recommends bash git ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Non-root user owns the workspace. The session never runs privileged.
RUN useradd --create-home --uid 10001 sandbox \
    && mkdir -p /workspace && chown sandbox:sandbox /workspace

COPY --from=build /app /app
WORKDIR /workspace
USER sandbox

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "/app/Executor.dll"]
