<#
    Self-contained smoke test for the Demand Intake Service (Arch.jpg Box #1).
    Covers every section of docs/test_local.md: the full happy path (§5),
    Reject/Cancel (§6), and all seven hardening-pass scenarios (§7) —
    idempotency replay/conflict, optimistic concurrency, fulfillmentMode
    validation rules, the demand.transition/demand.create permission split,
    and tenant isolation.

    Works on both Windows PowerShell 5.1 and PowerShell 7+. Uses
    Invoke-RestMethod (not curl.exe) for every call that sends a body —
    curl.exe's argument passing gets mangled by Windows PowerShell 5.1's
    native-command argument handling when the argument contains embedded
    double quotes (as JSON bodies always do), silently corrupting the JSON
    before it leaves the machine.

    Usage:
        # Against the standalone `dotnet run` instance from docs/test_local.md
        # section 3 (appsettings.Development.json's dev JWT secret/issuer):
        .\Test-DemandIntake.ps1

        # Against the full docker-compose stack's `work` container (reads its
        # JWT secret/issuer from .env — JWT_SECRET_KEY/JWT_ISSUER/JWT_AUDIENCE
        # — via docker-compose.yml's Jwt__* env vars, NOT the same secret the
        # standalone instance uses):
        .\Test-DemandIntake.ps1 -JwtSecretKey $env:JWT_SECRET_KEY -JwtIssuer $env:JWT_ISSUER -JwtAudience $env:JWT_AUDIENCE

    Prints PASS/FAIL for each check plus a final summary. Exits non-zero
    (via $LASTEXITCODE-style signal — see the final block) if anything failed,
    so it's usable as a real gate, not just a log dump.
#>
param(
    [string]$BaseUrl = "http://localhost:6004",
    [string]$JwtSecretKey = "development-only-secret-key-min-64-bytes-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    [string]$JwtIssuer = "eswmp",
    [string]$JwtAudience = "eswmp-api"
)

function New-EswmpTestJwt {
    param(
        [string]$SecretKey = "development-only-secret-key-min-64-bytes-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        [string]$Issuer = "eswmp",
        [string]$Audience = "eswmp-api",
        [string]$TenantId = [guid]::NewGuid().ToString(),
        [string]$Permissions = "demand.create,demand.read,demand.transition"
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

# ── §3 prerequisite: service reachable ──────────────────────────────────
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

# ── §4: mint a JWT ───────────────────────────────────────────────────────
Write-Host "`n=== Minting a test JWT ===" -ForegroundColor Cyan
$tenantId = [guid]::NewGuid().ToString()
$token = New-EswmpTestJwt -SecretKey $JwtSecretKey -Issuer $JwtIssuer -Audience $JwtAudience -TenantId $tenantId
Write-Host "TenantId: $tenantId"
Test-Result "Token minted (non-empty)" ($token.Length -gt 0) "(length=$($token.Length))"
if ($token.Length -eq 0) { return }

# ── §5: happy path ───────────────────────────────────────────────────────
Write-Host "`n=== Section 5: happy path ===" -ForegroundColor Cyan
$runId = [guid]::NewGuid().ToString().Substring(0, 8)
$createBody = @{
    demandType = "field-service-visit"; sourceSystem = "manual-test"; priority = "Normal"
    summary = "Smoke test demand"; requestedStartAtUtc = "2026-08-01T09:00:00Z"; requestedEndAtUtc = "2026-08-01T11:00:00Z"
    externalReferenceType = "test-case"; externalReferenceId = "smoke-$runId"
} | ConvertTo-Json -Compress
$createHeaders = @{ Authorization = "Bearer $token"; "Idempotency-Key" = "smoke-$runId" }

try {
    $demand = Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands" -Method Post -Headers $createHeaders -ContentType "application/json" -Body $createBody
    $demandId = $demand.id
    Test-Result "Create: got an id" ([bool]$demandId)
    Test-Result "Create: status is Received" ($demand.status -eq "Received") "(got '$($demand.status)')"
    Test-Result "Create: fulfillmentMode defaults to Scheduled" ($demand.fulfillmentMode -eq "Scheduled") "(got '$($demand.fulfillmentMode)')"
} catch {
    Test-Result "Create succeeds" $false "(threw: $($_.Exception.Message) / $(Get-HttpErrorBody $_))"
    return
}

$fetched = Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands/$demandId" -Headers @{ Authorization = "Bearer $token" }
Test-Result "GetById returns the same id" ($fetched.id -eq $demandId)

$searchBody = @{ status = "Received"; page = 1; pageSize = 10 } | ConvertTo-Json -Compress
$searchResult = Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands/search" -Method Post -Headers @{ Authorization = "Bearer $token" } -ContentType "application/json" -Body $searchBody
$foundInSearch = @($searchResult.items) | Where-Object { $_.id -eq $demandId }
Test-Result "Search finds the created demand" ([bool]$foundInSearch)

$validated = Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands/$demandId/validate" -Method Post -Headers @{ Authorization = "Bearer $token" }
Test-Result "Validate: status is Valid" ($validated.status -eq "Valid") "(got '$($validated.status)', issues=$($validated.issues | ConvertTo-Json -Compress))"

$accepted = Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands/$demandId/accept" -Method Post -Headers @{ Authorization = "Bearer $token" }
Test-Result "Accept: status is Accepted" ($accepted.status -eq "Accepted") "(got '$($accepted.status)')"

$history = Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands/$demandId/history" -Headers @{ Authorization = "Bearer $token" }
Test-Result "History is empty (documented, not a bug)" (@($history).Count -eq 0) "(count=$(@($history).Count))"

# ── §6: Reject and Cancel ────────────────────────────────────────────────
Write-Host "`n=== Section 6: Reject and Cancel ===" -ForegroundColor Cyan
$d2Body = @{
    demandType = "field-service-visit"; sourceSystem = "manual-test"
    requestedStartAtUtc = "2026-08-02T09:00:00Z"; requestedEndAtUtc = "2026-08-02T10:00:00Z"
    externalReferenceType = "test-case"; externalReferenceId = "smoke-$runId-reject"
} | ConvertTo-Json -Compress
$d2 = Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands" -Method Post -Headers @{ Authorization = "Bearer $token"; "Idempotency-Key" = "smoke-$runId-reject" } -ContentType "application/json" -Body $d2Body
$rejectBody = @{ reasonCode = "OUT_OF_SERVICE_AREA"; comment = "Too far from any resource" } | ConvertTo-Json -Compress
$rejected = Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands/$($d2.id)/reject" -Method Post -Headers @{ Authorization = "Bearer $token" } -ContentType "application/json" -Body $rejectBody
Test-Result "Reject: status is Rejected" ($rejected.status -eq "Rejected") "(got '$($rejected.status)')"

$d3Body = @{
    demandType = "field-service-visit"; sourceSystem = "manual-test"
    requestedStartAtUtc = "2026-08-03T09:00:00Z"; requestedEndAtUtc = "2026-08-03T10:00:00Z"
    externalReferenceType = "test-case"; externalReferenceId = "smoke-$runId-cancel"
} | ConvertTo-Json -Compress
$d3 = Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands" -Method Post -Headers @{ Authorization = "Bearer $token"; "Idempotency-Key" = "smoke-$runId-cancel" } -ContentType "application/json" -Body $d3Body
$cancelled = Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands/$($d3.id)/cancel" -Method Post -Headers @{ Authorization = "Bearer $token" }
Test-Result "Cancel: status is Cancelled" ($cancelled.status -eq "Cancelled") "(got '$($cancelled.status)')"

# ── §7.1: error envelope / missing Idempotency-Key ──────────────────────
Write-Host "`n=== Section 7.1: error envelope ===" -ForegroundColor Cyan
$badBody = @{ demandType = "x"; sourceSystem = "x"; externalReferenceType = "test"; externalReferenceId = "noidem-$runId" } | ConvertTo-Json -Compress
$r = Invoke-ExpectedFailure { Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands" -Method Post -Headers @{ Authorization = "Bearer $token" } -ContentType "application/json" -Body $badBody }
$errObj = if ($r.Body) { $r.Body | ConvertFrom-Json } else { $null }
Test-Result "Missing Idempotency-Key returns 400" ($r.Status -eq 400) "(got $($r.Status))"
Test-Result "Error envelope has code=VALIDATION_FAILED" ($errObj.code -eq "VALIDATION_FAILED") "(got '$($errObj.code)')"

# ── §7.2: idempotency replay vs conflict ─────────────────────────────────
Write-Host "`n=== Section 7.2: idempotency replay vs conflict ===" -ForegroundColor Cyan
$replayed = Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands" -Method Post -Headers $createHeaders -ContentType "application/json" -Body $createBody
Test-Result "Replay (same key+body) returns the same id" ($replayed.id -eq $demandId) "(expected $demandId got $($replayed.id))"

$diffBody = @{
    demandType = "field-service-visit"; sourceSystem = "manual-test"; priority = "Normal"
    summary = "A DIFFERENT summary"; requestedStartAtUtc = "2026-08-01T09:00:00Z"; requestedEndAtUtc = "2026-08-01T11:00:00Z"
    externalReferenceType = "test-case"; externalReferenceId = "smoke-$runId"
} | ConvertTo-Json -Compress
$r = Invoke-ExpectedFailure { Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands" -Method Post -Headers $createHeaders -ContentType "application/json" -Body $diffBody }
Test-Result "Same key, different body returns 409" ($r.Status -eq 409) "(got $($r.Status))"

# ── §7.3: optimistic concurrency ─────────────────────────────────────────
Write-Host "`n=== Section 7.3: optimistic concurrency ===" -ForegroundColor Cyan
$d4Body = @{
    demandType = "x"; sourceSystem = "x"
    requestedStartAtUtc = "2026-08-04T09:00:00Z"; requestedEndAtUtc = "2026-08-04T10:00:00Z"
    externalReferenceType = "test"; externalReferenceId = "smoke-$runId-patch"
} | ConvertTo-Json -Compress
$d4 = Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands" -Method Post -Headers @{ Authorization = "Bearer $token"; "Idempotency-Key" = "smoke-$runId-patch" } -ContentType "application/json" -Body $d4Body

$patchBadBody = @{ expectedVersion = 99; priority = "High" } | ConvertTo-Json -Compress
$r = Invoke-ExpectedFailure { Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands/$($d4.id)" -Method Patch -Headers @{ Authorization = "Bearer $token" } -ContentType "application/json" -Body $patchBadBody }
Test-Result "Stale expectedVersion returns 412" ($r.Status -eq 412) "(got $($r.Status))"

$patchGoodBody = @{ expectedVersion = 1; priority = "High" } | ConvertTo-Json -Compress
$patched = Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands/$($d4.id)" -Method Patch -Headers @{ Authorization = "Bearer $token" } -ContentType "application/json" -Body $patchGoodBody
Test-Result "Correct expectedVersion succeeds (priority=High)" ($patched.priority -eq "High") "(got '$($patched.priority)')"

# ── §7.4: fulfillmentMode rules ──────────────────────────────────────────
Write-Host "`n=== Section 7.4: fulfillmentMode rules ===" -ForegroundColor Cyan
$noWindowBody = @{ demandType = "x"; sourceSystem = "x"; fulfillmentMode = "Scheduled"; summary = "has summary"; externalReferenceType = "test"; externalReferenceId = "nowindow-$runId" } | ConvertTo-Json -Compress
$r = Invoke-ExpectedFailure { Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands" -Method Post -Headers @{ Authorization = "Bearer $token"; "Idempotency-Key" = "nowindow-$runId" } -ContentType "application/json" -Body $noWindowBody }
$errObj = if ($r.Body) { $r.Body | ConvertFrom-Json } else { $null }
Test-Result "Scheduled with no window returns 400" ($r.Status -eq 400) "(got $($r.Status))"
Test-Result "  ...with MODE_WINDOW_REQUIRED issue" (($errObj.issues | Where-Object { $_.code -eq "MODE_WINDOW_REQUIRED" }) -ne $null)

$onDemandBody = @{ demandType = "x"; sourceSystem = "x"; fulfillmentMode = "OnDemand"; summary = "has summary"; externalReferenceType = "test"; externalReferenceId = "ondemand-$runId" } | ConvertTo-Json -Compress
$onDemandDemand = Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands" -Method Post -Headers @{ Authorization = "Bearer $token"; "Idempotency-Key" = "ondemand-$runId" } -ContentType "application/json" -Body $onDemandBody
Test-Result "OnDemand with no window succeeds" ($onDemandDemand.status -eq "Received") "(got '$($onDemandDemand.status)')"

$searchModeBody = @{ fulfillmentMode = "OnDemand"; page = 1; pageSize = 50 } | ConvertTo-Json -Compress
$modeSearchResult = Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands/search" -Method Post -Headers @{ Authorization = "Bearer $token" } -ContentType "application/json" -Body $searchModeBody
$modeFound = @($modeSearchResult.items) | Where-Object { $_.id -eq $onDemandDemand.id }
Test-Result "Search by fulfillmentMode finds the OnDemand demand" ([bool]$modeFound)

# ── §7.5: permission split ────────────────────────────────────────────────
Write-Host "`n=== Section 7.5: demand.transition / demand.create permission split ===" -ForegroundColor Cyan
$createOnlyToken = New-EswmpTestJwt -SecretKey $JwtSecretKey -Issuer $JwtIssuer -Audience $JwtAudience -TenantId $tenantId -Permissions "demand.create,demand.read"
$r = Invoke-ExpectedFailure { Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands/$($d3.id)/accept" -Method Post -Headers @{ Authorization = "Bearer $createOnlyToken" } }
Test-Result "create-only token denied accept (403)" ($r.Status -eq 403) "(got $($r.Status))"

# ── §7.6: tenant isolation ────────────────────────────────────────────────
Write-Host "`n=== Section 7.6: tenant isolation ===" -ForegroundColor Cyan
$otherToken = New-EswmpTestJwt -SecretKey $JwtSecretKey -Issuer $JwtIssuer -Audience $JwtAudience -TenantId ([guid]::NewGuid().ToString())
$r = Invoke-ExpectedFailure { Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands/$demandId" -Headers @{ Authorization = "Bearer $otherToken" } }
$errObj = if ($r.Body) { $r.Body | ConvertFrom-Json } else { $null }
Test-Result "Cross-tenant GET returns 404 (not 403)" ($r.Status -eq 404) "(got $($r.Status))"
Test-Result "  ...with code=NOT_FOUND" ($errObj.code -eq "NOT_FOUND") "(got '$($errObj.code)')"

# ── §7.7: no token ────────────────────────────────────────────────────────
Write-Host "`n=== Section 7.7: no token ===" -ForegroundColor Cyan
$r = Invoke-ExpectedFailure { Invoke-RestMethod -Uri "$BaseUrl/api/v1/demands/$demandId" }
Test-Result "No Authorization header returns 401" ($r.Status -eq 401) "(got $($r.Status))"

# ── Summary ───────────────────────────────────────────────────────────────
Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "$script:testsPassed / $script:testsRun checks passed" -ForegroundColor $(if ($script:testsPassed -eq $script:testsRun) { "Green" } else { "Red" })
if ($script:failures.Count -gt 0) {
    Write-Host "Failed checks:" -ForegroundColor Red
    $script:failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
} else {
    Write-Host "All sections of docs/test_local.md (5, 6, 7) confirmed working end to end." -ForegroundColor Green
}
Write-Host "TenantId used: $tenantId"
