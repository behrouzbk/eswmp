# Per-service static config — kept free of any resource references so it can be
# used as a for_each source (for_each's key set must be known at plan time).
locals {
  # "gateway" is deliberately not in this map — see the standalone
  # azurerm_container_app.gateway resource below for why.
  services = {
    core = {
      image            = "eswmp-core"
      external         = false
      needs_db         = true
      needs_messagebus = true
      needs_redis      = true
    }
    assignment = {
      image            = "eswmp-assignment"
      external         = false
      needs_db         = true
      needs_messagebus = true
      needs_redis      = false
    }
    rules = {
      image            = "eswmp-rules"
      external         = false
      needs_db         = true
      needs_messagebus = true
      needs_redis      = false
    }
    work = {
      image            = "eswmp-work"
      external         = false
      needs_db         = true
      needs_messagebus = true
      needs_redis      = false
    }
  }

  # Every service validates inbound JWTs (Gateway) or trusts claims already
  # validated by the Gateway (Core/Assignment/Rules/Work) — all five need the Jwt secrets.
  # Keyed by the container-app secret name (must be lowercase/kebab-case).
  #
  # Plain values, not Key Vault references (key_vault_secret_id + identity =
  # "System") — confirmed live 2026-07-19 that the KV-reference path is
  # unreliable for a freshly-created Container App + freshly-granted managed
  # identity: every container app failed to start with `couldn't find key
  # jwt-secret-key in Secret k8se-apps/capp-<name>`, persisting for 10+
  # minutes and multiple revision restarts (ruling out ordinary propagation
  # delay). The Key Vault secrets themselves (JwtSecretKey/Issuer/Audience,
  # below) still exist as the canonical source of truth for humans/other
  # tooling; Container Apps just gets its own copy injected directly instead
  # of resolving it at container-start time. Container App secrets are still
  # encrypted at rest by Azure regardless of source.
  jwt_secret_values = {
    jwt-secret-key = var.jwt_secret_key
    jwt-issuer     = var.jwt_issuer
    jwt-audience   = var.jwt_audience
  }

  # Program.cs only runs `db.Database.MigrateAsync()` on startup when
  # IsDevelopment() or IsStaging() — a plain "Production" ASPNETCORE_ENVIRONMENT
  # would silently skip migrations against a brand-new staging database.
  aspnetcore_environment = {
    dev     = "Development"
    staging = "Staging"
    prod    = "Production"
  }
}

resource "azurerm_container_app" "services" {
  for_each = local.services

  name                         = "eswmp-${each.key}-${var.environment}"
  container_app_environment_id = azurerm_container_app_environment.env.id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"

  identity {
    type = "SystemAssigned"
  }

  # ── Secrets (plain values — see local.jwt_secret_values' comment for why
  # these aren't Key Vault references) ────────────────────────────────────
  dynamic "secret" {
    for_each = local.jwt_secret_values
    content {
      name  = secret.key
      value = secret.value
    }
  }

  dynamic "secret" {
    for_each = each.value.needs_db ? { "db-connection" = local.db_connection_strings[each.key] } : {}
    content {
      name  = secret.key
      value = secret.value
    }
  }

  dynamic "secret" {
    for_each = each.value.needs_messagebus ? { "servicebus-connection" = azurerm_servicebus_namespace_authorization_rule.sb_manage.primary_connection_string } : {}
    content {
      name  = secret.key
      value = secret.value
    }
  }

  dynamic "secret" {
    for_each = (each.value.needs_redis && var.deploy_redis) ? { "redis-connection" = "${azurerm_redis_cache.redis[0].hostname}:${azurerm_redis_cache.redis[0].ssl_port},password=${azurerm_redis_cache.redis[0].primary_access_key},ssl=True,abortConnect=False" } : {}
    content {
      name  = secret.key
      value = secret.value
    }
  }

  # Without this, Container Apps has no way to authenticate to the ACR at
  # all — admin_enabled = true on the registry only makes admin credentials
  # exist, it doesn't wire anything up to use them. Confirmed live
  # 2026-07-19: every Container App failed identically with `UNAUTHORIZED:
  # authentication required` even after the images were confirmed present in
  # ACR, because nothing here ever told Container Apps how to log in.
  # Uses the ACR admin credential rather than the container app's own
  # managed identity + an AcrPull role assignment — the latter has a known
  # race on first-ever creation (the initial revision's image pull happens
  # as part of the same create call that provisions the identity, often
  # before an RBAC role assignment has finished propagating through Azure
  # AD), which the admin-credential path avoids entirely.
  secret {
    name  = "acr-password"
    value = azurerm_container_registry.acr.admin_password
  }

  registry {
    server               = azurerm_container_registry.acr.login_server
    username             = azurerm_container_registry.acr.admin_username
    password_secret_name = "acr-password"
  }

  template {
    # Consumption plan (the default here — no workload_profile set) supports
    # true scale-to-zero. A QA environment used intermittently during work
    # hours has no reason to bill for 5 idle containers 24/7 — prod is the
    # one tier where a cold start on the first request of the day is
    # unacceptable, so it alone keeps a warm replica.
    min_replicas = var.environment == "prod" ? 1 : 0
    max_replicas = 5

    container {
      name   = each.key
      image  = "${azurerm_container_registry.acr.login_server}/${each.value.image}:latest"
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = local.aspnetcore_environment[var.environment]
      }
      env {
        name  = "Otel__ServiceName"
        value = "Eswmp.${title(each.key)}"
      }
      env {
        name  = "ApplicationInsights__ConnectionString"
        value = azurerm_application_insights.appinsights.connection_string
      }

      env {
        name        = "Jwt__SecretKey"
        secret_name = "jwt-secret-key"
      }
      env {
        name        = "Jwt__Issuer"
        secret_name = "jwt-issuer"
      }
      env {
        name        = "Jwt__Audience"
        secret_name = "jwt-audience"
      }

      dynamic "env" {
        for_each = each.value.needs_db ? { "ConnectionStrings__Default" = "db-connection" } : {}
        content {
          name        = env.key
          secret_name = env.value
        }
      }

      dynamic "env" {
        for_each = each.value.needs_messagebus ? {
          "MessageBus__Transport"                  = "AzureServiceBus"
          "MessageBus__ServiceBusConnectionString" = null
        } : {}
        content {
          name        = env.key
          value       = env.key == "MessageBus__Transport" ? env.value : null
          secret_name = env.key == "MessageBus__ServiceBusConnectionString" ? "servicebus-connection" : null
        }
      }

      dynamic "env" {
        for_each = (each.value.needs_redis && var.deploy_redis) ? { "Redis__ConnectionString" = "redis-connection" } : {}
        content {
          name        = env.key
          secret_name = env.value
        }
      }
    }
  }

  ingress {
    external_enabled = each.value.external
    target_port      = 8080

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  tags = local.common_tags
}

