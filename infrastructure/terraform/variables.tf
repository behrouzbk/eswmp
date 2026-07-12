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

locals {
  resource_prefix = "${var.project}-${var.environment}"
  common_tags = {
    project     = var.project
    environment = var.environment
    managed_by  = "terraform"
  }
}
