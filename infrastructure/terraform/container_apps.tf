# Per-service static config — kept free of any resource references so it can be
# used as a for_each source (for_each's key set must be known at plan time).
locals {
  services = {
    gateway = {
      image            = "eswmp-gateway"
      external         = true
      needs_db         = false
      needs_messagebus = false
      needs_redis      = false
    }
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
  jwt_secret_refs = {
    jwt-secret-key = azurerm_key_vault_secret.jwt_secret.versionless_id
    jwt-issuer     = azurerm_key_vault_secret.jwt_issuer.versionless_id
    jwt-audience   = azurerm_key_vault_secret.jwt_audience.versionless_id
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

  # ── Key Vault-backed secrets (every service needs the Jwt ones) ────────────
  dynamic "secret" {
    for_each = local.jwt_secret_refs
    content {
      name                = secret.key
      key_vault_secret_id = secret.value
      identity            = "System"
    }
  }

  dynamic "secret" {
    for_each = each.value.needs_db ? { "db-connection" = azurerm_key_vault_secret.db_connection[each.key].versionless_id } : {}
    content {
      name                = secret.key
      key_vault_secret_id = secret.value
      identity            = "System"
    }
  }

  dynamic "secret" {
    for_each = each.value.needs_messagebus ? { "servicebus-connection" = azurerm_key_vault_secret.servicebus_connection.versionless_id } : {}
    content {
      name                = secret.key
      key_vault_secret_id = secret.value
      identity            = "System"
    }
  }

  dynamic "secret" {
    for_each = (each.value.needs_redis && var.deploy_redis) ? { "redis-connection" = azurerm_key_vault_secret.redis_connection[0].versionless_id } : {}
    content {
      name                = secret.key
      key_vault_secret_id = secret.value
      identity            = "System"
    }
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

      # Gateway routes to the other four services by internal FQDN — Container
      # Apps assigns this only once the target app exists, so this reads the
      # sibling resource instances created by this same for_each.
      dynamic "env" {
        for_each = each.key == "gateway" ? {
          "ReverseProxy__Clusters__core__Destinations__primary__Address"       = "https://${azurerm_container_app.services["core"].latest_revision_fqdn}"
          "ReverseProxy__Clusters__assignment__Destinations__primary__Address" = "https://${azurerm_container_app.services["assignment"].latest_revision_fqdn}"
          "ReverseProxy__Clusters__rules__Destinations__primary__Address"      = "https://${azurerm_container_app.services["rules"].latest_revision_fqdn}"
          "ReverseProxy__Clusters__work__Destinations__primary__Address"       = "https://${azurerm_container_app.services["work"].latest_revision_fqdn}"
        } : {}
        content {
          name  = env.key
          value = env.value
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
