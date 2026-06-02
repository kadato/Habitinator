#Requires -Version 7
<#
  Generates automatic demo videos under docs/automation/demo-video-light.mp4 and demo-video-dark.mp4 (transcoded from WebM).
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
Write-Host "`n== Transcoding and Verification:"
foreach ($theme in $themes) {
    $webmPath = Join-Path $videoOutDir "demo-video-$theme.webm"
    $mp4Path = Join-Path $videoOutDir "demo-video-$theme.mp4"
    if (Test-Path $webmPath) {
        Write-Host "   Transcoding $theme webm to mp4 using ffmpeg..."
        # Use ffmpeg to transcode. -y overwrites existing output.
        & ffmpeg -y -i $webmPath -c:v libx264 -pix_fmt yuv420p $mp4Path
        if (Test-Path $mp4Path) {
            Write-Host "   [SUCCESS] Demo video ($theme) saved as MP4 to: $mp4Path"
            # Remove the webm file to avoid duplicates/confusion
            Remove-Item $webmPath -Force
        }
        else {
            Write-Error "   [FAILED] Transcoding failed for demo video ($theme)."
        }
    }
    else {
        Write-Warning "   [FAILED] Demo video ($theme) webm source was not generated."
    }
}


