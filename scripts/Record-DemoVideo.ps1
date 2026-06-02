#Requires -Version 7
<#
  Generates automatic demo videos under docs/automation/demo-video-light.webm and demo-video-dark.webm.
  Requires running web app (e.g. at -BaseUrl).

  Example:
    pwsh ./scripts/Record-DemoVideo.ps1
    pwsh ./scripts/Record-DemoVideo.ps1 -BaseUrl "http://127.0.0.1:5031"
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = "http://127.0.0.1:5050"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$docsAutomation = Join-Path $repoRoot "docs" "automation"
$videoOutDir = $docsAutomation

Write-Host "== Building E2E test project..."
dotnet build (Join-Path $repoRoot "tests" "App.Web.E2E" "App.Web.E2E.csproj") --configuration Release

$pw = Join-Path $repoRoot "tests" "App.Web.E2E" "bin" "Release" "net10.0" "playwright.ps1"
if (Test-Path $pw) {
    & $pw install chromium
}

Write-Host "== Generating demo videos at $BaseUrl..."
$env:E2E_BASE_URL = $BaseUrl
$env:E2E_VIDEO_OUT_DIR = $videoOutDir

try {
    dotnet test (Join-Path $repoRoot "tests" "App.Web.E2E" "App.Web.E2E.csproj") `
        --configuration Release --no-build `
        --filter "FullyQualifiedName~DemoVideoGenerator"
}
catch {
    Write-Warning "Video generation failed (is App.Web running at $BaseUrl with seeded demo guest?): $_"
}
finally {
    Remove-Item Env:E2E_BASE_URL -ErrorAction SilentlyContinue
    Remove-Item Env:E2E_VIDEO_OUT_DIR -ErrorAction SilentlyContinue
}

$themes = @("light", "dark")
Write-Host "`n== Verification:"
foreach ($theme in $themes) {
    $videoPath = Join-Path $videoOutDir "demo-video-$theme.webm"
    if (Test-Path $videoPath) {
        Write-Host "   [SUCCESS] Demo video ($theme) saved to: $videoPath"
    }
    else {
        Write-Warning "   [FAILED] Demo video ($theme) was not generated."
    }
}

