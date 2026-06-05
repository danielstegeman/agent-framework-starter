---
name: azure-devops-pipelines-for-agents
description: Author Azure DevOps YAML pipelines for building and deploying a code-first agent service to Azure Container Apps — dotnet build/test, container build/push to ACR, Bicep deployment, environment promotion via variable groups, and workload identity federation for the service connection. Use this skill when the user asks "create an ADO pipeline for my agent", "build pipeline for my container app agent", "deploy my agent via Azure DevOps", "set up CI/CD for my MAF agent", or mentions azure-pipelines.yml in the context of an agent project.
---

# Azure DevOps Pipelines for Agents

Author the build + deploy pipeline for a containerised code-first agent on Azure Container Apps. Reference template: [references/azure-pipelines.yml](../../references/azure-pipelines.yml).

## When to use

- New agent repo needs CI/CD.
- Existing pipeline doesn't yet build & push a container or doesn't deploy Bicep.
- Add a new environment (e.g. `test`) to an existing pipeline.

## Output

Place at `azure-pipelines.yml` at repo root. One file per agent repo.

## Required up front

- **ACR**: name + resource group.
- **Container Apps environment** + target app name per environment.
- **Resource group** per environment.
- **ADO service connection** (Azure RM) using **workload identity federation** — no client secrets. Set up once per ADO project; the same connection covers all environments.
- **Variable groups** named `agent-<env>` containing at minimum: `serviceConnection`, `acrName`, `resourceGroup`, `acaEnvName`, `acaAppName`, `keyVaultName`, `aiSecretName`.
- **Environments** in ADO (`agent-dev`, `agent-test`, `agent-prod`) so approvals/checks attach to deploys.

## Structure

Two stages: `Build` and `Deploy`.

### Build
1. `dotnet restore` / `build` / `test` (publish TRX results).
2. Build container with two tags: `$(Build.BuildId)` and `latest`.
3. Push to ACR via `az acr login` (uses the workload-identity service connection).

### Deploy
- Gated on `eq(variables['Build.SourceBranch'], 'refs/heads/main')`.
- `deployment:` job bound to the ADO environment (so approvals fire).
- Single step: `az deployment group create` against `infra/container-apps.bicep` with environment-specific parameters from the variable group.

## Rules

- **No secrets in the pipeline.** Use Key Vault refs from Bicep into the Container App; the pipeline never sees the values.
- **One image, many environments.** Build once, deploy the same tag to dev -> test -> prod. Different parameters, same image.
- **Fail fast on tests.** Tests run *before* the container build to avoid wasting time and registry storage.
- **Use a service connection per subscription, not per environment.** Environments are how you gate; service connections are how you authenticate.
- **Workload identity federation.** When creating the service connection, choose "Workload Identity federation (automatic)". This removes the need for client secret rotation entirely.
- **Pipeline lint.** Run `az pipelines runs list` smoke after first creation to confirm wiring; or use `pipeline-yaml-review` skill for review.

## PR validation

For PR builds, omit the deploy stage by branch condition (the template above already does this with `eq(...refs/heads/main)`). PR builds should still build the container to catch Dockerfile regressions, but skip the push (toggle on `$(Build.Reason) != 'PullRequest'`).

## Variable groups — secrets vs vars

Put **non-secret** infra coordinates (RG names, env names, ACR name) in the variable group as plain variables. Put **the App Insights connection string secret name** (not the value) in the group — the value lives in Key Vault and is fetched at deploy time by Bicep.

If you must reference a secret from the pipeline itself, link the variable group to a Key Vault — never hard-code secrets in YAML.

## Hand-off

- The Bicep file the pipeline deploys -> `azure-container-apps-bicep`.
- The runtime identity the pipeline creates / wires -> `agent-secrets-identity`.
- General ADO YAML review -> `pipeline-yaml-review`.
- Generic IaC review -> `infrastructure-review`.
