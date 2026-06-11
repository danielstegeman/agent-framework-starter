---
name: agent-infrastructure-overview
description: Explain the infrastructure concerns for hosting a code-first agent service — containerisation, registry, runtime identity, secrets, observability wiring, deployment pipeline, environment promotion, and networking — and route to the right leaf skill (`azure-container-apps-bicep`, `azure-devops-pipelines-for-agents`, `dotnet-aspire-apphost`) for the implementation. Use this skill when the user asks "how do I deploy an agent", "what infra do I need for my agent", "how should I host this in Azure", "what's the deployment story", or any equivalent — and you need to cover the *what* before diving into Bicep or YAML.
---

# Agent Infrastructure — Overview

What every code-first agent service needs at the infrastructure layer, why, and which leaf skill to invoke for each piece.

## The checklist

Every hosted agent needs all of these. Walk through them in order.

### 1. Container image

The agent is packaged as a container, regardless of host. Multi-stage Dockerfile, non-root user, no SDK in the runtime image.

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/<Agent>.Host -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
ENTRYPOINT ["dotnet", "<Agent>.Host.dll"]
```

Health endpoints: `/health/live` (process up) and `/health/ready` (downstream reachable).

### 2. Container registry

Azure Container Registry by default. One registry per environment family (dev/test/prod) or one shared registry with repository-level scoping — pick based on isolation requirements.

Pull authentication: **user-assigned managed identity** attached to the workload. No admin user, no service principals with passwords.

### 3. Runtime identity

User-assigned managed identity (UAMI) is the default. One per app.

The UAMI must have:
- `AcrPull` on the registry.
- `Cognitive Services OpenAI User` on the Azure OpenAI resource.
- `Key Vault Secrets User` on the Key Vault (if using KV references).
- Any data-plane role needed by tools (e.g. `Reader` on the subscription for resource lookups).

Hand off to `azure-rbac` for least-privilege role selection per tool.

### 4. Secrets & configuration

Two-layer strategy:
- **App settings**: non-sensitive config (endpoint URLs, deployment names, log levels) as environment variables.
- **Key Vault references** for anything sensitive (App Insights connection string, downstream API keys). Resolved by the host at startup via UAMI.

The agent's runtime identity (UAMI) is the credential — there are no secrets for the agent itself.

Defer to `agent-secrets-identity` for the federation + OBO patterns.

### 5. Observability wiring

The container must export traces and logs. Two configurations:
- **Production**: Azure Monitor exporter, connection string from Key Vault ref.
- **Local dev**: OTLP exporter to the Aspire dashboard or a local Jaeger.

Required env vars on the container:
- `APPLICATIONINSIGHTS_CONNECTION_STRING` (secretRef)
- `OTEL_SERVICE_NAME` (= app name)
- `OTEL_RESOURCE_ATTRIBUTES` (= `environment=<env>,version=<image-tag>`)

See the `otel-azuremonitor.cs` reference cited by the `maf-csharp-implementation` skill (in the **dotnet-implementation** package) and `appinsights-instrumentation`.

### 6. Hosting platform — pick one

| | Pick when |
|---|---|
| **Azure Container Apps** (default) | HTTP API, background jobs, both. Autoscale to zero. KEDA-aware. -> `azure-container-apps-bicep` |
| **App Service** | Existing App Service investment, no need for container-level control. |
| **Azure Functions** | Pure event-driven, per-trigger billing, sub-minute runs. |
| **AKS** | You already operate K8s and have a platform team. |

The default leaf skill in this repo covers Container Apps. If the user picks something else, state that explicitly and either reach for the appropriate Azure skill (`azure-prepare`) or document the gap as a follow-up.

### 7. Deployment pipeline

- Build, test, container image build, push to ACR, infrastructure deploy, app revision update.
- One pipeline per repo. Stage gating on environment.
- Workload identity federation between the CI system and Azure (no service principal secrets in the pipeline).

Default: Azure DevOps pipelines. -> `azure-devops-pipelines-for-agents`.

### 8. Environment promotion

`dev` -> `test` -> `prod`. Each environment has its own:
- Resource group
- Container Apps environment (or sharing one with strict per-app config)
- Azure OpenAI deployment (different model versions per env is normal)
- Key Vault
- App Insights workspace
- ADO variable group (`agent-dev`, `agent-test`, `agent-prod`)

Promotion = re-deploy the same image tag with environment-specific parameters. No per-env code branches.

### 9. Local developer experience

The same container should run on F5. Use .NET Aspire AppHost to orchestrate the agent + its dependencies locally with a trace dashboard. -> `dotnet-aspire-apphost`.

### 10. Networking (optional)

Default: public ingress, TLS terminated at Container Apps. If the agent is internal:
- Private ingress on the Container Apps env (requires VNet integration).
- Private endpoints to Azure OpenAI, Key Vault, ACR.
- Allow-list egress if the agent is restricted to specific external services.

## Hand-off

Once the checklist is walked:
- HTTP agent on Azure -> `azure-container-apps-bicep` for IaC, `azure-devops-pipelines-for-agents` for CI/CD.
- Local first -> `dotnet-aspire-apphost`.
- Auth detail -> `agent-secrets-identity`, `azure-rbac`.
- Functions / App Service / generic Azure deploy -> `azure-prepare`.

Do not produce Bicep or YAML in this skill — that's the leaves' job.
