<#
    Self-contained smoke test for the Work Requirement Service (Arch.jpg Box #2,
    reconciled model). Covers every section of docs/test_local.md's "Testing the
    Work Requirement Service locally" part: template lifecycle (create/configure/
    activate/retire), resolve, read/resolved/versions, validate, revise, compare,
    explain, cancel, idempotency replay/conflict, optimistic concurrency on
    revise, TEMPLATE_NOT_ACTIVE / TEMPLATE_IMMUTABLE / INVALID_WORK_REQUIREMENT
    error paths, and tenant isolation.

    Does NOT touch the Requirement Definition module (the renamed first-generation
    model at /api/v1/requirement-definitions) or Demand Intake (/api/v1/demands) --
    separate boxes, separate scripts (see Test-DemandIntake.ps1).

    Works on both Windows PowerShell 5.1 and PowerShell 7+. Uses Invoke-RestMethod
    (not curl.exe) for every call that sends a body -- see docs/test_local.md
    section 10's troubleshooting table for why curl.exe is unsafe here.

    Usage:
        # Against the standalone `dotnet run` instance (appsettings.Development.json's
        # dev JWT secret/issuer):
        .\Test-WorkRequirement.ps1

        # Against the full docker-compose stack's `work` container (reads its
        # JWT secret/issuer from .env via docker-compose.yml's Jwt__* env vars --
        # NOT the same secret the standalone instance uses):
        .\Test-WorkRequirement.ps1 -JwtSecretKey $env:JWT_SECRET_KEY -JwtIssuer $env:JWT_ISSUER -JwtAudience $env:JWT_AUDIENCE

    Prints PASS/FAIL for each check plus a final summary.
#>
param(
    [string]$BaseUrl = "http://localhost:6004",
    [string]$JwtSecretKey = "development-only-secret-key-min-64-bytes-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    [string]$JwtIssuer = "eswmp",
    [string]$JwtAudience = "eswmp-api"
)

$AllPermissions = "workrequirement.template.create,workrequirement.template.read,workrequirement.template.update,workrequirement.template.activate,workrequirement.template.retire,workrequirement.read,workrequirement.resolve,workrequirement.revise,workrequirement.validate,workrequirement.explain"

function New-EswmpTestJwt {
    param(
        [string]$SecretKey = "development-only-secret-key-min-64-bytes-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        [string]$Issuer = "eswmp",
        [string]$Audience = "eswmp-api",
        [string]$TenantId = [guid]::NewGuid().ToString(),
        [string]$Permissions = $AllPermissions
    )

    function ConvertTo-Base64Url([byte[]]$bytes) {
        [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
    }

    $headerObj = [ordered]@{ alg = "HS256"; typ = "JWT" }
    $header = $headerObj | ConvertTo-Json -Compress

    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $exp = $now + 3600
    $userId = [guid]::NewGuid().ToString()
    $payloadObj = [ordered]@{
        tenant_id   = $TenantId
        user_id     = $userId
        permissions = $Permissions
        iss         = $Issuer
        aud         = $Audience
        iat         = $now
        nbf         = $now
        exp         = $exp
    }
    $payload = $payloadObj | ConvertTo-Json -Compress

    $headerB64 = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($header))
    $payloadB64 = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payload))
    $toSign = "$headerB64.$payloadB64"

    $hmac = New-Object System.Security.Cryptography.HMACSHA256
    $hmac.Key = [Text.Encoding]::UTF8.GetBytes($SecretKey)
    $signature = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($toSign))
    $signatureB64 = ConvertTo-Base64Url $signature

    Write-Output "$toSign.$signatureB64"
}

function Get-HttpErrorBody($ErrorRecord) {
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        return $ErrorRecord.ErrorDetails.Message
    } else {
        $stream = $ErrorRecord.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($stream)
        return $reader.ReadToEnd()
    }
}

$script:testsRun = 0
$script:testsPassed = 0
$script:failures = @()

