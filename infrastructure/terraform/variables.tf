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
  description = "JWT signing secret used to validate tokens issued by consuming products (minimum 32 characters)"
  type        = string
  sensitive   = true
}

locals {
  resource_prefix = "${var.project}-${var.environment}"
  common_tags = {
    project     = var.project
    environment = var.environment
    managed_by  = "terraform"
  }
}
