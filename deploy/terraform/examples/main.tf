terraform {
  required_providers {
    directorynet = {
      source  = "directorynet/directorynet"
      version = "~> 0.1"
    }
  }
}

provider "directorynet" {
  endpoint = "https://dc1.example.com"
  api_key  = var.directorynet_api_key
}

variable "directorynet_api_key" {
  type      = string
  sensitive = true
}

variable "domain_dn" {
  type    = string
  default = "DC=example,DC=com"
}

# Create an organizational unit for the engineering team
resource "directorynet_ou" "engineering" {
  name             = "Engineering"
  parent_dn        = var.domain_dn
  description      = "Engineering department"
  protect_deletion = true
}

# Create a sub-OU for developers
resource "directorynet_ou" "developers" {
  name             = "Developers"
  parent_dn        = directorynet_ou.engineering.dn
  description      = "Software developers"
  protect_deletion = true
}

# Create a user in the developers OU
resource "directorynet_user" "jane_doe" {
  sam_account_name = "jane.doe"
  display_name     = "Jane Doe"
  given_name       = "Jane"
  surname          = "Doe"
  email            = "jane.doe@example.com"
  upn              = "jane.doe@example.com"
  ou               = directorynet_ou.developers.dn
  enabled          = true
}

# Create a security group for the engineering team
resource "directorynet_group" "engineering_team" {
  sam_account_name = "Engineering-Team"
  display_name     = "Engineering Team"
  description      = "All engineering staff"
  ou               = directorynet_ou.engineering.dn
  group_scope      = "Global"
  group_category   = "Security"
  members          = [directorynet_user.jane_doe.dn]
}

# Create a computer account
resource "directorynet_computer" "dev_workstation" {
  sam_account_name = "DEV-WS-001$"
  description      = "Jane's development workstation"
  ou               = directorynet_ou.developers.dn
  enabled          = true
}

# Create a Group Policy Object
resource "directorynet_gpo" "engineering_policy" {
  display_name = "Engineering Workstation Policy"
  description  = "Security baseline for engineering workstations"
  status       = "AllSettingsEnabled"
  links        = [directorynet_ou.engineering.dn]
}

# Create DNS records for an internal service
resource "directorynet_dns_record" "app_a_record" {
  zone  = "example.com"
  name  = "app"
  type  = "A"
  value = "10.0.1.50"
  ttl   = 3600
}

resource "directorynet_dns_record" "app_cname" {
  zone  = "example.com"
  name  = "myapp"
  type  = "CNAME"
  value = "app.example.com"
  ttl   = 3600
}

# Outputs
output "engineering_ou_dn" {
  value = directorynet_ou.engineering.dn
}

output "jane_doe_dn" {
  value = directorynet_user.jane_doe.dn
}