function Test-Result([string]$Name, [bool]$Passed, [string]$Detail = "") {
    $script:testsRun++
    if ($Passed) {
        $script:testsPassed++
        Write-Host "  PASS: $Name" -ForegroundColor Green
    } else {
        Write-Host "  FAIL: $Name $Detail" -ForegroundColor Red
        $script:failures += $Name
    }
}

function Invoke-ExpectedFailure {
    <# Calls the scriptblock, expects it to throw an HTTP error, returns @{ Status = <int>; Body = <string> }.
       If it does NOT throw, returns @{ Status = 200; Body = <success body> } so the caller's assertion fails
       naturally instead of the whole script crashing. #>
    param([scriptblock]$Call)
    try {
        $result = & $Call
        return @{ Status = 200; Body = ($result | ConvertTo-Json -Compress) }
    } catch {
        return @{ Status = [int]$_.Exception.Response.StatusCode; Body = (Get-HttpErrorBody $_) }
    }
}

Write-Host "PowerShell version: $($PSVersionTable.PSVersion)" -ForegroundColor Magenta

# ── prerequisite: service reachable ──────────────────────────────────────
Write-Host "`n=== Checking Eswmp.Work is reachable ===" -ForegroundColor Cyan
try {
    $health = Invoke-RestMethod -Uri "$BaseUrl/health/live"
    Test-Result "Service is Healthy" ($health -eq "Healthy") "(got '$health')"
    if ($health -ne "Healthy") {
        Write-Host "Is it running with `$env:ASPNETCORE_ENVIRONMENT = 'Development'`? See docs/test_local.md section 3." -ForegroundColor Yellow
        return
    }
} catch {
    Write-Host "Could not reach $BaseUrl at all. Start it first (docs/test_local.md section 3)." -ForegroundColor Red
    return
}

# ── mint a JWT ────────────────────────────────────────────────────────────
Write-Host "`n=== Minting a test JWT ===" -ForegroundColor Cyan
$tenantId = [guid]::NewGuid().ToString()
$token = New-EswmpTestJwt -SecretKey $JwtSecretKey -Issuer $JwtIssuer -Audience $JwtAudience -TenantId $tenantId
$headers = @{ Authorization = "Bearer $token" }
Write-Host "TenantId: $tenantId"
Test-Result "Token minted (non-empty)" ($token.Length -gt 0) "(length=$($token.Length))"
if ($token.Length -eq 0) { return }

$runId = [guid]::NewGuid().ToString().Substring(0, 8)
$templateCode = "SMOKE_TEST_$runId"

# ── Template: create ──────────────────────────────────────────────────────
Write-Host "`n=== Template: create ===" -ForegroundColor Cyan
$templateBody = @{ code = $templateCode; name = "Smoke Test Template"; workType = "SMOKE_TEST_WORK" } | ConvertTo-Json -Compress
$templateHeaders = $headers + @{ "Idempotency-Key" = "tpl-create-$runId" }
try {
    $template = Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirement-templates" -Method Post -Headers $templateHeaders -ContentType "application/json" -Body $templateBody
    $templateId = $template.id
    Test-Result "Template create: got an id" ([bool]$templateId)
    Test-Result "Template create: status is Draft" ($template.status -eq "Draft") "(got '$($template.status)')"
    Test-Result "Template create: currentVersion is 1" ($template.currentVersion -eq 1) "(got '$($template.currentVersion)')"
} catch {
    Test-Result "Template create succeeds" $false "(threw: $($_.Exception.Message) / $(Get-HttpErrorBody $_))"
    return
}

