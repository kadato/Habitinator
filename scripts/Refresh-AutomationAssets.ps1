#Requires -Version 7
<#
  Regenerates committed documentation assets under docs/automation/.
  - Mermaid: solution graph + database FK flowchart, no running server needed.
  - OpenAPI JSON + mobile screenshots: require App.Web reachable at -BaseUrl. PostgreSQL must match appsettings.

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
        Write-Warning "README.md not found. Skip Mermaid embed sync."
        return
    }

    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    $text = [System.IO.File]::ReadAllText($readmePath)

    $sections = @(
        @{ Id = "solution-graph"; File = "solution-graph.mmd" }
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
            Write-Warning "README: marker block '$($s.Id)' not found or pattern mismatch. Section not updated."
        }
        else {
            $text = $newText
        }
    }

    [System.IO.File]::WriteAllText($readmePath, $text, $utf8NoBom)
    Write-Output "== README Mermaid embeds synced from docs/automation/*.mmd"
}

Write-Host "== Diagrams (Mermaid) to $docsAutomation"
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
    Write-Warning "OpenAPI download failed. Is App.Web running at $base ? $_"
}

Write-Host "== Playwright screenshots (mobile, light+dark) to $shotDir"
& (Join-Path $repoRoot "tools" "Habitinator.Screenshots" "run.ps1") -BaseUrl $base
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Screenshot generation failed. Is App.Web running with the seeded demo guest? Exit code $LASTEXITCODE"
}

Write-Host "Done. Review changes under docs/automation/ and README.md, then commit."
