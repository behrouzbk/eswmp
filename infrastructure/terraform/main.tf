resource "azurerm_resource_group" "main" {
  name     = "${local.resource_prefix}-rg"
  location = var.location
  tags     = local.common_tags
}

# ── Container Registry ──────────────────────────────────────────────────────
resource "azurerm_container_registry" "acr" {
  name                = replace("${local.resource_prefix}acr", "-", "")
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "Basic"
  admin_enabled       = true
  tags                = local.common_tags
}

# ── PostgreSQL Flexible Server (shared, per-service databases) ───────────────
resource "azurerm_postgresql_flexible_server" "postgres" {
  name                   = "${local.resource_prefix}-postgres"
  resource_group_name    = azurerm_resource_group.main.name
  location               = azurerm_resource_group.main.location
  version                = "16"
  administrator_login    = var.pg_admin_login
  administrator_password = var.pg_admin_password
  sku_name               = var.environment == "prod" ? "GP_Standard_D2s_v3" : "B_Standard_B1ms"
  storage_mb             = var.environment == "prod" ? 65536 : 32768
  zone                   = "1"

  # HA is disabled for non-prod by omitting this block entirely — the azurerm
  # provider's high_availability.mode no longer accepts "Disabled" as a value
  # (only "ZoneRedundant"/"SameZone"); the block's absence is what disables it.
  dynamic "high_availability" {
    for_each = var.environment == "prod" ? [1] : []
    content {
      mode                      = "ZoneRedundant"
      standby_availability_zone = "2"
    }
  }

  maintenance_window {
    day_of_week  = 0
    start_hour   = 2
    start_minute = 0
  }

  tags = local.common_tags
}

resource "azurerm_postgresql_flexible_server_firewall_rule" "azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_postgresql_flexible_server.postgres.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

locals {
  service_databases = [
    "eswmp_core",
    "eswmp_assignment",
    "eswmp_rules",
    "eswmp_work",
  ]
}

resource "azurerm_postgresql_flexible_server_database" "service_db" {
  for_each  = toset(local.service_databases)
  name      = each.key
  server_id = azurerm_postgresql_flexible_server.postgres.id
  collation = "en_US.utf8"
  charset   = "utf8"
}

# ── Redis Cache ───────────────────────────────────────────────────────────────
# count-gated by var.deploy_redis (default false) — see its description in
# variables.tf for why this is off by default on a cost-constrained QA credit.
resource "azurerm_redis_cache" "redis" {
  count               = var.deploy_redis ? 1 : 0
  name                = "${local.resource_prefix}-redis"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  capacity            = var.environment == "prod" ? 1 : 0
  family              = "C"
  sku_name            = var.environment == "prod" ? "Standard" : "Basic"
  enable_non_ssl_port = false
  minimum_tls_version = "1.2"
  tags                = local.common_tags
}

# ── Container Apps Environment (3 services + gateway) ─────────────────────────
resource "azurerm_log_analytics_workspace" "logs" {
  name                = "${local.resource_prefix}-logs"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "PerGB2018"
  retention_in_days   = 30
  # Hard ingestion cap for non-prod — log/trace volume is the easiest cost to
  # accidentally blow through on a fixed credit (100% OTel sampling is fine for
  # a QA environment's traffic level, but nothing else bounds what a noisy
  # container can ship). null (no cap) for prod, where availability of full
  # logs matters more than a hard stop. Once the cap is hit for the day,
  # ingestion pauses until the next UTC day — Application Insights/traces
  # stop arriving, they are not throttled gracefully. Raise this if a real
  # load/soak test needs more headroom for a day.
  daily_quota_gb = var.environment == "prod" ? -1 : 1
  tags           = local.common_tags
}

resource "azurerm_container_app_environment" "env" {
  name                       = "${local.resource_prefix}-cae"
  resource_group_name        = azurerm_resource_group.main.name
  location                   = azurerm_resource_group.main.location
  log_analytics_workspace_id = azurerm_log_analytics_workspace.logs.id
  tags                       = local.common_tags
}

# ── Application Insights ──────────────────────────────────────────────────────
resource "azurerm_application_insights" "appinsights" {
  name                = "${local.resource_prefix}-ai"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  application_type    = "web"
  workspace_id        = azurerm_log_analytics_workspace.logs.id
  tags                = local.common_tags
}

# ── Service Bus (MassTransit transport — staging/prod replacement for RabbitMQ) ─
resource "azurerm_servicebus_namespace" "sb" {
  # Azure Service Bus namespace names may not end in "-sb" or "-mgmt" — confirmed
  # live 2026-07-19 (`"name" cannot end with a hyphen, -sb, or -mgmt`).
  name                = "${local.resource_prefix}-bus"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  # Standard tier required: MassTransit's Azure Service Bus transport uses topics
  # + subscriptions for pub/sub, which the Basic tier does not support.
  sku  = "Standard"
  tags = local.common_tags
}

resource "azurerm_servicebus_namespace_authorization_rule" "sb_manage" {
  name         = "eswmp-manage"
  namespace_id = azurerm_servicebus_namespace.sb.id
  # MassTransit provisions its own topics/subscriptions/queues at bus startup and
  # needs Manage rights to do so — it is not given a fixed, pre-created topology.
  listen = true
  send   = true
  manage = true
}

# ── Key Vault ─────────────────────────────────────────────────────────────────
data "azurerm_client_config" "current" {}