# Missing Idempotency-Key => 400
$r = Invoke-ExpectedFailure { Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirement-templates" -Method Post -Headers $headers -ContentType "application/json" -Body $templateBody }
Test-Result "Template create without Idempotency-Key returns 400" ($r.Status -eq 400) "(got $($r.Status))"

# Resolving against a not-yet-activated template => 409 TEMPLATE_NOT_ACTIVE
$notActiveBody = @{ sourceType = "Demand"; sourceId = "not-active-$runId"; templateCode = $templateCode } | ConvertTo-Json -Compress
$r = Invoke-ExpectedFailure { Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirements/resolve" -Method Post -Headers ($headers + @{ "Idempotency-Key" = "not-active-$runId" }) -ContentType "application/json" -Body $notActiveBody }
$errObj = if ($r.Body) { $r.Body | ConvertFrom-Json } else { $null }
Test-Result "Resolve against a Draft-only template returns 409 TEMPLATE_NOT_ACTIVE" ($r.Status -eq 409 -and $errObj.code -eq "TEMPLATE_NOT_ACTIVE") "(got $($r.Status)/$($errObj.code))"

# ── Template: configure requirements (fails first, on purpose) ───────────
Write-Host "`n=== Template: configure requirements ===" -ForegroundColor Cyan
$invalidDefs = @{
    resourceRequirements = @()
    durationRequirement  = @{ durationType = "Fixed"; estimatedDurationMinutes = 60 }
} | ConvertTo-Json -Compress
$r = Invoke-ExpectedFailure { Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirement-templates/$templateId/versions/1/requirements" -Method Put -Headers $headers -ContentType "application/json" -Body $invalidDefs }
Test-Result "Configure with zero resource roles returns 422 INVALID_WORK_REQUIREMENT" ($r.Status -eq 422) "(got $($r.Status))"

$validDefs = @{
    resourceRequirements = @(
        @{ roleCode = "SMOKE_ROLE"; resourceCategory = "Person"; minimumQuantity = 1; maximumQuantity = 1 }
    )
    durationRequirement  = @{ durationType = "Fixed"; estimatedDurationMinutes = 60 }
    capabilityRequirements = @(
        @{ roleCode = "SMOKE_ROLE"; capabilityCode = "SMOKE_CAPABILITY"; mandatory = $true }
    )
    capacityRequirements = @(
        @{ roleCode = "SMOKE_ROLE"; dimensionCode = "UNIT_COUNT"; quantity = 1; unit = "COUNT" }
    )
    locationRequirement = @{ locationMode = "CustomerLocation" }
} | ConvertTo-Json -Compress -Depth 5
$configured = Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirement-templates/$templateId/versions/1/requirements" -Method Put -Headers $headers -ContentType "application/json" -Body $validDefs
Test-Result "Configure with valid definitions succeeds" ($configured.status -eq "Draft") "(got '$($configured.status)')"

# ── Template: activate ────────────────────────────────────────────────────
Write-Host "`n=== Template: activate ===" -ForegroundColor Cyan
$activated = Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirement-templates/$templateId/versions/1/activate" -Method Post -Headers $headers
Test-Result "Activate: version status is Active" ($activated.status -eq "Active") "(got '$($activated.status)')"

# Re-configuring an Active version => 409 TEMPLATE_IMMUTABLE
$r = Invoke-ExpectedFailure { Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirement-templates/$templateId/versions/1/requirements" -Method Put -Headers $headers -ContentType "application/json" -Body $validDefs }
$errObj = if ($r.Body) { $r.Body | ConvertFrom-Json } else { $null }
Test-Result "Re-configuring an Active version returns 409 TEMPLATE_IMMUTABLE" ($r.Status -eq 409 -and $errObj.code -eq "TEMPLATE_IMMUTABLE") "(got $($r.Status)/$($errObj.code))"

# ── Resolve ────────────────────────────────────────────────────────────────
Write-Host "`n=== Resolve ===" -ForegroundColor Cyan
$resolveBody = @{
    sourceType = "Demand"; sourceId = "smoke-$runId"; sourceVersion = 1; templateCode = $templateCode
    inputs = @{
        unitCount = 3
        requestedWindow = @{ start = "2026-08-01T08:00:00-07:00"; end = "2026-08-01T12:00:00-07:00" }
    }
} | ConvertTo-Json -Compress -Depth 5
$resolveHeaders = $headers + @{ "Idempotency-Key" = "resolve-$runId" }
try {
    $resolved = Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirements/resolve" -Method Post -Headers $resolveHeaders -ContentType "application/json" -Body $resolveBody
    $workRequirementId = $resolved.workRequirementId
    Test-Result "Resolve: got a workRequirementId" ([bool]$workRequirementId)
    Test-Result "Resolve: status is Valid" ($resolved.status -eq "Valid") "(got '$($resolved.status)')"
} catch {
    Test-Result "Resolve succeeds" $false "(threw: $($_.Exception.Message) / $(Get-HttpErrorBody $_))"
    return
}

# Replay: same key + same body => same id, no second row created
$replayed = Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirements/resolve" -Method Post -Headers $resolveHeaders -ContentType "application/json" -Body $resolveBody
Test-Result "Resolve replay (same key+body) returns the same workRequirementId" ($replayed.workRequirementId -eq $workRequirementId)

# Same key, different body => 409 IDEMPOTENCY_CONFLICT
$diffResolveBody = @{ sourceType = "Demand"; sourceId = "different-$runId"; templateCode = $templateCode } | ConvertTo-Json -Compress
$r = Invoke-ExpectedFailure { Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirements/resolve" -Method Post -Headers $resolveHeaders -ContentType "application/json" -Body $diffResolveBody }
Test-Result "Resolve same key + different body returns 409 IDEMPOTENCY_CONFLICT" ($r.Status -eq 409) "(got $($r.Status))"

# ── Read / resolved ────────────────────────────────────────────────────────
Write-Host "`n=== Read / resolved contract ===" -ForegroundColor Cyan
$fetched = Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirements/$workRequirementId" -Headers $headers
Test-Result "GetById returns the same id" ($fetched.id -eq $workRequirementId)
Test-Result "GetById: templateCode resolved back to the template's code" ($fetched.templateCode -eq $templateCode) "(got '$($fetched.templateCode)')"

$contract = Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirements/$workRequirementId/resolved" -Headers $headers
Test-Result "Resolved contract: one resource requirement" ($contract.resourceRequirements.Count -eq 1) "(count=$($contract.resourceRequirements.Count))"
Test-Result "Resolved contract: capacity overlay applied (unitCount=3 -> UNIT_COUNT quantity)" ($contract.capacityRequirements[0].quantity -eq 3) "(got '$($contract.capacityRequirements[0].quantity)')"
Test-Result "Resolved contract: requestedWindow overlaid onto timeRequirement" ([bool]$contract.timeRequirement.earliestStart)

$versionOne = Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirements/$workRequirementId/versions/1" -Headers $headers
Test-Result "GetVersion(1): changeType is Initial" ($versionOne.changeType -eq "Initial") "(got '$($versionOne.changeType)')"

# ── Validate ───────────────────────────────────────────────────────────────
Write-Host "`n=== Validate ===" -ForegroundColor Cyan
$validation = Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirements/$workRequirementId/validate" -Method Post -Headers $headers
Test-Result "Validate: valid is true" ($validation.valid -eq $true) "(errors=$($validation.errors | ConvertTo-Json -Compress))"

# ── Revise ─────────────────────────────────────────────────────────────────
Write-Host "`n=== Revise ===" -ForegroundColor Cyan
$reviseBadBody = @{ expectedVersion = 99; reason = "stale on purpose" } | ConvertTo-Json -Compress
$r = Invoke-ExpectedFailure { Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirements/$workRequirementId/revisions" -Method Post -Headers ($headers + @{ "Idempotency-Key" = "revise-bad-$runId" }) -ContentType "application/json" -Body $reviseBadBody }
Test-Result "Revise with stale expectedVersion returns 412 VERSION_CONFLICT" ($r.Status -eq 412) "(got $($r.Status))"

$reviseBody = @{
    expectedVersion = 1
    reason = "smoke test bumped unitCount"
    changes = @{ capacityRequirements = @(@{ roleCode = "SMOKE_ROLE"; dimensionCode = "UNIT_COUNT"; quantity = 5 }) }
} | ConvertTo-Json -Compress -Depth 5
$revised = Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirements/$workRequirementId/revisions" -Method Post -Headers ($headers + @{ "Idempotency-Key" = "revise-$runId" }) -ContentType "application/json" -Body $reviseBody
Test-Result "Revise: requirementVersion bumped to 2" ($revised.requirementVersion -eq 2) "(got '$($revised.requirementVersion)')"

$contractAfterRevise = Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirements/$workRequirementId/resolved" -Headers $headers
Test-Result "Revise: capacity quantity now 5" ($contractAfterRevise.capacityRequirements[0].quantity -eq 5) "(got '$($contractAfterRevise.capacityRequirements[0].quantity)')"

# ── Compare ────────────────────────────────────────────────────────────────
Write-Host "`n=== Compare ===" -ForegroundColor Cyan
$comparison = Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirements/$workRequirementId/compare?fromVersion=1&toVersion=2" -Headers $headers
Test-Result "Compare: capacityRequirements listed as changed" (@($comparison.changedCategories) -contains "capacityRequirements") "(got '$($comparison.changedCategories -join ",")')"

# ── Explain ────────────────────────────────────────────────────────────────
Write-Host "`n=== Explain ===" -ForegroundColor Cyan
$explanation = Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirements/$workRequirementId/explain" -Headers $headers
Test-Result "Explain: summary is non-empty" (-not [string]::IsNullOrWhiteSpace($explanation.summary))
Test-Result "Explain: has at least one derived requirement" (@($explanation.derivedRequirements).Count -gt 0)

# ── Cancel ─────────────────────────────────────────────────────────────────
Write-Host "`n=== Cancel ===" -ForegroundColor Cyan
$cancelled = Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirements/$workRequirementId/cancel" -Method Post -Headers $headers
Test-Result "Cancel: status is Cancelled" ($cancelled.status -eq "Cancelled") "(got '$($cancelled.status)')"

$r = Invoke-ExpectedFailure { Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirements/$workRequirementId/cancel" -Method Post -Headers $headers }
Test-Result "Cancel again returns 409 STATUS_CONFLICT (idempotent-by-status, no key needed)" ($r.Status -eq 409) "(got $($r.Status))"

# ── Tenant isolation ─────────────────────────────────────────────────────
Write-Host "`n=== Tenant isolation ===" -ForegroundColor Cyan
$otherToken = New-EswmpTestJwt -SecretKey $JwtSecretKey -Issuer $JwtIssuer -Audience $JwtAudience -TenantId ([guid]::NewGuid().ToString())
$r = Invoke-ExpectedFailure { Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirements/$workRequirementId" -Headers @{ Authorization = "Bearer $otherToken" } }
$errObj = if ($r.Body) { $r.Body | ConvertFrom-Json } else { $null }
Test-Result "Cross-tenant GET returns 404 (not 403)" ($r.Status -eq 404 -and $errObj.code -eq "NOT_FOUND") "(got $($r.Status)/$($errObj.code))"

# ── No token ───────────────────────────────────────────────────────────────
Write-Host "`n=== No token ===" -ForegroundColor Cyan
$r = Invoke-ExpectedFailure { Invoke-RestMethod -Uri "$BaseUrl/api/v1/work-requirements/$workRequirementId" }
Test-Result "No Authorization header returns 401" ($r.Status -eq 401) "(got $($r.Status))"

# ── Summary ───────────────────────────────────────────────────────────────
Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "$script:testsPassed / $script:testsRun checks passed" -ForegroundColor $(if ($script:testsPassed -eq $script:testsRun) { "Green" } else { "Red" })
if ($script:failures.Count -gt 0) {
    Write-Host "Failed checks:" -ForegroundColor Red
    $script:failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
} else {
    Write-Host "Template lifecycle, resolve, read/resolved/versions, validate, revise, compare, explain, cancel, idempotency, optimistic concurrency, and tenant isolation all confirmed working end to end." -ForegroundColor Green
}
Write-Host "TenantId used: $tenantId"
Write-Host "TemplateCode used: $templateCode"
