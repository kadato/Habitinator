#Requires -Version 7
# Writes docs/automation/screenshots/04-statistics-readme.png (top 50% height of 04-statistics.png).
param(
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"
$shotDir = Join-Path $RepoRoot "docs" "automation" "screenshots"
$srcPath = Join-Path $shotDir "04-statistics.png"
$dstPath = Join-Path $shotDir "04-statistics-readme.png"

if (-not (Test-Path $srcPath)) {
    Write-Warning "Missing $srcPath — skip README statistics crop."
    exit 0
}

Add-Type -AssemblyName System.Drawing
$img = $null
$bmp = $null
$g = $null
try {
    $img = [System.Drawing.Image]::FromFile($srcPath)
    $cropH = [int][Math]::Floor($img.Height / 2)
    $cropW = $img.Width
    $bmp = New-Object System.Drawing.Bitmap $cropW, $cropH
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $srcRect = New-Object System.Drawing.Rectangle 0, 0, $cropW, $cropH
    $dstRect = New-Object System.Drawing.Rectangle 0, 0, $cropW, $cropH
    $g.DrawImage($img, $dstRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
    $bmp.Save($dstPath, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "Wrote $dstPath (${cropW}x${cropH} from top half of statistics screenshot)"
}
finally {
    if ($null -ne $g) { $g.Dispose() }
    if ($null -ne $bmp) { $bmp.Dispose() }
    if ($null -ne $img) { $img.Dispose() }
}
