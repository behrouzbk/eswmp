locals {
  services = {
    "gateway"    = { port = 8080, image = "eswmp-gateway" }
    "core"       = { port = 8080, image = "eswmp-core" }
    "assignment" = { port = 8080, image = "eswmp-assignment" }
    "rules"      = { port = 8080, image = "eswmp-rules" }
  }
}

resource "azurerm_container_app" "services" {
  for_each = local.services

  name                         = "eswmp-${each.key}-${var.environment}"
  container_app_environment_id = azurerm_container_app_environment.env.id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"

  template {
    min_replicas = 1
    max_replicas = 5

    container {
      name   = each.key
      image  = "${azurerm_container_registry.acr.login_server}/${each.value.image}:latest"
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }
      env {
        name  = "Otel__ServiceName"
        value = "Eswmp.${title(each.key)}"
      }
      env {
        name  = "ApplicationInsights__ConnectionString"
        value = azurerm_application_insights.appinsights.connection_string
      }
    }
  }

  ingress {
    external_enabled = each.key == "gateway"
    target_port      = each.value.port

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  tags = local.common_tags
}
