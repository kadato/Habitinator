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
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$docsAutomation = Join-Path $repoRoot "docs" "automation"
$shotDir = Join-Path $docsAutomation "screenshots"

New-Item -ItemType Directory -Path $shotDir -Force | Out-Null

function Sync-ReadmeMermaidEmbeds {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RepoRootPath
    )

    $readmePath = Join-Path $RepoRootPath "README.md"
    if (-not (Test-Path $readmePath)) {
        Write-Warning "README.md not found; skip Mermaid embed sync."
        return
    }

    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    $text = [System.IO.File]::ReadAllText($readmePath)

    $sections = @(
        @{ Id = "solution-graph"; File = "solution-graph.mmd" }
        @{ Id = "database-schema"; File = "database-schema.mmd" }
    )

    foreach ($s in $sections) {
        $mmdPath = Join-Path $RepoRootPath "docs" "automation" $s.File
        if (-not (Test-Path $mmdPath)) {
            Write-Warning "Skip README embed '$($s.Id)': missing $mmdPath"
            continue
        }

        $body = [System.IO.File]::ReadAllText($mmdPath).TrimEnd()
        $nl = [Environment]::NewLine
        $fence = '```mermaid' + $nl + $body + $nl + '```'
        $replacement = '<!-- HABITINATOR_MERMAID_BEGIN:' + $s.Id + ' -->' + $nl + $fence + $nl + '<!-- HABITINATOR_MERMAID_END:' + $s.Id + ' -->'

        $escapedId = [regex]::Escape($s.Id)
        $pattern = '(?s)<!-- HABITINATOR_MERMAID_BEGIN:' + $escapedId + ' -->\s*```mermaid\s*.*?```\s*<!-- HABITINATOR_MERMAID_END:' + $escapedId + ' -->'

        $newText = [regex]::Replace($text, $pattern, $replacement)
        if ($newText -ceq $text) {
            Write-Warning "README: marker block '$($s.Id)' not found or pattern mismatch; section not updated."
        }
        else {
            $text = $newText
        }
    }

    [System.IO.File]::WriteAllText($readmePath, $text, $utf8NoBom)
    Write-Host "== README Mermaid embeds synced from docs/automation/*.mmd"
}

Write-Host "== Diagrams (Mermaid) -> $docsAutomation"
dotnet build (Join-Path $repoRoot "tools" "Habitinator.Diagrams" "Habitinator.Diagrams.csproj") --configuration Release
dotnet run --project (Join-Path $repoRoot "tools" "Habitinator.Diagrams" "Habitinator.Diagrams.csproj") `
    --configuration Release --no-build -- `
    "$repoRoot" "$docsAutomation"

Sync-ReadmeMermaidEmbeds -RepoRootPath $repoRoot

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

Write-Host "Done. Review changes under docs/automation/ and README.md, then commit."
