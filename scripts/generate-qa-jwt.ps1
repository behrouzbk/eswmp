<#
.SYNOPSIS
    Mints a JWT for testing the QA Gateway, signed with the real QA HMAC key
    pulled live from Key Vault (eswmp-staging-kv) — never hardcode that key
    in a script or commit it anywhere. Only accounts with Key Vault access
    (the terraform-apply identity, i.e. you) can run this; hand the resulting
    token to a teammate, not the underlying secret.

    Claim shape must match Eswmp.Shared.Auth.EswmpClaimTypes / what every
    service's JWT bearer validation expects: tenant_id, user_id, permissions.

.PARAMETER TenantId
    The tenant_id claim value. Defaults to a new random GUID if omitted —
    pass a real tenant ID if you need the token to line up with existing
    seeded data.

.PARAMETER Permissions
    Comma-separated permissions claim, e.g. "reservations:read,reservations:write".
    Omit for a token with no permissions claim (fine for endpoints that only
    check tenant_id).

.PARAMETER ExpiryHours
    Token lifetime in hours. Defaults to 24 — long enough for a remote
    teammate's testing session without leaving a near-permanent credential
    floating in chat history.

.PARAMETER KeyVaultName
    Defaults to eswmp-staging-kv (QA). Override for a different environment.

.EXAMPLE
    .\scripts\generate-qa-jwt.ps1 -Permissions "reservations:read,reservations:write" -ExpiryHours 48
#>

param(
    [string]$TenantId = [guid]::NewGuid().ToString(),
    [string]$Permissions = "",
    [int]$ExpiryHours = 24,
    [string]$KeyVaultName = "eswmp-staging-kv"
)

$ErrorActionPreference = 'Stop'

function ConvertTo-Base64Url([byte[]]$Bytes) {
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

Write-Host "Fetching JWT signing config from Key Vault '$KeyVaultName'..."
$secretKey = az keyvault secret show --vault-name $KeyVaultName --name "JwtSecretKey" --query "value" -o tsv
$issuer    = az keyvault secret show --vault-name $KeyVaultName --name "JwtIssuer" --query "value" -o tsv
$audience  = az keyvault secret show --vault-name $KeyVaultName --name "JwtAudience" --query "value" -o tsv

if (-not $secretKey) {
    throw "Could not read JwtSecretKey from Key Vault '$KeyVaultName' — do you have Get/List access? (Only the terraform-apply identity does by default.)"
}

$now = [DateTimeOffset]::UtcNow
$iat = $now.ToUnixTimeSeconds()
$exp = $now.AddHours($ExpiryHours).ToUnixTimeSeconds()

$header = '{"alg":"HS256","typ":"JWT"}'

$payloadObj = [ordered]@{
    tenant_id = $TenantId
    user_id   = [guid]::NewGuid().ToString()
    iss       = $issuer
    aud       = $audience
    iat       = $iat
    exp       = $exp
}
if ($Permissions) {
    $payloadObj["permissions"] = $Permissions
}
$payload = $payloadObj | ConvertTo-Json -Compress

$headerB64  = ConvertTo-Base64Url ([System.Text.Encoding]::UTF8.GetBytes($header))
$payloadB64 = ConvertTo-Base64Url ([System.Text.Encoding]::UTF8.GetBytes($payload))
$signingInput = "$headerB64.$payloadB64"

$hmac = New-Object System.Security.Cryptography.HMACSHA256
$hmac.Key = [System.Text.Encoding]::UTF8.GetBytes($secretKey)
$signatureB64 = ConvertTo-Base64Url $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($signingInput))
$hmac.Dispose()

$token = "$signingInput.$signatureB64"

Write-Host ""
Write-Host "tenant_id : $TenantId"
Write-Host "permissions: $(if ($Permissions) { $Permissions } else { '(none)' })"
Write-Host "expires   : $($now.AddHours($ExpiryHours).ToString('u')) ($ExpiryHours h from now)"
Write-Host ""
Write-Host "Bearer token (send this to the tester, not the Key Vault secret):" -ForegroundColor Cyan
Write-Host $token
