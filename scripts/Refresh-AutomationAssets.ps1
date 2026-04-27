#Requires -Version 7
<#
  Regenerates committed documentation assets under docs/automation/.
  - Mermaid: solution graph + ER diagram (no running server).
  - OpenAPI JSON + Playwright PNGs: require App.Web reachable at -BaseUrl (PostgreSQL must match appsettings).

  Example:
    pwsh ./scripts/Refresh-AutomationAssets.ps1
    pwsh ./scripts/Refresh-AutomationAssets.ps1 -BaseUrl "http://127.0.0.1:5031"
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = "http://127.0.0.1:5050"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$docsAutomation = Join-Path $repoRoot "docs" "automation"
$shotDir = Join-Path $docsAutomation "screenshots"

New-Item -ItemType Directory -Path $shotDir -Force | Out-Null

Write-Host "== Diagrams (Mermaid) -> $docsAutomation"
dotnet build (Join-Path $repoRoot "tools" "Habitinator.Diagrams" "Habitinator.Diagrams.csproj") --configuration Release
dotnet run --project (Join-Path $repoRoot "tools" "Habitinator.Diagrams" "Habitinator.Diagrams.csproj") `
    --configuration Release --no-build -- `
    "$repoRoot" "$docsAutomation"

$openApiPath = Join-Path $docsAutomation "openapi-v1.json"
$base = $BaseUrl.TrimEnd('/')
try {
    Write-Host "== OpenAPI -> $openApiPath"
    Invoke-WebRequest -Uri "$base/openapi/v1.json" -OutFile $openApiPath -UseBasicParsing
}
catch {
    Write-Warning "OpenAPI download failed (is App.Web running at $base ?): $_"
}

Write-Host "== Playwright screenshots -> $shotDir"
$env:E2E_BASE_URL = $base
$env:E2E_SCREENSHOT_DIR = $shotDir
dotnet build (Join-Path $repoRoot "tests" "App.Web.E2E" "App.Web.E2E.csproj") --configuration Release
$pw = Join-Path $repoRoot "tests" "App.Web.E2E" "bin" "Release" "net10.0" "playwright.ps1"
if (Test-Path $pw) {
    & $pw install chromium
}
else {
    Write-Warning "playwright.ps1 not found; build E2E project first."
}

try {
    dotnet test (Join-Path $repoRoot "tests" "App.Web.E2E" "App.Web.E2E.csproj") `
        --configuration Release --no-build `
        --filter "FullyQualifiedName~DocumentationScreenshotsTests"
}
catch {
    Write-Warning "Screenshot tests failed (is App.Web running with seeded demo guest?): $_"
}
finally {
    Remove-Item Env:E2E_BASE_URL -ErrorAction SilentlyContinue
    Remove-Item Env:E2E_SCREENSHOT_DIR -ErrorAction SilentlyContinue
}

Write-Host "Done. Review changes under docs/automation/ then commit."
