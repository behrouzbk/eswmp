# ESWMP — QA Environment Setup & Deployment Guide (Azure)

> **Audience:** you, doing this yourself, on Windows 11 with PowerShell 7+.
> **Goal of this pass:** set up the federated identity credential and run
> `terraform apply` to stand up the QA environment for the first time.
> **Supersedes:** `docs/QA_STAGING_INFRASTRUCTURE.md`, which described this
> same environment as of 2026-07-07 (before `Eswmp.Work` existed) and has a
> superseded-notice at its top pointing here.
> **Written:** 2026-07-17, reformatted 2026-07-18 — against the actual
> `infrastructure/terraform/` files in this repo and the actual
> `docker-compose.yml`/`appsettings.json` config every service reads. **If
> this document and the Terraform ever disagree, the Terraform is the source
> of truth.**
> **Every command below is PowerShell 7+.** Azure CLI (`az`) and Terraform
> behave identically across shells — only quoting differs from a bash
> equivalent, so don't copy commands from elsewhere without adjusting quotes.

---

## At a glance

| Question | Answer |
| --- | --- |
| Database engine? | **Real Azure Database for PostgreSQL — Flexible Server** (`azurerm_postgresql_flexible_server` in `main.tf`). Not Supabase, not any other third-party/managed Postgres host — this repo has zero references to Supabase anywhere. |
| Where does it run? | Azure Container Apps (Consumption plan) — not AKS, not App Service. See [§1](#1-executive-summary--the-decisions-and-why) for why. |
| What does QA cost realistically? | **~$40–$75/month**, pessimistic case ~$100–120/month. Full breakdown: [§2](#2-cost-breakdown-estimates--confirm-with-the-azure-pricing-calculator). |
| What's my credit? | $1,000, expires end of October 2026 (~3.5 months from 2026-07-17). $40–75/mo leaves very comfortable headroom. |
| How do I know I won't blow through it? | Four structural guardrails, all already in the Terraform (nothing left for you to configure beyond one required email address) — see the table below. |

**The four cost guardrails already built into `infrastructure/terraform/`:**

| Guardrail | What it does | Detail |
| --- | --- | --- |
| Cheapest Postgres SKU | `B_Standard_B1ms` Burstable, 32GB — ~$15–20/mo, no HA | [§2](#2-cost-breakdown-estimates--confirm-with-the-azure-pricing-calculator) |
| Container Apps scale-to-zero | All 5 services scale to **0 replicas** when idle — no 24/7 billing | [§3.2](#32-container-apps-scale-to-zero-for-non-prod-min_replicas) |
| Log Analytics daily cap | Ingestion hard-stops at 1 GB/day for non-prod | [§3.3](#33-log-analytics-daily-ingestion-cap-daily_quota_gb) |
| Consumption Budget alert | Email at 50/80/100% of a **$100/month** threshold | [§3.4](#34-budget-alert-budget_alert_email-monthly_budget_amount) |

Azure budgets **alert, they never block spend** — this is a tripwire, not a
hard limit. Treat a 100% alert as "stop and look," not background noise.

---

## Contents

0. [What "QA environment" means here](#0-what-qa-environment-means-here)
1. [Executive summary — the decisions and why](#1-executive-summary--the-decisions-and-why)
2. [Cost breakdown](#2-cost-breakdown-estimates--confirm-with-the-azure-pricing-calculator)
3. [What this pass changed in your Terraform](#3-what-this-pass-changed-in-your-terraform-already-done-reasoning-below)
4. [Prerequisites](#4-prerequisites-windows-11-powershell-7)
5. [One-time Azure account setup](#5-one-time-azure-account-setup)
6. [One-time Terraform remote state bootstrap](#6-one-time-terraform-remote-state-bootstrap)
7. [Configure and apply Terraform](#7-configure-and-apply-terraform-manual-deployment-part-1-infrastructure)
8. [Manual deployment (build, push, deploy)](#8-manual-deployment-part-2-build-push-and-deploy-the-5-services)
9. [Automated deployment — GitHub Actions OIDC](#9-automated-deployment--github-actions-with-oidc-no-stored-password)
10. [Extending this for new services later](#10-extending-this-when-new-servicesapisdatabases-are-added-later)
11. [Cost monitoring and shutdown practices](#11-cost-monitoring-and-shutdown-practices)
12. [Tearing down the whole environment](#12-tearing-down-the-whole-environment)
13. [Troubleshooting](#13-troubleshooting)
14. [Command cheat-sheet](#14-command-cheat-sheet)

**First time here?** Read §0–§3 once (context — what you're building and
why), then work through §4–§9 in order, checking off each box. §10–§14 are
reference material for later.

---

## 0. What "QA environment" means here

Your Terraform already has an `environment` variable that accepts `dev`,
`staging`, or `prod` (`infrastructure/terraform/variables.tf`). This repo's
existing convention (see the doc this one supersedes) already treats
**`environment = "staging"` as the shared QA/testing environment** — one
deployment, one resource group, used by you and your team to exercise
services before anything reaches a real tenant. This guide keeps that
convention rather than inventing a fourth `qa` value: everywhere it says "the
QA environment," the Terraform artifact is `-var environment=staging`, and
every resource name below (`eswmp-staging-rg`, `eswmp-core-staging`, etc.)
follows from that.

There is no `prod` environment yet (see `docs/DEVELOPMENT_STATUS.md`'s Phase
status) — this guide only covers standing up and operating QA/staging.

---

## 1. Executive summary — the decisions and why

You asked for a QA environment set up "very efficiently" against a **$1,000
startup credit that expires end of October 2026**. Two things shaped every
decision below:

- **`docs/Azure/azure-architecture-reference.md` is a checklist for a much
  bigger platform than you need for QA today.** It's written as a
  forward-looking reference for "a gateway platform" in general — AKS,
  Azure API Management, Entra ID app registrations, VNet integration. Its own
  opening line says so: "the specific SKUs, names, and sizes will differ per
  project." Standing up AKS + APIM Premium for a QA environment used by a
  handful of people would burn a meaningful fraction of $1,000 in **days**,
  not months, for capability this project doesn't need yet (no external
  partners calling in through APIM, no Kubernetes-specific workload). This
  guide deliberately does not follow that document's shape for QA. It's kept
  as the reference to revisit **if and when** ESWMP grows an actual external
  partner integration with APIM-shaped requirements (rate limiting per
  partner, a real edge policy layer) — not before.
- **Your Terraform already made the right call before this pass started**:
  `infrastructure/terraform/` provisions **Azure Container Apps**, not AKS,
  and there is no APIM anywhere in it — the existing `Eswmp.Gateway` (YARP)
  container **is** the edge, exposed as the one externally-reachable
  Container App. That was already the efficient choice; this pass tightened
  it further (see §3) rather than replacing it.

What you get for QA, mapped against the reference doc's four layers:

| Layer | What QA uses | Not used | Why |
| --- | --- | --- | --- |
| Edge | `Eswmp.Gateway` Container App, external ingress | Azure API Management | JWT validation + rate limiting already live in Gateway code (`Eswmp.Shared`) — APIM's "two independent gates" principle is already satisfied without a second product. |
| Compute | Azure Container Apps, Consumption plan | AKS | No cluster to size/patch/upgrade; scales each of the 5 services independently, down to **zero** replicas when idle ([§3.2](#32-container-apps-scale-to-zero-for-non-prod-min_replicas)). |
| Supporting services | Key Vault, Container Registry Basic | Entra ID app registrations | ESWMP validates a shared-secret JWT, it doesn't do OIDC (`docs/QA_STAGING_INFRASTRUCTURE.md` §4). |
| Observability | Log Analytics + Application Insights, daily ingestion cap | — | Prevents a noisy test run from quietly racking up log-ingestion charges ([§3.3](#33-log-analytics-daily-ingestion-cap-daily_quota_gb)). |

One thing the reference doc doesn't mention at all, added specifically
because of your credit deadline: an **Azure Consumption Budget with email
alerts at 50/80/100%** of a monthly threshold ([§3.4](#34-budget-alert-budget_alert_email-monthly_budget_amount)) — the single most
important guardrail for a shared, time-boxed credit.

> **A note on the screenshot you mentioned.** You referenced a screenshot
> (`sc103.jpg`) showing your Azure startup credit account. It isn't actually
> present in the `sc/` folder in this repo (only two older, unrelated
> Solution Explorer screenshots are there) — this guide proceeds on the
> numbers you stated in chat ($1,000 credit, expires October 2026) rather
> than anything read from that image. If the actual credit amount, expiry, or
> subscription type differs from what you told me, re-check [§2](#2-cost-breakdown-estimates--confirm-with-the-azure-pricing-calculator)'s budget
> number and [§5.3](#53-confirm-your-subscription-supports-cost-management-budgets)'s subscription-type caveat.

---

## 2. Cost breakdown (estimates — confirm with the Azure Pricing Calculator)

Region assumed: `canadacentral` (the existing Terraform default — change
`location` in `variables.tf` if your credit is anchored to a different
region; some startup-credit offers restrict which regions count against the
credit).

| Resource | SKU / config | Estimated cost | Notes |
| --- | --- | --- | --- |
| Container Registry | Basic tier | ~$5/mo flat | 10 GB included storage — far more than 5 small .NET images need. |
| PostgreSQL Flexible Server | Burstable `B_Standard_B1ms`, 32 GB | ~$15–20/mo compute + ~$4–5/mo storage | The one resource that supports being **stopped** (not deleted) when idle — [§11.2](#112-stop-postgresql-when-the-team-wont-be-testing-for-a-while). Storage cost continues while stopped; compute does not. |
| Service Bus | Standard tier | ~$10/mo base | Required (not Basic) — MassTransit's Azure Service Bus transport needs topics/subscriptions. Fixed cost regardless of publish volume. |
| Container Apps | Consumption plan, `min_replicas=0` | **$0–15/mo** | 180,000 vCPU-seconds + 360,000 GiB-seconds free per month, plus 2M requests. All 5 services scale to zero when idle, so intermittent team testing should stay near/within the free grant. |
| Log Analytics + App Insights | 1 GB/day cap (non-prod) | **$5–20/mo realistic, ~$50–75/mo worst case** | First 5 GB/mo per workspace free; beyond that ~$2–3/GB. Worst case = hitting the cap every single day (~30 GB/mo). |
| Key Vault | Per-operation billing | ~$0/mo | Billed per 10,000 operations, a few cents at this scale. |
| Cache for Redis | Not provisioned (`deploy_redis=false`) | $0/mo | Add ~$16/mo (Basic C0) back only once CO-11 actually ships and needs it — [§3.1](#31-redis-is-now-optional-deploy_redis-defaults-to-false). |

**Total realistic estimate: ~$40–$75/month.** Pessimistic case (Log
Analytics cap hit every day **and** Container Apps usage beyond the free
grant, simultaneously): **~$100–120/month**. Against a $1,000 credit over
~3.5 months, both cases leave comfortable headroom — the real risk isn't
baseline cost, it's an accidental AKS/APIM detour or an unbounded
log-ingestion spike, both of which this guide's guardrails close off.

---

## 3. What this pass changed in your Terraform (already done, reasoning below)

These four changes are already applied to
`infrastructure/terraform/{variables,main,container_apps,outputs}.tf` and
`staging.tfvars.example` — you don't need to make them yourself, but you do
need to understand them before running `terraform apply`, because one of
them introduces a new **required** variable.

### 3.1 Redis is now optional (`deploy_redis`, defaults to `false`)

`CO-11` ("Redis-backed slot search caching") is not implemented anywhere in
the codebase yet — nothing calls `IDistributedCache.Get`/`Set`. Provisioning
Azure Cache for Redis today would be ~$16/month spent on a cache nothing
reads or writes. `Eswmp.Core`'s health check for Redis is already conditional
on `Redis:ConnectionString` being set (`HealthCheckExtensions.cs`), so simply
not setting it is safe — the service starts and reports healthy without it.

> **Action needed from you:** none, unless/until CO-11 ships. When it does,
> set `deploy_redis = true` in `staging.tfvars` and re-apply.

### 3.2 Container Apps scale to zero for non-prod (`min_replicas`)

Previously hardcoded to `1` for every service in every environment — meaning
5 containers billed continuously, 24/7, even overnight when nobody's testing.
Now `var.environment == "prod" ? 1 : 0`. QA/staging containers cold-start on
the first request after being idle (a few seconds for a .NET container) —
acceptable for a shared test environment; not acceptable for a production
environment, which is why `prod` alone keeps a warm replica.

> **Action needed from you:** none — this is automatic based on
> `environment = "staging"`.

### 3.3 Log Analytics daily ingestion cap (`daily_quota_gb`)

Set to `1` GB/day for non-prod, `-1` (unlimited) for prod. Once the daily cap
is hit, ingestion **pauses until the next UTC day** — logs and traces stop
arriving rather than being throttled gracefully. If you're running an
intentional load/soak test and need more headroom for a day, raise this
temporarily in `main.tf` and re-apply, then lower it back.

> **Action needed from you:** none, unless you hit the cap during a real
> test session — see [§13](#13-troubleshooting)'s troubleshooting entry for what that looks like.

### 3.4 Budget alert (`budget_alert_email`, `monthly_budget_amount`)

A new `azurerm_consumption_budget_resource_group` resource, scoped to just
the QA resource group (not your whole subscription — so it won't be
confused by anything else on the same credit), with four notifications: 50%,
80%, 100% of actual spend, and a forecasted-100% warning. All four email
`var.budget_alert_email`.

> **Action needed from you:** `budget_alert_email` has **no default** — you
> must set it in `staging.tfvars`, or `terraform apply` will prompt for it
> interactively every time. `monthly_budget_amount` defaults to **`100`**
> (tightened from a starting default of 150, given realistic spend is
> $40–75/mo — see [§2](#2-cost-breakdown-estimates--confirm-with-the-azure-pricing-calculator)) — raise or lower it to whatever number you want
> the alert tripwire set at.

---

## 4. Prerequisites (Windows 11, PowerShell 7+)

- [ ] Install what you don't already have. `winget` ships with Windows 11:

  ```powershell
  winget install -e --id Microsoft.AzureCLI
  winget install -e --id Hashicorp.Terraform
  winget install -e --id Docker.DockerDesktop
  winget install -e --id Git.Git
  winget install -e --id GitHub.cli   # optional — used in §9.6 for a manual trigger
  ```

- [ ] Open a **new** PowerShell 7+ window after installing (so `PATH` picks
      up the new tools), then confirm:

  ```powershell
  az version
  terraform version
  docker version
  git --version
  gh --version   # if installed
  ```

- [ ] **Docker Desktop running** (for building images in §8).
- [ ] **An Azure subscription** — the startup-credit one you already created.
- [ ] **Owner or Contributor role** on that subscription (needed to create
      resource groups, role assignments, and the federated identity
      credential in §9).

---

## 5. One-time Azure account setup

### 5.1 Log in and confirm the right subscription

- [ ] ```powershell
      az login

      az account show --output table
      
      ```

  If you have more than one subscription/tenant, make sure the credit
  subscription is selected:

  ```powershell
  az account list --output table
  az account set --subscription "<subscription-id-or-name>"
  ```

### 5.2 Register the resource providers this project uses

A brand-new subscription sometimes has resource providers unregistered,
which fails `terraform apply` partway through with an opaque error.

- [ ] Register them all up front — this is idempotent and safe to run even
      if some are already registered:

  ```powershell
  $providers = @(
      "Microsoft.App",                  # Container Apps
      "Microsoft.ContainerRegistry",
      "Microsoft.DBforPostgreSQL",
      "Microsoft.ServiceBus",
      "Microsoft.OperationalInsights",  # Log Analytics
      "Microsoft.Insights",             # Application Insights
      "Microsoft.KeyVault",
      "Microsoft.Cache",                # Redis — register even though deploy_redis defaults false, cheap to have ready
      "Microsoft.Consumption",          # Budgets
      "Microsoft.Storage"                # §6's tfstate bootstrap storage account — nothing else here uses Storage, easy to miss
  )
  foreach ($p in $providers) {
      az provider register --namespace $p
  }
  ```

- [ ] Confirm all show `Registered` (registration can take a minute or two
      the first time on a new subscription):

  ```powershell
  foreach ($p in $providers) {
      az provider show --namespace $p --query "{Namespace:namespace, State:registrationState}" --output tsv
  }
  ```

### 5.3 Confirm your subscription supports Cost Management budgets

Some subscription offer types (notably legacy "Free Trial") don't support
Azure Cost Management budgets. Startup-credit subscriptions (Microsoft for
Startups / Founders Hub, or a sponsorship offer) normally do, but confirm
before relying on §3.4's alert as your safety net:

- [ ] ```powershell
      az consumption budget list --output table
      ```

  An empty table (no error) means budgets are supported and none exist yet —
  expected before you've run `terraform apply`. If this command itself
  errors with something like "not supported for this offer type," the
  budget resource in Terraform will fail to apply — see [§13](#13-troubleshooting)'s
  troubleshooting entry for the fallback (a portal-based spending alert
  instead).

---

## 6. One-time Terraform remote state bootstrap

Terraform's backend (`providers.tf`) expects a storage account to already
exist — it cannot create the account it stores its own state in. **Skip this
section entirely if it's already been done once for this subscription.**

- [ ] ```powershell
      az group create --name eswmp-tfstate-rg --location canadacentral

      az storage account create `
          --name eswmptfstate `
          --resource-group eswmp-tfstate-rg `
          --sku Standard_LRS `
          --encryption-services blob

      az storage container create --name tfstate --account-name eswmptfstate --auth-mode login
      ```

  `--auth-mode login` uses your own Azure AD sign-in to create the
  container instead of the storage account's shared key — works
  regardless of whether shared-key access is enabled on the account, and
  is the confirmed-working form of this command (verified live 2026-07-19).

  If `eswmptfstate` is already taken globally (storage account names are
  globally unique across all of Azure, not just your subscription), pick a
  different name and update `backend "azurerm" { storage_account_name = ... }`
  in `infrastructure/terraform/providers.tf` to match.

  > **If `az storage account create` fails with `(SubscriptionNotFound)
  > Subscription <guid> was not found`**: despite the name, this is
  > almost never an actual subscription problem — `az account show`/
  > `az account list` will still show the subscription as `Enabled` and
  > accessible. On a brand-new subscription (exactly the startup-credit
  > scenario this guide is written for), it means the `Microsoft.Storage`
  > resource provider isn't registered yet. Confirmed live: `az group
  > create` (no specific provider needed) succeeded in the same
  > subscription while `az storage account create` failed with this exact
  > error, and `az provider show --namespace Microsoft.Storage --query
  > registrationState --output tsv` showed `NotRegistered`. Fix:
  > `az provider register --namespace Microsoft.Storage`, then poll
  > `az provider show --namespace Microsoft.Storage --query
  > registrationState --output tsv` until it says `Registered` (can take a
  > few minutes on a new subscription) before retrying. §5.2's provider
  > list above already includes `Microsoft.Storage` for exactly this
  > reason — if you skipped straight to §6, go back and run §5.2 first.

---

## 7. Configure and apply Terraform (manual deployment, part 1: infrastructure)

### 7.1 Generate secrets

- [ ] ```powershell
      cd C:\Workspace\eswmp\infrastructure\terraform
      Copy-Item staging.tfvars.example staging.tfvars

      # Postgres admin password. Uses the instance-based RandomNumberGenerator
      # API (Create()+GetBytes(byte[])), not the static GetBytes(int) overload —
      # that overload is .NET 6+ only and throws MethodNotFound on Windows
      # PowerShell 5.1 (.NET Framework), which is this machine's default shell
      # despite this guide's "PowerShell 7+" framing (confirmed live 2026-07-19).
      # This form works on both 5.1 and 7+.
      $bytes = New-Object byte[] 24
      [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
      [Convert]::ToBase64String($bytes)

      # JWT signing secret — this exact value must ALSO be configured on whatever
      # product embeds ESWMP (e.g. PetZiv) and issues the tokens ESWMP validates.
      # Coordinate this with that team before relying on it; regenerating later
      # means both sides update together.
      $bytes = New-Object byte[] 64
      [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
      [Convert]::ToBase64String($bytes)
      ```

- [ ] Open `staging.tfvars` in an editor and fill in:

  | Variable | Value |
  | --- | --- |
  | `pg_admin_password` | The first generated value above. |
  | `jwt_secret_key` | The second generated value above. |
  | `budget_alert_email` | A real inbox or team distribution list (**required**, no default — `terraform apply` prompts for it if left blank). |
  | `monthly_budget_amount` | Leave at `100` or adjust ([§2](#2-cost-breakdown-estimates--confirm-with-the-azure-pricing-calculator), [§3.4](#34-budget-alert-budget_alert_email-monthly_budget_amount)). |
  | `deploy_redis` | Leave `false` ([§3.1](#31-redis-is-now-optional-deploy_redis-defaults-to-false)) unless CO-11 has shipped. |

Postgres admin password (24 bytes):
$bytes = New-Object byte[] 24
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[Convert]::ToBase64String($bytes)

JWT secret key (64 bytes):
$bytes = New-Object byte[] 64
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[Convert]::ToBase64String($bytes)

  **Never commit `staging.tfvars`** — it's already gitignored (`*.tfvars`).

### 7.2 Provision the Azure resources

- [ ] ```powershell
      cd C:\Workspace\eswmp\infrastructure\terraform
      terraform init
      terraform workspace new staging    # first time only; use "select" if it already exists
      terraform plan -var-file staging.tfvars
      ```

  > **Use a space, not `=`, before the filename** (`-var-file staging.tfvars`,
  > not `-var-file=staging.tfvars`) — the equals-sign form is valid Terraform
  > syntax and works fine in most shells, but Windows PowerShell 5.1
  > specifically fails to pass it through to `terraform.exe` correctly,
  > producing a misleading `Error: Too many command line arguments` /
  > `To specify a working directory for the plan, use the global -chdir flag`
  > error that has nothing to do with directories. Confirmed live 2026-07-19:
  > identical command, identical directory, identical `staging.tfvars` file —
  > the equals-sign form failed every time in Windows PowerShell 5.1 (this
  > machine's default shell) while working fine from other shells; switching
  > to the space-separated form fixed it immediately, no other change needed.
  > This applies to every `-var-file=`/`-var-file` use below (`apply`,
  > `destroy`) — use the space form throughout.

  Read the plan output before applying — confirm it's creating (not
  destroying) resources, especially if this isn't the very first apply. If
  `budget_alert_email` still shows as `REPLACE_ME@yourcompany.com` in the
  plan output, go back and fill in a real address in `staging.tfvars` first —
  applying with the placeholder means your 50/80/100% budget alerts
  ([§3.4](#34-budget-alert-budget_alert_email-monthly_budget_amount)) go nowhere.

- [ ] ```powershell
      terraform apply -var-file staging.tfvars
      ```

  This takes several minutes — PostgreSQL Flexible Server and Application
  Insights/Log Analytics are the slowest resources to provision.

  > **History, for context (already fixed — you shouldn't hit this):** the
  > first time this guide was run end-to-end (2026-07-19), this exact
  > `apply` failed on all 5 Container Apps with `UNAUTHORIZED: authentication
  > required` pulling `eswmp-<service>:latest`. The real cause wasn't image
  > timing — it persisted identically even after the images were confirmed
  > pushed to ACR. `container_apps.tf` simply had **no registry
  > authentication configured on the Container Apps at all**: no `registry`
  > block, no `AcrPull` role assignment, nothing. `admin_enabled = true` on
  > the ACR resource (`main.tf`) only makes admin credentials *exist* — it
  > doesn't wire anything up to actually use them. Fixed by adding a
  > `registry` block (server + the ACR admin username/password as a
  > container-app secret) to both `azurerm_container_app.services` and
  > `azurerm_container_app.gateway`. Deliberately used the admin credential
  > rather than the container apps' own managed identity + an `AcrPull` role
  > assignment — the latter has a known race on first-ever creation, since
  > the initial revision's image pull happens as part of the same API call
  > that provisions the identity, often before the RBAC role assignment has
  > finished propagating through Azure AD. Recovering from the original
  > failure also required deleting 4 orphaned Container Apps that Azure had
  > created despite Terraform reporting an error (its `CreateOrUpdate` call
  > succeeded while the follow-up *polling-for-healthy* step failed, so they
  > existed in Azure but never made it into Terraform's state) — if you ever
  > see `A resource with the ID "..." already exists - to be managed via
  > Terraform this resource needs to be imported` after a failed `apply`,
  > that's this same pattern: check `az containerapp list --resource-group
  > eswmp-staging-rg --output table`, and delete-then-reapply rather than
  > import if the app never reached a healthy revision (nothing to lose).
  >
  > **Second issue found in the same pass, also already fixed:** with
  > registry auth fixed, all 5 Container Apps created successfully at the
  > Terraform/ARM level, but Gateway's actual container never started —
  > stuck indefinitely in `CreateContainerConfigError`. Container Apps'
  > system logs (queryable via
  > `ContainerAppSystemLogs_CL | where ContainerAppName_s == '...'` against
  > the Log Analytics workspace) named the exact cause:
  > `couldn't find key jwt-secret-key in Secret k8se-apps/capp-<name>` — the
  > Key-Vault-reference secret mechanism (`key_vault_secret_id` +
  > `identity = "System"` on a `secret` block) never successfully resolved,
  > persisting for 10+ minutes and multiple revision restarts (ruling out
  > ordinary AAD-propagation delay — that resolves in seconds to low
  > minutes, not indefinitely). Every service has multiple such
  > identity-based KV references (the 3 JWT secrets on all five apps, plus
  > `db-connection`/`servicebus-connection` on the four backends), so this
  > would have blocked all of them once they actually received traffic and
  > tried to start a replica (Consumption plan's `min_replicas = 0` meant
  > the 4 backends hadn't been put to the test yet at this point — only
  > Gateway had, since it was the one being health-checked). Fixed by
  > switching every Container App secret from a Key Vault reference to a
  > **plain value**, sourced directly from the same Terraform values/
  > resources the Key Vault secrets themselves come from (`local.jwt_secret_values`,
  > `local.db_connection_strings`, etc., in `container_apps.tf`) — the Key
  > Vault secrets still exist as the canonical store for humans/other
  > tooling to read, Container Apps just no longer depends on resolving
  > them at container-start time. Container App secrets are still encrypted
  > at rest by Azure regardless of source, so this isn't a security
  > regression, just a reliability trade-off forced by a real platform
  > limitation encountered live. If you're troubleshooting a Container App
  > stuck in `CreateContainerConfigError` on a checkout from before this
  > fix, this is almost certainly why.

### 7.3 Capture the outputs you'll need next

- [ ] ```powershell
      $acr = terraform output -raw acr_login_server
      $rg  = terraform output -raw resource_group_name
      $gatewayUrl = terraform output -raw gateway_url

      Write-Host "ACR: $acr"
      Write-Host "Resource Group: $rg"
      Write-Host "Gateway URL: $gatewayUrl"
      ```

---

## 8. Manual deployment (part 2: build, push, and deploy the 5 services)

This is the "do it by hand once" path — §9 covers the automated GitHub
Actions equivalent for every push after that.

### 8.1 Build and push the 5 images

- [ ] ```powershell
      cd C:\Workspace\eswmp
      az acr login --name $acr

      $services = @("gateway", "core", "assignment", "rules", "work")
      foreach ($svc in $services) {
          $pascal = (Get-Culture).TextInfo.ToTitleCase($svc)
          $image = "$acr/eswmp-$svc`:latest"
          docker build -f "src/Eswmp.$pascal/Dockerfile" -t $image .
          docker push $image
      }
      ```

### 8.2 Point each Container App at the pushed image

Terraform's `container_apps.tf` references `:latest` at creation time, but a
Container App does **not** automatically pull a new `:latest` just because
you pushed one.

> **`--image` alone is not enough — you need `--revision-suffix` too.**
> Confirmed live 2026-07-19: `az containerapp update --image
> $acr/eswmp-core:latest` (no suffix) silently did **nothing** when the image
> string was textually identical to what was already configured, even though
> the tag now pointed at a completely different, newly-pushed digest —
> Container Apps only creates a new revision when it detects a *template*
> change, and an unchanged `:latest` string doesn't count as one. The
> running replica just kept executing the *old* container it had already
> pulled. A unique `--revision-suffix` forces a genuinely new revision every
> time, which *does* trigger a fresh pull:

- [ ] ```powershell
      $suffix = Get-Date -Format "MMddHHmmss"
      foreach ($svc in $services) {
          az containerapp update `
              --name "eswmp-$svc-staging" `
              --resource-group $rg `
              --image "$acr/eswmp-$svc`:latest" `
              --revision-suffix "d$suffix"
      }
      ```

  (Revision suffixes must start with a letter, hence the leading `d` before
  the timestamp digits.)

### 8.3 Verify

- [ ] ```powershell
      Invoke-RestMethod -Uri "$gatewayUrl/health"
      # Expect: "Healthy"
      ```

  If it's not healthy immediately, give it 30–60 seconds — `min_replicas = 0`
  ([§3.2](#32-container-apps-scale-to-zero-for-non-prod-min_replicas)) means the very first request after `apply`/`update` may be a
  cold start, and each service's own `db.Database.MigrateAsync()` (§8.4)
  needs to finish before the health check reports ready. On a fully-cold
  environment (nothing warmed up yet), expect to retry `/health` 2-3 times a
  few seconds apart — Gateway's aggregate check calls each of the 4 backends'
  own `/health/ready`, and if *they're* also cold, that first probe times out
  before they finish waking up; each retry warms them a little more.

  > **Confirmed live 2026-07-19, already fixed:** two additional real bugs
  > surfaced getting `/health` to actually go green for the first time —
  > worth knowing if you're diagnosing a fresh `Unhealthy` and want to rule
  > these out first. (1) Every backend's own `/health/ready` unconditionally
  > checked RabbitMQ at `localhost:15672` regardless of the configured
  > `MessageBus:Transport`, because `appsettings.json`'s base
  > `"Host": "localhost"` default survives into every environment and the
  > check only gated on Host being non-blank, not on RabbitMQ actually being
  > the selected transport — fixed in `Eswmp.Shared/Extensions/HealthCheckExtensions.cs`.
  > (2) Gateway's YARP routing config pointed at the backends'
  > *revision-specific* internal hostnames (`<app>--<suffix>.internal...`)
  > instead of the stable, revision-independent form
  > (`<app>.internal...`) — meaning routing silently broke every time a
  > backend got a new revision until Gateway was also re-applied. Fixed in
  > `container_apps.tf` by switching to `azurerm_container_app.services[...].ingress[0].fqdn`
  > (and the same fix for the `gateway_url` output itself, which had the
  > identical problem). If you're on a checkout from before either fix, pull
  > latest and rebuild the 4 backend images.

### 8.4 About EF Core migrations — you don't run them separately

`Program.cs` in every service only calls `db.Database.MigrateAsync()` on
startup when `ASPNETCORE_ENVIRONMENT` is `Development` or `Staging`.
`container_apps.tf` sets this from `var.environment` (`staging` →
`"Staging"`), so **migrations apply automatically the first time each
service's container starts** against a fresh database. There is no manual
`dotnet ef database update` step for QA/staging.

(`prod` deliberately maps to `"Production"` and will **not** auto-migrate —
irrelevant today since there is no prod environment yet.)

---

## 9. Automated deployment — GitHub Actions with OIDC (no stored password)

> **✅ Done and confirmed working, 2026-07-19.** A full
> `gh workflow run deploy-qa.yml` completed all 12 jobs (the `build.yml`
> gate, 5 image build/pushes, 5 Container App deploys) successfully, and
> Gateway's `/health` reported `200 Healthy` immediately afterward. If
> you're setting this up for the first time (a different subscription, a
> fork, etc.), the steps below are exactly what was run — including one
> real gotcha found doing it: `az role assignment create` failed
> repeatedly with a confusing `(MissingSubscription)` error when run from a
> **Git Bash** shell (not Windows PowerShell — this doesn't affect you if
> you're following this guide's own PowerShell commands as written).
> Git Bash's automatic MSYS path-conversion mangles any `--scope` argument
> starting with `/` (e.g. `/subscriptions/...`) into a bogus Windows path
> before the CLI ever sends the request. Irrelevant if you run these
> commands in PowerShell as documented; noted here only in case you ever
> see the same error running Azure CLI commands from Git Bash/WSL instead.

`deploy-qa.yml` builds and pushes all 5 images and updates all 5 Container
Apps on every push to `main`, or on demand. It authenticates to Azure via
**OpenID Connect federation** — GitHub proves who it is with a short-lived
token, Azure trusts that token because you told it to, and **no client
secret or password is ever stored in GitHub** at all. This is the current
recommended practice over a service-principal client secret, and it's what
the reference doc's Container Registry section means by "images built and
tagged through your CI pipeline."

> **One more gotcha, confirmed 2026-07-19:** `az ad sp create-for-rbac
> --skip-assignment` (§9.1 below) generates a client-secret password
> credential on the service principal as a side effect, even though it's
> unused — the whole point of this section is OIDC auth with no stored
> secret. Clean it up after §9.1 so an unused credential doesn't sit around:
> `az ad app credential list --id $appId` to find its `keyId`, then
> `az ad app credential delete --id $appId --key-id <keyId>`.

### 9.1 Create the Entra ID App Registration

- [ ] ```powershell
      $appName = "eswmp-github-actions-qa"
      $app = az ad app create --display-name $appName | ConvertFrom-Json
      $appId = $app.appId

      # A service principal is what Azure RBAC actually grants roles to — the App
      # Registration alone isn't enough.
      az ad sp create-for-rbac --name $appName --skip-assignment | Out-Null
      $spObjectId = (az ad sp show --id $appId --query id --output tsv)
      ```

### 9.2 Grant it Contributor on the QA resource group only

Scope this to the one resource group, not the whole subscription — the
pipeline never needs to touch anything outside it:

- [ ] ```powershell
      $subId = az account show --query id --output tsv
      az role assignment create `
          --assignee $appId `
          --role "Contributor" `
          --scope "/subscriptions/$subId/resourceGroups/$rg"

      # It also needs to push images to ACR specifically:
      $acrId = az acr show --name $acr.Split('.')[0] --query id --output tsv
      az role assignment create `
          --assignee $appId `
          --role "AcrPush" `
          --scope $acrId
      ```

### 9.3 Create the federated identity credential

This is the piece that replaces a client secret entirely — it tells Entra ID
"trust a GitHub Actions OIDC token, but only if it says it came from this
exact repo and branch":

- [ ] ```powershell
      $repo = "behrouzbk/eswmp"   # adjust if your GitHub org/repo differs

      $credentialJson = @{
          name        = "eswmp-qa-deploy-main"
          issuer      = "https://token.actions.githubusercontent.com"
          subject     = "repo:$repo`:ref:refs/heads/main"
          description = "Deploy to QA from main branch pushes"
          audiences   = @("api://AzureADTokenExchange")
      } | ConvertTo-Json -Compress

      $credentialJson | Out-File -Encoding utf8 federated-credential.json
      az ad app federated-credential create --id $appId --parameters federated-credential.json
      Remove-Item federated-credential.json
      ```

  If you also want to trigger deploys manually via `workflow_dispatch` from a
  branch other than `main`, or from a pull request, add another
  `federated-credential create` call with a different `subject` (e.g.
  `repo:$repo:pull_request` or a specific branch ref) — one credential per
  trust condition.

### 9.4 Add the three GitHub repository secrets

No client secret among them — that's the point:

- [ ] ```powershell
      gh secret set AZURE_CLIENT_ID --body $appId --repo $repo
      gh secret set AZURE_TENANT_ID --body (az account show --query tenantId --output tsv) --repo $repo
      gh secret set AZURE_SUBSCRIPTION_ID --body $subId --repo $repo
      ```

  (No `gh` CLI? Add the same three under **Settings → Secrets and variables
  → Actions** in the GitHub web UI instead.)

### 9.5 Confirm `deploy-qa.yml` matches your actual resource names

- [ ] Open `.github/workflows/deploy-qa.yml` and confirm its `env:` block
      matches what Terraform actually created — it hardcodes
      `eswmp-staging-rg` and `eswmpstagingacr`, which is correct **only if**
      you used `project = "eswmp"` / `environment = "staging"` in
      `staging.tfvars` (the example defaults). If you changed either, update
      the workflow's `env:` block to match, or the deploy job will fail
      looking for resources that don't exist under those names.

### 9.6 Trigger it

- [ ] Automatically: push to `main`. Manually:

  ```powershell
  gh workflow run deploy-qa.yml --repo $repo
  gh run watch --repo $repo   # follow it live
  ```

  Or without `gh`: **Actions** tab → **Deploy to QA (Azure Container Apps)**
  → **Run workflow**.

---

## 10. Extending this when new services/APIs/databases are added later

You explicitly asked this guide to cover future services too, not just what
exists today. The Terraform is already shaped for this — adding a 5th (well,
6th) backend service is a data-driven change, not a structural one:

- **`infrastructure/terraform/main.tf`**: add the new database name to
  `local.service_databases` (creates its Postgres database + Key Vault
  connection-string secret automatically via the existing `for_each`).
- **`infrastructure/terraform/container_apps.tf`**: add an entry to
  `local.services` (image name, whether it's externally reachable, whether it
  needs a DB/message bus/Redis) — the existing `for_each` over
  `azurerm_container_app.services` picks it up with no other changes.
- **The new service's own `Dockerfile`**: same pattern as the existing 5 —
  `EXPOSE 8080`, build context is the repo root.
- **`.github/workflows/build.yml`**: add the new service's test project to
  the "Unit Tests" step, and its database to "Create CI databases".
- **`.github/workflows/deploy-qa.yml`**: add the new service's short name to
  **both** matrix arrays (`build-and-push-images` and `deploy`).
- **New standalone API surface with its own subdomain/API concerns** (not
  just another internal backend): that's the point at which revisiting
  `docs/Azure/azure-architecture-reference.md`'s APIM section becomes
  relevant — not before.

---

## 11. Cost monitoring and shutdown practices

### 11.1 Check spend on demand

```powershell
az consumption budget list --resource-group $rg --output table
```

Or the Azure Portal: **Cost Management + Billing → Cost analysis**, scoped to
`eswmp-staging-rg`.

### 11.2 Stop PostgreSQL when the team won't be testing for a while

This is the one resource in the stack with meaningful idle cost that
Container Apps' scale-to-zero doesn't already handle. Azure allows stopping a
Flexible Server for up to 7 days at a time (auto-restarts after 7 days if you
forget) — compute cost stops accruing while stopped; storage cost does not:

```powershell
$pgName = "eswmp-staging-postgres"   # local.resource_prefix-postgres
az postgres flexible-server stop --name $pgName --resource-group $rg

# ...and when testing resumes:
az postgres flexible-server start --name $pgName --resource-group $rg
```

Every service will fail its `/health/ready` check (and most requests) while
Postgres is stopped — this is expected, not a bug. Don't stop it mid-sprint;
this is for actual multi-day gaps (e.g. over a weekend if the team wants the
extra margin, or between test cycles).

### 11.3 Container Apps already need no manual action

`min_replicas = 0` ([§3.2](#32-container-apps-scale-to-zero-for-non-prod-min_replicas)) means idle Container Apps cost nothing beyond the
free grant on their own — there's no equivalent "stop" command needed or
available for them.

### 11.4 Service Bus and ACR have no stop/start

Their small fixed cost (~$15/month combined) is the price of them existing —
not worth the operational complexity of tearing down and recreating them
between test cycles. If you truly won't need the environment for an extended
period (weeks), see §12 instead.

---

## 12. Tearing down the whole environment

If QA won't be used for an extended stretch and you want to stop **all**
cost, not just Postgres:

```powershell
cd C:\Workspace\eswmp\infrastructure\terraform
terraform destroy -var-file staging.tfvars
```

This deletes every resource in `eswmp-staging-rg` — the resource group
itself, Postgres (and all its data — there is no backup step in this guide;
take one first if the data matters), Redis (if it was ever provisioned),
Service Bus, Container Apps, Key Vault, Log Analytics, everything. The
remote-state storage account (`eswmptfstate`, [§6](#6-one-time-terraform-remote-state-bootstrap)) is a separate bootstrap
resource and is **not** touched by this destroy — re-running `terraform
apply -var-file staging.tfvars` later recreates everything from the same
state backend.

Key Vault has soft-delete enabled by default in this provider config
(`providers.tf`'s `key_vault { purge_soft_delete_on_destroy = true }`) —
`terraform destroy` purges it immediately rather than leaving a soft-deleted
vault occupying the name for its retention window, so re-applying later with
the same Key Vault name won't hit a naming conflict.

---

## 13. Troubleshooting

| Symptom | Cause / fix |
| --- | --- |
| `terraform apply` fails on the budget resource with something like "the subscription does not support this operation" | Your subscription offer type doesn't support Cost Management budgets ([§5.3](#53-confirm-your-subscription-supports-cost-management-budgets)). Fallback: comment out the `azurerm_consumption_budget_resource_group` resource in `main.tf`, apply everything else, and set a spending alert manually via **Cost Management + Billing → Budgets** in the portal (same 50/80/100% thresholds, same email) — it just won't be version-controlled. |
| `terraform apply` fails with a resource-provider-not-registered error | You skipped or a provider hasn't finished registering yet ([§5.2](#52-register-the-resource-providers-this-project-uses)). Re-run the registration loop, wait a minute, retry `apply`. |
| `az storage account create` (§6) fails with `(SubscriptionNotFound) Subscription <guid> was not found`, even though `az account show`/`az account list` show that exact subscription as `Enabled` | Misleading error text — actually means the `Microsoft.Storage` resource provider isn't registered yet, common on a brand-new subscription. Confirmed live 2026-07-19: `az group create` succeeded in the same subscription (doesn't need a specific provider) while `az storage account create` failed this way, and `az provider show --namespace Microsoft.Storage --query registrationState -o tsv` showed `NotRegistered`. Fix: `az provider register --namespace Microsoft.Storage`, poll the same command until it says `Registered` (a few minutes on a new subscription), then retry §6. |
| `[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(N))` (§7.1's secret generation) throws `Method invocation failed because ... does not contain a method named 'GetBytes'` | The static `RandomNumberGenerator.GetBytes(int)` overload is .NET 6+ only. Windows PowerShell 5.1 runs on .NET Framework, which doesn't have it — and Windows PowerShell 5.1 is this machine's default shell despite this guide's "PowerShell 7+" framing (confirmed live 2026-07-19). §7.1's commands already use the fixed, dual-compatible form (`RandomNumberGenerator.Create().GetBytes(byte[])`); if you're copying commands from an older source or from memory, use that form instead. |
| `terraform plan`/`apply`/`destroy -var-file=staging.tfvars` (equals-sign form) fails with `Error: Too many command line arguments` / `To specify a working directory for the plan, use the global -chdir flag` — nothing to do with directories despite the message | Confirmed live 2026-07-19: Windows PowerShell 5.1 (this machine's default shell) fails to pass the equals-sign `-var-file=X` form through to `terraform.exe` correctly; ruled out `TF_CLI_ARGS`/`TF_CLI_ARGS_plan` env vars and a shadowing `terraform` alias/function first (both came back clean) before isolating it to this specific syntax form. The identical command from a different shell (PowerShell 7, or non-interactive) works fine — this is specific to Windows PowerShell 5.1's native-argument handling. | Use the space-separated form instead: `-var-file staging.tfvars` (no `=`). Every command in §7.2/§12/§14 already uses this form. |
| `az acr login` succeeds but `docker push` gets `401 Unauthorized` | The ACR login token expired (they're short-lived); re-run `az acr login --name $acr` and retry the push. |
| A Container App won't go healthy after `az containerapp update` | Check its logs before assuming the image is broken: `az containerapp logs show --name eswmp-<service>-staging --resource-group $rg --follow`. A common first-deploy cause is a missing/misnamed Key Vault secret reference — confirm the secret names in `container_apps.tf` match what `main.tf` actually created. |
| `terraform apply` (§7.2) fails on the Container Apps with `UNAUTHORIZED: authentication required` pulling `eswmp-<service>:latest`, even with the images confirmed present in ACR | This was a real, now-fixed bug (2026-07-19), not an environment issue — `container_apps.tf` had no `registry` block at all, so Container Apps had no way to authenticate to the ACR regardless of whether the image existed. Fixed by adding a `registry` block using the ACR admin credential to both `azurerm_container_app.services` and `.gateway`. If you're on a checkout from before this fix, pull latest. See §7.2's detailed history note for the full story, including how to recover orphaned broken Container Apps Azure creates despite Terraform reporting an error (`az containerapp list`, then delete + re-`apply` rather than import). |
| A Container App is stuck in `CreateContainerConfigError` (check via `az containerapp replica list --name <app> --resource-group $rg`) even though `terraform apply` reported success | Also a real, now-fixed bug (2026-07-19) — Key-Vault-referenced Container App secrets (`key_vault_secret_id` + `identity = "System"`) never resolved for a freshly-created app + freshly-granted managed identity; Container Apps' system logs named it exactly: `couldn't find key <secret-name> in Secret k8se-apps/capp-<app-name>`, persisting 10+ minutes and multiple revision restarts. Query the real reason yourself via `az monitor log-analytics query --workspace <workspace-customer-id> --analytics-query "ContainerAppSystemLogs_CL \| where ContainerAppName_s == '<app-name>' \| order by TimeGenerated desc \| take 30 \| project TimeGenerated, Log_s, Reason_s"` (get the workspace ID with `az monitor log-analytics workspace show --resource-group $rg --workspace-name eswmp-staging-logs --query customerId -o tsv`; needs `az extension add --name log-analytics` first). Fixed by switching every Container App secret to a plain value instead of a Key Vault reference — see §7.2's detailed history note. If you're on a checkout from before this fix, pull latest. |
| Logs/traces stop appearing partway through a heavy test session | You've hit the 1 GB/day Log Analytics cap ([§3.3](#33-log-analytics-daily-ingestion-cap-daily_quota_gb)). Check **Log Analytics workspace → Usage and estimated costs** in the portal for confirmation; ingestion resumes at the next UTC day, or raise `daily_quota_gb` temporarily and re-apply if you need it back sooner. |
| GitHub Actions `azure/login` fails with an OIDC/federation error | The federated credential's `subject` ([§9.3](#93-create-the-federated-identity-credential)) must match exactly: branch pushes need `repo:<org>/<repo>:ref:refs/heads/<branch>`; a mismatch (wrong repo name, wrong branch, or a manually-dispatched run on a different ref) fails closed with an authentication error, not a helpful "wrong subject" message. Double-check `$repo` and the branch name used in §9.3 against what actually triggered the run. |
| `deploy-qa.yml` fails looking for a resource group/ACR that doesn't exist | Its hardcoded `env:` block assumes `project = "eswmp"` and `environment = "staging"` ([§9.5](#95-confirm-deploy-qayml-matches-your-actual-resource-names)). If your `staging.tfvars` used different values, update the workflow to match. |

---

## 14. Command cheat-sheet

```powershell
# Check what's running / spend
az consumption budget list --resource-group $rg --output table
az containerapp list --resource-group $rg --output table

# Redeploy one service manually after a hotfix (skip the whole matrix)
az containerapp update --name eswmp-work-staging --resource-group $rg --image "$acr/eswmp-work`:latest"

# Tail one service's logs
az containerapp logs show --name eswmp-work-staging --resource-group $rg --follow

# Pause Postgres for a multi-day gap / resume it
az postgres flexible-server stop  --name eswmp-staging-postgres --resource-group $rg
az postgres flexible-server start --name eswmp-staging-postgres --resource-group $rg

# Full environment teardown
terraform destroy -var-file staging.tfvars

# Trigger the automated deploy on demand
gh workflow run deploy-qa.yml --repo behrouzbk/eswmp
```