resource "azurerm_key_vault" "kv" {
  name                = "${local.resource_prefix}-kv"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  tenant_id           = data.azurerm_client_config.current.tenant_id
  sku_name            = "standard"
  tags                = local.common_tags
}

# Grants the identity running `terraform apply` full secret management — separate
# from the per-container-app read-only grants in container_apps.tf so the two
# don't fight over ownership of the vault's access policy list.
resource "azurerm_key_vault_access_policy" "terraform_operator" {
  key_vault_id       = azurerm_key_vault.kv.id
  tenant_id          = data.azurerm_client_config.current.tenant_id
  object_id          = data.azurerm_client_config.current.object_id
  secret_permissions = ["Get", "List", "Set", "Delete", "Recover", "Backup", "Restore", "Purge"]
}

resource "azurerm_key_vault_secret" "jwt_secret" {
  name         = "JwtSecretKey"
  value        = var.jwt_secret_key
  key_vault_id = azurerm_key_vault.kv.id
  depends_on   = [azurerm_key_vault_access_policy.terraform_operator]
}

resource "azurerm_key_vault_secret" "jwt_issuer" {
  name         = "JwtIssuer"
  value        = var.jwt_issuer
  key_vault_id = azurerm_key_vault.kv.id
  depends_on   = [azurerm_key_vault_access_policy.terraform_operator]
}

resource "azurerm_key_vault_secret" "jwt_audience" {
  name         = "JwtAudience"
  value        = var.jwt_audience
  key_vault_id = azurerm_key_vault.kv.id
  depends_on   = [azurerm_key_vault_access_policy.terraform_operator]
}

resource "azurerm_key_vault_secret" "servicebus_connection" {
  name         = "ServiceBusConnection"
  value        = azurerm_servicebus_namespace_authorization_rule.sb_manage.primary_connection_string
  key_vault_id = azurerm_key_vault.kv.id
  depends_on   = [azurerm_key_vault_access_policy.terraform_operator]
}

resource "azurerm_key_vault_secret" "redis_connection" {
  count        = var.deploy_redis ? 1 : 0
  name         = "RedisConnection"
  value        = "${azurerm_redis_cache.redis[0].hostname}:${azurerm_redis_cache.redis[0].ssl_port},password=${azurerm_redis_cache.redis[0].primary_access_key},ssl=True,abortConnect=False"
  key_vault_id = azurerm_key_vault.kv.id
  depends_on   = [azurerm_key_vault_access_policy.terraform_operator]
}

locals {
  db_connection_strings = {
    core       = "Host=${azurerm_postgresql_flexible_server.postgres.fqdn};Port=5432;Database=eswmp_core;Username=${var.pg_admin_login};Password=${var.pg_admin_password};Ssl Mode=Require"
    assignment = "Host=${azurerm_postgresql_flexible_server.postgres.fqdn};Port=5432;Database=eswmp_assignment;Username=${var.pg_admin_login};Password=${var.pg_admin_password};Ssl Mode=Require"
    rules      = "Host=${azurerm_postgresql_flexible_server.postgres.fqdn};Port=5432;Database=eswmp_rules;Username=${var.pg_admin_login};Password=${var.pg_admin_password};Ssl Mode=Require"
    work       = "Host=${azurerm_postgresql_flexible_server.postgres.fqdn};Port=5432;Database=eswmp_work;Username=${var.pg_admin_login};Password=${var.pg_admin_password};Ssl Mode=Require"
  }
}

resource "azurerm_key_vault_secret" "db_connection" {
  for_each     = local.db_connection_strings
  name         = "${title(each.key)}DbConnection"
  value        = each.value
  key_vault_id = azurerm_key_vault.kv.id
  depends_on   = [azurerm_key_vault_access_policy.terraform_operator]
}

# ── Cost guardrail — Consumption Budget ────────────────────────────────────
# Alerts only; Azure budgets never block or throttle spend. This is the
# single most important safety net against a shared, time-boxed credit
# (see docs/azure/QA-ENVIRONMENT-GUIDE.md for the full cost strategy) —
# treat a 100% actual-spend alert as "stop and look", not background noise.
resource "azurerm_consumption_budget_resource_group" "qa_budget" {
  name              = "${local.resource_prefix}-budget"
  resource_group_id = azurerm_resource_group.main.id

  amount     = var.monthly_budget_amount
  time_grain = "Monthly"

  time_period {
    # Azure requires a start date aligned to the beginning of a month.
    start_date = formatdate("YYYY-MM-01'T'00:00:00'Z'", timestamp())
  }

  notification {
    enabled        = true
    threshold      = 50
    operator       = "GreaterThan"
    threshold_type = "Actual"
    contact_emails = [var.budget_alert_email]
  }

  notification {
    enabled        = true
    threshold      = 80
    operator       = "GreaterThan"
    threshold_type = "Actual"
    contact_emails = [var.budget_alert_email]
  }

  notification {
    enabled        = true
    threshold      = 100
    operator       = "GreaterThan"
    threshold_type = "Actual"
    contact_emails = [var.budget_alert_email]
  }

  notification {
    enabled        = true
    threshold      = 100
    operator       = "GreaterThan"
    threshold_type = "Forecasted"
    contact_emails = [var.budget_alert_email]
  }

  lifecycle {
    # The start_date is computed from timestamp() at apply time — without this,
    # every subsequent `terraform plan` would show a spurious diff on this
    # attribute alone (Azure ignores changes to a budget's start_date after
    # creation for a Monthly grain anyway; it always aligns to month boundaries).
    ignore_changes = [time_period[0].start_date]
  }
}
