# ESWMP — What To Do After You Change Code (Azure QA Deployment Workflow)

> **Companion to `QA-ENVIRONMENT-GUIDE.md`.** That document is about standing
> the QA environment up the *first* time. This one is about every time
> *after* that — you changed something (code, a database migration, a config
> value, added a whole new service) and need to know exactly what has to
> happen for that change to actually show up in Azure.
> **Written:** 2026-07-19, immediately after the QA environment's first
> confirmed-healthy live deployment. **Updated same day**: §9 of
> `QA-ENVIRONMENT-GUIDE.md` (GitHub Actions + OIDC) is now done and confirmed
> working — a full manual `gh workflow run deploy-qa.yml` completed all 12
> jobs successfully and the Gateway reported `200 Healthy` afterward. Every
> claim below reflects the actual deployed/configured state, not
> aspirational. **Updated again same day**: the automatic push-triggered path
> (§2a) was independently verified against a real combined code+Terraform
> commit — see the new gotcha in §8 about occasional transient OIDC-login
> failures on push-triggered runs.

---

## At a glance

| Question | Answer |
| --- | --- |
| Does anything deploy to Azure automatically right now? | **Yes, for code-only changes to the 5 existing services** — confirmed working live 2026-07-19 (§9 done: Entra ID app registration, federated OIDC credential, the 3 GitHub secrets, all in place). A push to `main` triggers `deploy-qa.yml`, which builds, pushes, and redeploys all 5 services automatically. |
| Can I make it automatic? | Already done — see above. If you're reading this on a *different* subscription/repo where §9 hasn't been run yet, this section still applies once you do. |
| Will infrastructure changes ever be automatic? | **No, never, by design.** `deploy-qa.yml` explicitly never runs `terraform apply` — it only rebuilds images and points *existing* Container Apps at them. A new service, a new database, a Terraform config change — all always need a human to run `terraform apply`. |
| What's the one command I'll run most often? | `terraform apply -var-file staging.tfvars` (space before the filename — see `QA-ENVIRONMENT-GUIDE.md` §7.2 for why the `=` form breaks on this machine). |

---

## Contents

