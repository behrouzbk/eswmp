output "resource_group_name" {
  value = azurerm_resource_group.main.name
}

output "acr_login_server" {
  value = azurerm_container_registry.acr.login_server
}

output "postgres_fqdn" {
  value     = azurerm_postgresql_flexible_server.postgres.fqdn
  sensitive = true
}

output "redis_hostname" {
  value     = var.deploy_redis ? azurerm_redis_cache.redis[0].hostname : null
  sensitive = true
}

output "redis_primary_key" {
  value     = var.deploy_redis ? azurerm_redis_cache.redis[0].primary_access_key : null
  sensitive = true
}

output "container_app_environment_id" {
  value = azurerm_container_app_environment.env.id
}

output "application_insights_connection_string" {
  value     = azurerm_application_insights.appinsights.connection_string
  sensitive = true
}

output "key_vault_uri" {
  value = azurerm_key_vault.kv.vault_uri
}

output "servicebus_connection_string" {
  value     = azurerm_servicebus_namespace_authorization_rule.sb_manage.primary_connection_string
  sensitive = true
}

output "gateway_url" {
  description = "Public HTTPS URL of the Gateway — the only externally reachable service"
  # Stable, revision-independent hostname — see the comment on Gateway's
  # ReverseProxy env vars in container_apps.tf for why this isn't
  # latest_revision_fqdn (that form changes on every new revision/deploy).
  value = "https://${azurerm_container_app.gateway.ingress[0].fqdn}"
}
