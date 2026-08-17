---
name: azure-devops-pipelines-for-agents
description: Author Azure DevOps YAML pipelines as one CI/CD option for building and deploying a code-first agent service to Azure Container Apps — dotnet build/test, container build/push to ACR, Bicep deployment, environment promotion via variable groups, and workload identity federation for the service connection. Use this skill when the user asks "create an ADO pipeline for my agent", "build pipeline for my container app agent", "deploy my agent via Azure DevOps", "set up CI/CD for my MAF agent", or mentions azure-pipelines.yml in the context of an agent project. If the user uses GitHub Actions or managed Foundry Hosted Agents, treat this as an alternative rather than a mandate.
---

# Azure DevOps Pipelines for Agents

Author the build + deploy pipeline for a containerised code-first agent on Azure Container Apps when **Azure DevOps** is the chosen CI/CD system. Reference template: [references/azure-pipelines.yml](references/azure-pipelines.yml).

## When to use

- New agent repo needs CI/CD in Azure DevOps.
- Existing ADO pipeline doesn't yet build/test, validate the container, push to ACR, or deploy Bicep.
- Add a new environment (e.g. `test`) to an existing ADO pipeline.

Do **not** imply Azure DevOps is mandatory. GitHub Actions can run the same build, OIDC login, ACR push, and Bicep deployment flow when the repo and approvals live in GitHub. If the user picked Foundry Hosted Agents, confirm whether any app container pipeline is still needed.

## Output

Place at `azure-pipelines.yml` at repo root. One file per agent repo.

## Required up front

- **ACR**: name + resource group.
- **Container Apps environment** + target app name per environment.
- **Resource group** per environment.
- **Azure AI Foundry project endpoint** and model deployment name per environment (`AZURE_AI_PROJECT_ENDPOINT`, `AZURE_AI_MODEL_DEPLOYMENT_NAME`).
- **ADO service connection** (Azure RM) using **workload identity federation** — no client secrets. Set up once per ADO project; the same connection covers all environments when governance allows it.
- **Variable groups** named `agent-<env>` containing at minimum: `serviceConnection`, `acrName`, `resourceGroup`, `acaEnvName`, `acaAppName`, `keyVaultName`, `aiSecretName`, `azureAiProjectEndpoint`, `azureAiModelDeploymentName`.
- **Environments** in ADO (`agent-dev`, `agent-test`, `agent-prod`) so approvals/checks attach to deploys.

## Structure

Two stages: `Build` and `Deploy`.

### Build
1. `dotnet restore` / `build` / `test` (publish TRX results).
2. Build the container image for all runs, including PR validation.
3. Push image tags to ACR only when the run is not a pull request.

### Deploy
- Gated on `eq(variables['Build.SourceBranch'], 'refs/heads/main')`.
- `deployment:` job bound to the ADO environment so approvals fire.
- Single step: `az deployment group create` against `infra/container-apps.bicep` with environment-specific parameters from the variable group.

## Rules

- **No secrets in the pipeline.** Use Key Vault refs from Bicep into the Container App; the pipeline never sees the values.
- **One image, many environments.** Build once, deploy the same tag to dev -> test -> prod. Different parameters, same image.
- **Fail fast on tests.** Tests run *before* the container push to avoid wasting time and registry storage.
- **Use a service connection per subscription, not per environment.** Environments are how you gate; service connections are how you authenticate.
- **Workload identity federation.** When creating the service connection, choose "Workload Identity federation (automatic)". This removes the need for client secret rotation entirely.
- **Pipeline lint.** Run a pipeline smoke test after first creation to confirm wiring; or use `pipeline-yaml-review` skill for review.

## PR validation

For PR builds, omit the deploy stage by branch condition. PR builds should still build the container to catch Dockerfile regressions, but skip the push. The reference YAML does this by splitting Docker build from ACR push and adding a PR condition to the push task.

## Variable groups — secrets vs vars

Put **non-secret** infra coordinates (RG names, env names, ACR name, Foundry project endpoint, deployment name) in the variable group as plain variables. Put **the App Insights connection string secret name** (not the value) in the group — the value lives in Key Vault and is fetched at deploy time by Bicep.

If you must reference a secret from the pipeline itself, link the variable group to a Key Vault — never hard-code secrets in YAML.

## Hand-off

- The Bicep file the pipeline deploys -> `azure-container-apps-bicep`.
- The runtime identity the pipeline creates / wires -> `agent-secrets-identity`.
- Foundry project endpoint and model deployment provisioning -> `foundry-model-deployment`.
- General ADO YAML review -> `pipeline-yaml-review`.
- Generic IaC review -> `infrastructure-review`.
- GitHub Actions implementation -> adapt the same steps with federated `azure/login` rather than this ADO YAML.

## Official Documentation

- [Azure Pipelines YAML schema reference](https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema)
- [Workload identity federation service connections (Azure DevOps)](https://learn.microsoft.com/en-us/azure/devops/pipelines/release/configure-workload-identity)
- [AzureCLI@2 task reference](https://learn.microsoft.com/en-us/azure/devops/pipelines/tasks/reference/azure-cli-v2)
