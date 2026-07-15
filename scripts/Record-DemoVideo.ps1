#Requires -Version 7
<#
  Records, transcodes, and converts demo videos for both dark and light themes.
  Outputs:
    docs/automation/demo-video-dark.mp4  + demo-video-dark.gif
    docs/automation/demo-video-light.mp4 + demo-video-light.gif
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

$pw = Join-Path $repoRoot "tests" "App.Web.E2E" "bin" "Release" "net11.0" "playwright.ps1"
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

$themes = @("dark", "light")
Write-Host "`n== Transcoding and GIF conversion:"
foreach ($theme in $themes) {
    $webmPath = Join-Path $videoOutDir "demo-video-$theme.webm"
    $mp4Path  = Join-Path $videoOutDir "demo-video-$theme.mp4"
    $gifPath  = Join-Path $videoOutDir "demo-video-$theme.gif"

    if (Test-Path $webmPath) {
        Write-Host "   Transcoding $theme webm -> mp4..."
        & ffmpeg -y -i $webmPath -c:v libx264 -pix_fmt yuv420p $mp4Path
        if (Test-Path $mp4Path) {
            Write-Host "   [OK] mp4: $mp4Path"
            Remove-Item $webmPath -Force
        } else {
            Write-Error "   [FAIL] Transcoding failed for $theme."
            continue
        }
    }

    if (Test-Path $mp4Path) {
        Write-Host "   Converting $theme mp4 -> gif (720p, 8fps, optimized palette)..."
        & ffmpeg -y -ss 1.5 -i $mp4Path `
            -vf "fps=8,scale=720:-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=128[p];[s1][p]paletteuse=dither=bayer" `
            -loop 0 $gifPath
        if (Test-Path $gifPath) {
            $sizeMB = [math]::Round((Get-Item $gifPath).Length / 1MB, 1)
            Write-Host "   [OK] gif: $gifPath ($sizeMB MB)"
        } else {
            Write-Error "   [FAIL] GIF conversion failed for $theme."
        }
    } else {
        Write-Warning "   [SKIP] No mp4 found for $theme, skipping GIF conversion."
    }
}
