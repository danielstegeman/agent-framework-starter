---
name: agent-infrastructure-overview
description: Explain the infrastructure options for hosting a code-first agent service — managed Foundry Hosted Agents, self-hosted Azure Container Apps, App Service, Azure Functions, containerisation, registry, runtime identity, secrets, observability wiring, deployment pipeline options, environment promotion, and networking — and route to the right leaf skill (`azure-container-apps-bicep`, `azure-devops-pipelines-for-agents`, `dotnet-aspire-apphost`) when implementation details are needed. Use this skill when the user asks "how do I deploy an agent", "what infra do I need for my agent", "how should I host this in Azure", "what's the deployment story", or any equivalent — and you need to cover the *what* before diving into Bicep or YAML.
---

# Agent Infrastructure — Overview

What a code-first agent service needs at the infrastructure layer, which parts are optional, and which leaf skill to invoke for each piece.

## First decision: managed or self-hosted

Start by choosing who operates the runtime:

| Option | Pick when | Tradeoff |
|---|---|---|
| **Foundry Hosted Agents** | You can use the managed Foundry Agent Service runtime and want Microsoft to handle scaling, session lifecycle, and much of the platform integration. | Skips most Container Apps, ACR, and custom pipeline infrastructure; less control over custom HTTP hosting and sidecars. |
| **Azure Container Apps** | You want a self-hosted container with HTTP endpoints, background workers, KEDA scaling, revision control, or custom networking. | You operate the container, registry, identity, pipeline, observability, and environment promotion. |
| **App Service** | You already standardise on App Service and need a straightforward web app host more than container-level control. | Simpler hosting, less event-driven/container-native flexibility than Container Apps. |
| **Azure Functions** | The agent is primarily event-driven or needs durable workflow execution with serverless operations. | Best for trigger-oriented work; less natural for always-on custom web APIs. |
| **AKS** | You already operate Kubernetes and need cluster-level control. | Highest operational burden; do not introduce it just for an agent. |

This repo's implementation leaf skills focus on the self-hosted **Azure Container Apps** path because the Bicep and pipeline YAML are reusable reference assets. If the user chooses Foundry Hosted Agents, record that most of this infra is intentionally unnecessary. If they choose App Service, Azure Functions, or AKS, state the choice and route to a broader Azure deployment skill.

## Self-hosted checklist

If the user chooses a self-hosted agent, walk through these items in order.

### 1. Container image

Package the agent as a container when the host supports containers. Use a multi-stage Dockerfile, a non-root runtime user, and a runtime image without the SDK. Keep health endpoints distinct: `/health/live` for process liveness and `/health/ready` for downstream readiness.

### 2. Container registry

Azure Container Registry is a recommended option for Azure-hosted containers. One registry per environment family or one shared registry with repository-level scoping are both valid; choose based on isolation requirements.

Pull authentication: **user-assigned managed identity** attached to the workload. No admin user, no service principals with passwords.

### 3. Runtime identity

User-assigned managed identity (UAMI) is the default for self-hosted Azure workloads. One per logical app.

The UAMI must have least-privilege roles for:
- Pulling the image from ACR.
- Calling the Azure AI Foundry project/model endpoint (`Cognitive Services User` or a more specific provider role when applicable).
- Reading Key Vault secrets if using Key Vault references.
- Any data-plane access needed by tools, such as `Reader` for resource lookup tools.

Hand off to `azure-rbac` for least-privilege role selection per tool.

### 4. Secrets & configuration

Two-layer strategy:
- **App settings**: non-sensitive config such as `AZURE_AI_PROJECT_ENDPOINT`, `AZURE_AI_MODEL_DEPLOYMENT_NAME`, endpoint URLs, and log levels.
- **Key Vault references**: sensitive values such as App Insights connection strings and downstream API keys. Resolve them with the workload UAMI.

The agent's runtime identity is the credential — there are no secrets for the agent itself.

Defer to `agent-secrets-identity` for managed identity, federation, and On-Behalf-Of patterns.

### 5. Observability wiring

The runtime should export traces and logs. Two common configurations:
- **Production**: Azure Monitor exporter, connection string from Key Vault reference.
- **Local dev**: OTLP exporter to the Aspire dashboard or a local collector.

Recommended container env vars:
- `APPLICATIONINSIGHTS_CONNECTION_STRING` (secretRef)
- `OTEL_SERVICE_NAME` (= app name)
- `OTEL_RESOURCE_ATTRIBUTES` (= `environment=<env>,version=<image-tag>`)