# Read-only Key Vault grant per container app's own managed identity — kept
# separate from the Terraform-operator policy in main.tf.
resource "azurerm_key_vault_access_policy" "container_app" {
  for_each = local.services

  key_vault_id       = azurerm_key_vault.kv.id
  tenant_id          = data.azurerm_client_config.current.tenant_id
  object_id          = azurerm_container_app.services[each.key].identity[0].principal_id
  secret_permissions = ["Get"]
}

# ── Gateway — a standalone resource, not part of the local.services for_each ──
# It needs to reference the other four services' ingress[0].fqdn for its
# YARP routing config — the stable, revision-independent hostname
# (<app-name>.internal.<environment-domain>), not latest_revision_fqdn
# (<app-name>--<revision-suffix>.internal...). Using the revision-specific
# form was a real bug found live 2026-07-19: forcing a new backend revision
# (e.g. any future `az containerapp update`, including what deploy-qa.yml
# does on every push) changes that hostname, silently breaking Gateway's
# routing until Gateway itself is also re-applied — Azure Container Apps'
# internal DNS resolves the stable form to whichever revision currently
# holds 100% traffic, which is the whole point of "Single" revision mode.
# Referencing a sibling for_each instance's attribute
# from within the *same* resource address is a well-known Terraform limitation
# that produces a false-positive dependency cycle — confirmed live 2026-07-19
# ("Cycle: azurerm_container_app.services[\"rules\"], [\"work\"], [\"assignment\"],
# [\"core\"]") even though the real dependency is a clean one-directional DAG
# (gateway -> the other four, never the reverse). Splitting gateway into its
# own resource address turns this into a normal cross-resource reference,
# which Terraform's graph builder handles correctly.
resource "azurerm_container_app" "gateway" {
  name                         = "eswmp-gateway-${var.environment}"
  container_app_environment_id = azurerm_container_app_environment.env.id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"

  identity {
    type = "SystemAssigned"
  }

  dynamic "secret" {
    for_each = local.jwt_secret_values
    content {
      name  = secret.key
      value = secret.value
    }
  }

  # See the matching comment on azurerm_container_app.services above — same
  # missing-registry-auth bug, same admin-credential fix.
  secret {
    name  = "acr-password"
    value = azurerm_container_registry.acr.admin_password
  }

  registry {
    server               = azurerm_container_registry.acr.login_server
    username             = azurerm_container_registry.acr.admin_username
    password_secret_name = "acr-password"
  }

  template {
    min_replicas = var.environment == "prod" ? 1 : 0
    max_replicas = 5

    container {
      name   = "gateway"
      image  = "${azurerm_container_registry.acr.login_server}/eswmp-gateway:latest"
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = local.aspnetcore_environment[var.environment]
      }
      env {
        name  = "Otel__ServiceName"
        value = "Eswmp.Gateway"
      }
      env {
        name  = "ApplicationInsights__ConnectionString"
        value = azurerm_application_insights.appinsights.connection_string
      }
      env {
        name        = "Jwt__SecretKey"
        secret_name = "jwt-secret-key"
      }
      env {
        name        = "Jwt__Issuer"
        secret_name = "jwt-issuer"
      }
      env {
        name        = "Jwt__Audience"
        secret_name = "jwt-audience"
      }

      env {
        name  = "ReverseProxy__Clusters__core__Destinations__primary__Address"
        value = "https://${azurerm_container_app.services["core"].ingress[0].fqdn}"
      }
      env {
        name  = "ReverseProxy__Clusters__assignment__Destinations__primary__Address"
        value = "https://${azurerm_container_app.services["assignment"].ingress[0].fqdn}"
      }
      env {
        name  = "ReverseProxy__Clusters__rules__Destinations__primary__Address"
        value = "https://${azurerm_container_app.services["rules"].ingress[0].fqdn}"
      }
      env {
        name  = "ReverseProxy__Clusters__work__Destinations__primary__Address"
        value = "https://${azurerm_container_app.services["work"].ingress[0].fqdn}"
      }
    }
  }

  ingress {
    external_enabled = true
    target_port      = 8080

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  tags = local.common_tags
}

resource "azurerm_key_vault_access_policy" "container_app_gateway" {
  key_vault_id       = azurerm_key_vault.kv.id
  tenant_id          = data.azurerm_client_config.current.tenant_id
  object_id          = azurerm_container_app.gateway.identity[0].principal_id
  secret_permissions = ["Get"]
}
