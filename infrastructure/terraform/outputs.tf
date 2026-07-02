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
  value     = azurerm_redis_cache.redis.hostname
  sensitive = true
}

output "redis_primary_key" {
  value     = azurerm_redis_cache.redis.primary_access_key
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