See the native OpenTelemetry guidance in the `maf-csharp-implementation` skill (in the **maf-core** package), the [MAF observability docs](https://learn.microsoft.com/en-us/agent-framework/agents/observability), and `appinsights-instrumentation`.

### 6. Hosting platform

Do not present Container Apps as mandatory. It is the recommended self-hosted option in this repo when the agent is containerised and needs HTTP/background hosting. Foundry Hosted Agents, App Service, Azure Functions, and AKS remain valid alternatives depending on the first decision.

### 7. Deployment pipeline

A self-hosted container pipeline usually performs build, test, image build, registry push, infrastructure deploy, and app revision update.

Azure DevOps pipelines are one supported option in this repo (`azure-devops-pipelines-for-agents`). GitHub Actions is also valid when the repository and environment governance are GitHub-based. In both cases, use workload identity federation — no service principal secrets.

### 8. Environment promotion

`dev` -> `test` -> `prod`. Each environment commonly has its own:
- Resource group
- Hosting environment, such as a Container Apps environment
- Azure AI Foundry project/model deployment or approved shared Foundry project
- Key Vault
- App Insights workspace
- CI/CD environment configuration (ADO variable group or GitHub environment variables/secrets)

Promotion = re-deploy the same image tag with environment-specific parameters. No per-env code branches.

### 9. Local developer experience

The same container should run locally. Use .NET Aspire AppHost to orchestrate the agent and dependencies with a trace dashboard when it fits the project. -> `dotnet-aspire-apphost`.

### 10. Networking (optional)

Default for self-hosted HTTP agents: public ingress with TLS at the platform edge. If the agent is internal:
- Use private ingress where the chosen host supports it.
- Prefer private endpoints to Foundry/Azure OpenAI, Key Vault, and ACR.
- Restrict egress when the agent is limited to known external services.

### 11. Code-execution sandbox (only if the agent runs model-generated code)

**Skip this unless the agent executes model-generated code or commands** (see `agent-sandboxing` for the "is a sandbox required?" gate — typed-tool-only agents don't need one). When required on the self-hosted Container Apps path, the sandbox is a **separate runtime** from the host: an Azure Container Apps **dynamic-sessions pool** of Hyper-V-isolated, custom-container sessions that the host allocates per conversation.

- The host's UAMI needs the `Azure ContainerApps Session Executor` role on the pool; the session itself stays credential-less. -> `agent-secrets-identity`.
- The session image is built and pushed to ACR before the pool is created (two-phase deploy). -> `agent-sandbox-csharp` for the image + C# client.
- The pool resource, scaling (`maxConcurrentSessions` / `readySessionInstances` / cooldown), and egress isolation. -> `azure-container-apps-sessions-bicep`.

If the user picked Foundry Hosted Agents or a provider-hosted code interpreter, verify whether a custom sandbox pool is still needed before provisioning one.

## Hand-off

Once the checklist is walked:
- Managed Foundry runtime -> record the decision; most ACA/ACR/pipeline infra is skipped.
- Self-hosted HTTP/background agent on Azure Container Apps -> `azure-container-apps-bicep` for IaC; `azure-devops-pipelines-for-agents` if Azure DevOps is the CI/CD choice.
- GitHub-based CI/CD -> use the same workload identity and Bicep concepts, but express them in GitHub Actions.
- Local first -> `dotnet-aspire-apphost`.
- Auth detail -> `agent-secrets-identity`, `azure-rbac`.
- Code-execution sandbox -> `azure-container-apps-sessions-bicep` (IaC) + `agent-sandbox-csharp` (C#).
- Functions / App Service / generic Azure deploy -> `azure-prepare`.

Do not produce Bicep or YAML in this skill — that's the leaves' job.

## Official Documentation

- [Agent Framework hosting overview](https://learn.microsoft.com/en-us/agent-framework/hosting/)
- [Foundry Agent Service overview](https://learn.microsoft.com/en-us/azure/foundry/agents/overview)
- [Azure Container Apps overview](https://learn.microsoft.com/en-us/azure/container-apps/overview)
- [Azure App Service overview](https://learn.microsoft.com/en-us/azure/app-service/overview)
- [Azure Functions hosting for Agent Framework](https://learn.microsoft.com/en-us/agent-framework/hosting/azure-functions)
- [Azure Container Registry overview](https://learn.microsoft.com/en-us/azure/container-registry/container-registry-intro)
- [Azure Monitor OpenTelemetry exporter](https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-enable?tabs=net)
