#Requires -Version 7.0
<#
.SYNOPSIS
    Generates C# HTTP clients from all OpenAPI contracts using NSwag CLI.
    Run from the repo root: .\scripts\generate-csharp-clients.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$ContractsDir = Join-Path $RepoRoot 'contracts\openapi'
$OutputDir = Join-Path $RepoRoot 'src\Eswmp.Shared\Generated'

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

$Contracts = @(
    @{ Yaml = 'core.v1.yaml';       Namespace = 'Eswmp.Shared.Generated.Core';       ClassName = 'CoreClient'       },
    @{ Yaml = 'assignment.v1.yaml'; Namespace = 'Eswmp.Shared.Generated.Assignment'; ClassName = 'AssignmentClient' },
    @{ Yaml = 'rules.v1.yaml';      Namespace = 'Eswmp.Shared.Generated.Rules';      ClassName = 'RulesClient'      }
)

foreach ($c in $Contracts) {
    $InputPath  = Join-Path $ContractsDir $c.Yaml
    $OutputFile = Join-Path $OutputDir "$($c.ClassName).cs"

    Write-Host "Generating $($c.ClassName) from $($c.Yaml)..."

    nswag openapi2csclient `
        /input:"$InputPath" `
        /namespace:"$($c.Namespace)" `
        /className:"$($c.ClassName)" `
        /output:"$OutputFile" `
        /generateClientInterfaces:true `
        /injectHttpClient:true `
        /disposeHttpClient:false `
        /generateExceptionClasses:true `
        /exceptionClass:EswmpApiException `
        /generateResponseClasses:true

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to generate $($c.ClassName)"
        exit 1
    }

    Write-Host "  -> $OutputFile" -ForegroundColor Green
}

Write-Host "`nAll C# clients generated successfully." -ForegroundColor Cyan
