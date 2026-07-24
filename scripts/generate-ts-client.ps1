#Requires -Version 7.0
<#
.SYNOPSIS
    Generates TypeScript type definitions from all OpenAPI contracts using openapi-typescript.
    Run from the repo root: .\scripts\generate-ts-client.ps1
    Output lands in clients/generated/ — there is no frontend in this repo yet;
    a future consuming app (or a dedicated ESWMP admin console) will import from there.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot    = Split-Path $PSScriptRoot -Parent
$ContractsDir = Join-Path $RepoRoot 'contracts\openapi'
$OutputDir   = Join-Path $RepoRoot 'clients\generated'

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

$Contracts = @(
    @{ Yaml = 'core.v1.yaml';       Out = 'core.d.ts'       },
    @{ Yaml = 'assignment.v1.yaml'; Out = 'assignment.d.ts' },
    @{ Yaml = 'rules.v1.yaml';      Out = 'rules.d.ts'      }
)

foreach ($c in $Contracts) {
    $InputPath  = Join-Path $ContractsDir $c.Yaml
    $OutputFile = Join-Path $OutputDir $c.Out

    Write-Host "Generating $($c.Out) from $($c.Yaml)..."

    npx openapi-typescript "$InputPath" --output "$OutputFile"

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to generate $($c.Out)"
        exit 1
    }

    Write-Host "  -> $OutputFile" -ForegroundColor Green
}

Write-Host "`nAll TypeScript clients generated successfully." -ForegroundColor Cyan
