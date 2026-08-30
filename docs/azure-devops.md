# Azure DevOps pipeline

`azure-pipelines.yml` at the repo root, with reusable step/stage templates under
`.azuredevops/templates/`. It runs on every push to `main` and on every PR
targeting `main`. The **required PR check** is the GitHub Actions workflow
([below](#github-actions-pr-gate)); this pipeline runs the same restore / build /
test on PRs and additionally packages and deploys on `main`.

Spec §52. **No secrets or connection strings live in YAML.**

---

## Stage graph

```
Build ─┬─► UnitTests ────────┐
       ├─► IntegrationTests ──┼─► Coverage ─┐
       └─► Security ──────────┼─────────────┼─► Package ─► Deploy_Development ─► Deploy_Test ─► Deploy_Staging ─► Deploy_Production
                              └─────────────┘   (main only)   (all Deploy_* inert until deployEnabled = 'true')
```

| Stage | Job(s) | Template | Does |
|---|---|---|---|
| **Build** | `build` | `build-steps.yml` | Install SDK (`global.json`), NuGet cache, `dotnet restore`, `dotnet build -c Release --no-restore` (warnings-as-errors). |
| **UnitTests** | `unit` | `test-steps.yml` | `dotnet test` for `CTMS.Application.Tests` + `CTMS.Client.Tests`, `--collect:"XPlat Code Coverage"`, publish `.trx` + Cobertura, publish `coverage-unit` artifact. |
| **IntegrationTests** | `integration` | `test-steps.yml` | `dotnet test` for `CTMS.Api.IntegrationTests` (Testcontainers `mongo:7` on the Docker-capable `ubuntu-latest` agent), coverage, `coverage-integration` artifact. |
| **Coverage** | `coverage` | `coverage-steps.yml` | Download `coverage-unit` + `coverage-integration`, merge with ReportGenerator into one Cobertura + HTML, publish combined coverage + `coverage-combined` artifact. |
| **Security** | `scan` | `security-steps.yml` | `dotnet list <shipping project> package --vulnerable --include-transitive` for `CTMS.Api` / `CTMS.AdminUI` / `CTMS.Client`; **fails on a vulnerable transitive**. Deprecated packages are advisory. Test projects are skipped on purpose (`NuGetAudit=false`). |
| **Package** | `package` | `package-steps.yml` | **`main` only.** `dotnet publish src/CTMS.Api` → `ctms-api` pipeline artifact; `docker buildAndPush` the root `Dockerfile` (context = repo root) to ACR, tags `$(Build.BuildNumber)` + `latest`. |
| **Deploy_Development** | `approval?` + `deploy_container_app` | `deploy-stage.yml` | `az deployment group create` against `deploy/azure/main.bicep` with `apiImage` = the just-pushed tag; smoke-test `GET /health/ready`. |
| **Deploy_Test** | ″ | ″ | as above, `dependsOn: Deploy_Development`. |
| **Deploy_Staging** | ″ | ″ | as above, `dependsOn: Deploy_Test`. |
| **Deploy_Production** | **`approval`** (`ManualValidation@0`, agentless) + `deploy_container_app` | ″ | as above, `dependsOn: Deploy_Staging`, with the in-pipeline manual approval gate. |

`Package` runs only on `main` (`condition: eq(variables.isMain, 'true')`). Every
`Deploy_*` stage is **inert** — `condition: eq('$(deploy<Env>)', 'true')` and the
`deploy<Env>` variables are all `'false'` in `azure-pipelines.yml`. Nothing under
`deploy/` needs to exist or run for the pipeline to be valid.

## Environments and the Production approval gate

The four environments — **Development / Test / Staging / Production** — are Azure
DevOps **Environments** (`Pipelines > Environments`), referenced by the
`deployment` job's `environment:` property (`ctms-development`, `ctms-test`,
`ctms-staging`, `ctms-production`).

The **Production approval gate** is enforced two ways:

1. **Azure DevOps Environment checks** — on `ctms-production`, add an *Approvals*
   check (and any branch-control / exclusive-lock checks). This is the primary
   gate and is configured in the ADO UI, not in YAML.
2. **In-pipeline `ManualValidation`** — `deploy-stage.yml` with
   `requireManualApproval: true` adds an agentless `approval` job
   (`ManualValidation@0`, 3-day timeout, reject-on-timeout) that the
   `deploy_container_app` job `dependsOn`. This means an approval step exists
   even before the Environment's checks are set up.

To turn a deployment on: set the matching `deploy<Env>` variable to `'true'`
(in `azure-pipelines.yml` or the `ctms-deploy` variable group), create the ADO
Environment, and populate the `ctms-deploy` / `ctms-acr` variable groups.

> `main.bicep`'s `environmentName` parameter only accepts `dev | test | prod`.
> `Deploy_Staging` passes `bicepEnvironmentName: test` as a stopgap (documented in
> the template) until the infra workstream adds a `staging` value.

## Variable groups (Pipelines > Library) — referenced, never defined in YAML

| Group | Keys | Used by |
|---|---|---|
| `ctms-ci` | shared CI knobs | all stages |
| `ctms-acr` | `acrServiceConnection` (Docker registry service connection name), `acrLoginServer` (e.g. `myregistry.azurecr.io`), `imageRepository` (e.g. `ctms/api`) | Package, Deploy_* |
| `ctms-deploy` | `azureServiceConnection` (ARM / workload-identity connection name), `resourceGroupDev` / `resourceGroupTest` / `resourceGroupStaging` / `resourceGroupProd` | Deploy_* only |

## Service connections

- **Docker registry** service connection (name in `ctms-acr.acrServiceConnection`)
  — used by `Docker@2 buildAndPush`.
- **ARM / workload-identity** service connection
  (`ctms-deploy.azureServiceConnection`) — used by `AzureCLI@2` in the deploy
  stages. Scope it to the target subscription / resource groups.

## Secrets

None in YAML. The Bicep resolves the Mongo and Redis connection strings from
**Key Vault references** at container-app runtime via the app's user-assigned
managed identity — `CtmsDatabase-ConnectionString`, `Redis-ConnectionString`
(see [`azure-deployment.md`](azure-deployment.md)). The API needs no Entra ID
client secret (it is a token validator). Seed the Key Vault secrets once after
the first deploy, then restart the container app revision.

## Local dry-run

`act` is for GitHub Actions, not Azure Pipelines. To validate `azure-pipelines.yml`
without a run:

```bash
# YAML well-formedness
ruby -ryaml -e "YAML.load_file('azure-pipelines.yml')"
# full expansion (needs an ADO org + the Azure CLI devops extension)
az pipelines runs create --branch main --open --dry-run   # or the "Validate" button in the ADO editor
```

---

## GitHub Actions PR gate

`.github/workflows/ci.yml` is the **required check** on `main`. It mirrors this
pipeline's `Build` + test stages at **solution scope**:

- `dotnet restore CTMS.sln` → `dotnet build CTMS.sln -c Release --no-restore`
  (warnings-as-errors) → `dotnet test CTMS.sln -c Release --no-build
  --collect:"XPlat Code Coverage"`, then publishes the `.trx` results as a PR
  check summary (`EnricoMi/publish-unit-test-result-action`).
- A separate non-blocking `format` job runs `dotnet format --verify-no-changes`.
- Actions pinned to major-version tags; NuGet + `~/.cache/ephemeral-mongo`
  cached; one in-flight run per ref.

Because it is **solution-scoped**, it automatically covers `CTMS.Client` (kept)
and carries no reference to the removed API-key / webhook test files — no change
was needed when those were deleted.