1. [Decision tree — what did you change?](#1-decision-tree--what-did-you-change)
2. [Scenario A — code-only change to an existing service](#2-scenario-a--code-only-change-to-an-existing-service)
3. [Scenario B — a new database migration](#3-scenario-b--a-new-database-migration)
4. [Scenario C — a new service or a new database](#4-scenario-c--a-new-service-or-a-new-database)
5. [Scenario D — a Terraform config change](#5-scenario-d--a-terraform-config-change)
6. [Scenario E — a secret or environment variable change](#6-scenario-e--a-secret-or-environment-variable-change)
7. [Verifying any deploy](#7-verifying-any-deploy)
8. [Gotchas worth remembering](#8-gotchas-worth-remembering-confirmed-live-2026-07-19)

---

## 1. Decision tree — what did you change?

| You changed... | Automatic (§9 is set up)? | Go to |
| --- | --- | --- |
| Application code inside an existing service (`Eswmp.Core`/`Assignment`/`Rules`/`Work`/`Gateway`), no new DB tables, no new service | ✅ Yes — `git push` to `main` | [§2](#2-scenario-a--code-only-change-to-an-existing-service) |
| A new EF Core migration (schema change) in an existing service | ✅ Yes, same as above — the migration applies itself | [§3](#3-scenario-b--a-new-database-migration) |
| A brand-new backend service, or a new database for an existing service | ❌ No — always manual | [§4](#4-scenario-c--a-new-service-or-a-new-database) |
| Anything in `infrastructure/terraform/*.tf` (SKU sizes, new resources, security settings) | ❌ No — always manual | [§5](#5-scenario-d--a-terraform-config-change) |
| A secret value or environment variable (JWT secret, connection string, feature flag) | ❌ No — always manual | [§6](#6-scenario-e--a-secret-or-environment-variable-change) |

Not sure which bucket you're in? If your change touched any `.tf` file, it's
never fully automatic (D or C). If it only touched files under `src/`, it's
A or B.

---

## 2. Scenario A — code-only change to an existing service

### 2a. With automated deploy set up (§9 of `QA-ENVIRONMENT-GUIDE.md` done)

- [ ] Commit and push to `main` (or run `gh workflow run deploy-qa.yml --repo behrouzbk/eswmp` for an on-demand run without waiting for a push).
- [ ] Watch it: `gh run watch --repo behrouzbk/eswmp`, or the **Actions** tab.
- [ ] `deploy-qa.yml` runs `build.yml` as a gate first (unit tests must pass), then builds and pushes all 5 images (tagged with the commit SHA, not just `:latest` — see [§8](#8-gotchas-worth-remembering-confirmed-live-2026-07-19)), then updates all 5 Container Apps to that SHA-tagged image, sequentially, waiting for each to report healthy before moving to the next.
- [ ] Verify per [§7](#7-verifying-any-deploy).

### 2b. Without automated deploy (manual — what we actually did 2026-07-19)

- [ ] Build and push just the image(s) that changed (no need to redo all 5 if only one service changed):

  ```powershell
  cd C:\Workspace\eswmp
  az acr login --name eswmpstagingacr
  $pascal = "Work"   # match the service you changed: Gateway/Core/Assignment/Rules/Work
  docker build -f "src/Eswmp.$pascal/Dockerfile" -t "eswmpstagingacr.azurecr.io/eswmp-work:latest" .
  docker push "eswmpstagingacr.azurecr.io/eswmp-work:latest"
  ```

- [ ] Force a **new revision** with a unique suffix — plain `--image ...:latest` does **nothing** if the tag string is unchanged, even though the digest behind it changed (confirmed live 2026-07-19, see [§8](#8-gotchas-worth-remembering-confirmed-live-2026-07-19)):

  ```powershell
  $suffix = Get-Date -Format "MMddHHmmss"
  az containerapp update `
      --name eswmp-work-staging `
      --resource-group eswmp-staging-rg `
      --image "eswmpstagingacr.azurecr.io/eswmp-work:latest" `
      --revision-suffix "d$suffix"
  ```

- [ ] Verify per [§7](#7-verifying-any-deploy).

---

## 3. Scenario B — a new database migration

No extra deploy step beyond §2 — `Program.cs` in every service calls
`db.Database.MigrateAsync()` automatically on startup when
`ASPNETCORE_ENVIRONMENT` is `Staging` (which it always is in this
environment, set by `container_apps.tf`). Deploying the new image (§2) is
the whole story.

**What to actually watch for:** a broken migration crashes the *whole
service* on startup, not just the one endpoint touching the new table. Since
`min_replicas = 0`, you may not notice a broken migration until the first
real request after the deploy — check logs proactively rather than assuming
silence means success:

```powershell
az containerapp logs show --name eswmp-work-staging --resource-group eswmp-staging-rg --tail 50
```

Look for the `CREATE TABLE`/`ALTER TABLE` lines from your migration, and
confirm no exception follows them.

---

## 4. Scenario C — a new service or a new database

This always needs a human running `terraform apply` — no CI/CD path skips
this, by design (`deploy-qa.yml` only ever touches *existing* Container
Apps).

- [ ] Follow `QA-ENVIRONMENT-GUIDE.md` §10's structural checklist: add the new
      database to `local.service_databases` (`main.tf`) and/or the new
      service to `local.services` (`container_apps.tf`), give it a
      `Dockerfile` matching the existing 5's pattern.
- [ ] **Build and push the new service's image to ACR *before* running
      `terraform apply`** — since ACR already exists in this environment
      (unlike the very first bootstrap), this sidesteps the chicken-and-egg
      problem entirely: Terraform's `azurerm_container_app` resource
      creation tries to pull the referenced image as part of creating the
      Container App, and fails outright if nothing's there yet (confirmed
      live 2026-07-19 — see [§8](#8-gotchas-worth-remembering-confirmed-live-2026-07-19)).
      Push first, apply second.
- [ ] `terraform plan -var-file staging.tfvars` — read it, confirm it's only
      *adding* the new resources.
- [ ] `terraform apply -var-file staging.tfvars`.
- [ ] If you skipped the "push first" step and the apply fails on the new
      Container App with an image-pull error anyway: this is recoverable,
      not a disaster — see `QA-ENVIRONMENT-GUIDE.md` §7.2's detailed history
      note and §13's troubleshooting table for the exact recovery sequence
      (check `az containerapp list` for an orphaned broken app Azure created
      despite Terraform reporting failure, delete it, re-apply).
- [ ] If using automated CI/CD (§9 done): add the new service's short name to
      **both** matrix arrays in `deploy-qa.yml` (`build-and-push-images` and
      `deploy`), and its test project/database to `build.yml`, so future
      code-only changes to it also become automatic.
- [ ] Verify per [§7](#7-verifying-any-deploy).

---

## 5. Scenario D — a Terraform config change

Resource sizing, a new Azure resource type, a security setting, anything
that isn't a new service/database but still touches `.tf` files.

- [ ] `terraform plan -var-file staging.tfvars` — **always read the plan**
      before applying. Confirm it's doing what you expect, especially watch
      for anything showing as "destroy" that you didn't intend.
- [ ] `terraform apply -var-file staging.tfvars`.
- [ ] Verify per [§7](#7-verifying-any-deploy) if the change could plausibly
      affect a running service (e.g. a Key Vault access policy, a network
      rule); skip if it's purely cosmetic (e.g. a tag).

---

## 6. Scenario E — a secret or environment variable change

Rotating the JWT secret, changing a connection string, flipping a feature
flag baked into Terraform.

- [ ] Update the value in `staging.tfvars` (never commit this file — it's
      gitignored).
- [ ] `terraform apply -var-file staging.tfvars`.

  Unlike the manual `az containerapp update --image` case in §2b, a
  genuine configuration difference (a secret's actual value changing) *does*
  reliably trigger Terraform to create a new Container App revision on its
  own — confirmed live 2026-07-19 when the Key-Vault-reference-to-plain-value
  secret migration rolled out via a plain `terraform apply` with no explicit
  `--revision-suffix` needed. The `--revision-suffix` gotcha in §2b is
  specifically about the Azure CLI failing to detect an unchanged `:latest`
  tag string as a change — it doesn't apply here, since Terraform is
  submitting genuinely different content.
- [ ] Verify per [§7](#7-verifying-any-deploy) — this is a good scenario to
      always verify, since a bad secret value (e.g. a malformed connection
      string) will make the affected service crash-loop on start.

---

## 7. Verifying any deploy

The one command that tells you the whole system is actually working, not
just that Terraform/the CLI reported success:

```powershell
Invoke-RestMethod -Uri "https://eswmp-gateway-staging.bravehill-5af5160d.canadacentral.azurecontainerapps.io/health"
# Expect: "Healthy"
```

(Get the current URL yourself rather than trusting a hardcoded copy of it —
resource names don't change, but confirm with
`terraform output -raw gateway_url` from `infrastructure/terraform/`, or
`az containerapp show --name eswmp-gateway-staging --resource-group eswmp-staging-rg --query "properties.configuration.ingress.fqdn" --output tsv`.)

If it's not `Healthy` immediately: `min_replicas = 0` means the first
request after any deploy is a cold start for every service in the call
chain (Gateway's own `/health` calls each of the 4 backends' `/health/ready`
in turn) — retry 2-3 times, a few seconds apart, before concluding something
is actually broken. If it's still unhealthy after that, check the specific
service's logs:

```powershell
az containerapp logs show --name eswmp-<service>-staging --resource-group eswmp-staging-rg --tail 50
```

For anything log streaming doesn't explain clearly, query Container Apps'
system-level events (these name the *exact* reason a container failed to
even start, which console logs won't show if it never got that far):

```powershell
$workspaceId = az monitor log-analytics workspace show --resource-group eswmp-staging-rg --workspace-name eswmp-staging-logs --query customerId --output tsv
az monitor log-analytics query --workspace $workspaceId --analytics-query "ContainerAppSystemLogs_CL | where ContainerAppName_s == 'eswmp-<service>-staging' | order by TimeGenerated desc | take 30 | project TimeGenerated, Log_s, Reason_s" --output table
```

(First run needs `az extension add --name log-analytics --yes` once.)

---

## 8. Gotchas worth remembering (confirmed live 2026-07-19)

These are real bugs the team hit standing this environment up for the first
time — documented in full in `QA-ENVIRONMENT-GUIDE.md` and
`docs/DEVELOPMENT_STATUS.md`'s 2026-07-19 changelog entry. Summarized here
because they're exactly the kind of thing that resurfaces on a routine
deploy months from now when nobody remembers the original debugging session:

| Gotcha | Why it matters for routine deploys |
| --- | --- |
| `az containerapp update --image <unchanged-tag-string>` does nothing | Always use `--revision-suffix` for manual redeploys (§2b) — a re-pushed `:latest` with the same string silently keeps serving the *old* container. |
| A brand-new Container App fails if its image doesn't exist in ACR yet | Push images before `terraform apply` when adding a new service (§4) — Terraform's `create` call tries to pull the image immediately. |
| Gateway's routing uses the *stable* hostname (`ingress[0].fqdn`), not the revision-specific one | Already fixed in `container_apps.tf` — routing survives backend redeploys without needing Gateway itself re-applied. If you ever see this pattern reintroduced in a future change (a `latest_revision_fqdn` reference), that's a regression — routing will silently break on the next backend deploy. |
| Container App secrets are plain values, not Key Vault references | Deliberate, not an oversight — Key-Vault-referenced secrets never reliably resolved for a freshly-granted managed identity (see `QA-ENVIRONMENT-GUIDE.md`'s history note). If you're tempted to "clean this up" back to KV references, re-read that note first. |
| `terraform apply` failing partway through leaves real Azure resources that Terraform doesn't know about | Check `az containerapp list`/`az resource list --resource-group eswmp-staging-rg` after any failed apply before just retrying — a failed `apply` can still have created something. `QA-ENVIRONMENT-GUIDE.md` §13 has the exact recovery pattern. |
| A push-triggered `deploy-qa.yml` run occasionally fails at the **Azure login (OIDC)** step on exactly one of the parallel `Build & Push Images` matrix jobs, with `Login failed with Error: Using auth-type: SERVICE_PRINCIPAL. Not all values are present. Ensure 'client-id' and 'tenant-id' are supplied.` | Confirmed live 2026-07-19 — happened on 4 consecutive push-triggered runs (a different service's job each time), yet a `gh workflow run deploy-qa.yml` (`workflow_dispatch`) run for the exact same commit completed all 12 jobs cleanly right after. This looks like a transient GitHub Actions OIDC-secret-injection race on matrix jobs, not a real credential/Entra ID problem — don't start re-checking the federated credential or manually rebuilding images. Just re-run the failed job (Actions tab → **Re-run failed jobs**, or `gh run rerun <run-id> --failed`) or fire `gh workflow run deploy-qa.yml --repo behrouzbk/eswmp` instead, then verify per §7 same as any deploy. |
