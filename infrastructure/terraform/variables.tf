variable "environment" {
  description = "Deployment environment"
  type        = string
  default     = "dev"
  validation {
    condition     = contains(["dev", "staging", "prod"], var.environment)
    error_message = "Must be dev, staging, or prod."
  }
}

variable "location" {
  description = "Azure region"
  type        = string
  default     = "canadacentral"
}

variable "project" {
  description = "Project name prefix"
  type        = string
  default     = "eswmp"
}

variable "pg_admin_login" {
  description = "PostgreSQL Flexible Server administrator login"
  type        = string
  default     = "eswmpadmin"
}

variable "pg_admin_password" {
  description = "PostgreSQL Flexible Server administrator password"
  type        = string
  sensitive   = true
}

variable "jwt_secret_key" {
  description = "Shared HMAC key used to validate JWTs issued by the consuming product. ESWMP validates tokens, it never issues them — this exact value must also be configured on the consuming product's token issuer, minimum 64 bytes."
  type        = string
  sensitive   = true
}

variable "jwt_issuer" {
  description = "Expected JWT 'iss' claim — must match the consuming product's token issuer configuration"
  type        = string
  default     = "eswmp"
}

variable "jwt_audience" {
  description = "Expected JWT 'aud' claim — must match the consuming product's token issuer configuration"
  type        = string
  default     = "eswmp-api"
}

variable "deploy_redis" {
  description = "Whether to provision Azure Cache for Redis. Defaults to false: CO-11 (Redis-backed slot search caching) is not yet implemented anywhere in the codebase, so paying for a cache nothing reads or writes is pure waste on a limited QA credit. AddStackExchangeRedisCache in Eswmp.Core tolerates an empty Redis:ConnectionString fine (the health check that would exercise it is itself conditional on that value being set — see HealthCheckExtensions.cs). Flip to true once CO-11 actually ships."
  type        = bool
  default     = false
}

variable "budget_alert_email" {
  description = "Email address that receives Azure Consumption Budget alerts (50/80/100% of monthly_budget_amount). Required — there is no sensible default for someone else's inbox."
  type        = string
}

variable "monthly_budget_amount" {
  description = "Monthly spend threshold (in the subscription's billing currency) for the Consumption Budget alert. Sized as a guardrail against the shared QA credit running out early, not a hard cap — Azure budgets alert, they do not block spend. Set below realistic QA spend ($40-75/mo per docs/Azure/QA-ENVIRONMENT-GUIDE.md §2) so the 50% alert fires early rather than late against a fixed, time-boxed credit."
  type        = number
  default     = 100
}

locals {
  resource_prefix = "${var.project}-${var.environment}"
  common_tags = {
    project     = var.project
    environment = var.environment
    managed_by  = "terraform"
  }
}
