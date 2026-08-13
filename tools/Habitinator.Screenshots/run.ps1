#!/usr/bin/env pwsh
#Requires -Version 7
<#
  Generates the mobile screenshots used in the README preview.

  Outputs PNG pairs like board-light.png and board-dark.png to docs/automation/screenshots/.
  Requires the web app to be running, for example via AppHost, with the demo guest seeded.

  Example:
    pwsh ./tools/Habitinator.Screenshots/run.ps1
    pwsh ./tools/Habitinator.Screenshots/run.ps1 -BaseUrl "http://127.0.0.1:5033"
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = "http://127.0.0.1:5050"
)

$ErrorActionPreference = "Stop"
$ScriptDir = $PSScriptRoot
$ProjectDir = Resolve-Path "$ScriptDir/../.."

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Habitinator Screenshot Generation" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Step 1: Verify the web app is reachable
$base = $BaseUrl.TrimEnd('/')
try {
    $health = Invoke-RestMethod -Uri "$base/health" -TimeoutSec 5 -UseBasicParsing
    Write-Host "[OK] Web app reachable at $base. Health: $health" -ForegroundColor Green
}
catch {
    Write-Host "[ERROR] Web app not reachable at $base. Start it first:" -ForegroundColor Red
    Write-Host "  dotnet run --project src/AppHost/AppHost.csproj"
    Write-Host "  Alternatively run App.Web against a PostgreSQL that has the demo guest seeded"
    exit 1
}

# Step 2: Build the tool and install the Playwright browser
Write-Host "`nBuilding Habitinator.Screenshots..." -ForegroundColor Yellow
Push-Location $ScriptDir
try {
    dotnet build Habitinator.Screenshots.csproj --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }

    $pw = Join-Path $ScriptDir "bin" "Release" "net11.0" "playwright.ps1"
    if (Test-Path $pw) {
        Write-Host "Ensuring Playwright chromium is installed..." -ForegroundColor Yellow
        & $pw install chromium
        if ($LASTEXITCODE -ne 0) {
            throw "playwright install failed with exit code $LASTEXITCODE"
        }
    }
    else {
        Write-Warning "playwright.ps1 not found. Build the project first."
    }
}
catch {
    Write-Host "[ERROR] Failed to prepare screenshot tool: $_" -ForegroundColor Red
    exit 1
}

# Step 3: Run the screenshot tool
Write-Host "`nRunning screenshot generation..." -ForegroundColor Yellow
try {
    $env:E2E_BASE_URL = $base
    dotnet run --project Habitinator.Screenshots.csproj --configuration Release --no-build -- "$base"
    if ($LASTEXITCODE -ne 0) {
        throw "Screenshot tool exited with code $LASTEXITCODE"
    }
    Write-Host "[SUCCESS] Screenshot generation completed!" -ForegroundColor Green
}
catch {
    Write-Host "[ERROR] Screenshot generation failed: $_" -ForegroundColor Red
    exit 1
}
finally {
    Remove-Item Env:E2E_BASE_URL -ErrorAction SilentlyContinue
    Pop-Location
}

exit 0
