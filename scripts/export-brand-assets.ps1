# Exports Habitinator brand raster assets from SVG masters in src/App.Web/wwwroot/brand/.
# Requires: .NET SDK, scripts/BrandExporter, and Node.js, npx @resvg/resvg-js-cli for OG wordmark fonts.
# Optional: ImageMagick, magick, for multi-size .ico.
$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$BrandDir = Join-Path $RepoRoot "src\App.Web\wwwroot\brand"
$WebRoot = Join-Path $RepoRoot "src\App.Web\wwwroot"
$ExporterProj = Join-Path $PSScriptRoot "BrandExporter\BrandExporter.csproj"

function Export-Svg {
    param(
        [string]$InputSvg,
        [string]$OutputFile,
        [int]$Width,
        [int]$Height = $Width
    )
    $outDir = Split-Path -Parent $OutputFile
    if ($outDir -and -not (Test-Path $outDir)) {
        New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    }
    dotnet run --project $ExporterProj -c Release -- `
        $InputSvg $OutputFile $Width $Height
    if ($LASTEXITCODE -ne 0) { throw "Export failed: $InputSvg to $OutputFile" }
}

function Export-WordmarkOg {
    param(
        [string]$InputSvg,
        [string]$OutputFile
    )
    $brandDir = Split-Path -Parent $InputSvg
    $fontDir = Join-Path $brandDir "fonts"
    if (-not (Test-Path $fontDir)) {
        throw "Missing font directory: $fontDir"
    }
    Push-Location $brandDir
    try {
        npx --yes @resvg/resvg-js-cli `
            --fit-width 1200 `
            --font-dir "fonts" `
            --font-file "fonts/PlusJakartaSans-Bold.woff2" `
            --font-file "fonts/PlusJakartaSans-Medium.woff2" `
            --font-default-family "Plus Jakarta Sans" `
            (Split-Path -Leaf $InputSvg) $OutputFile
        if ($LASTEXITCODE -ne 0) { throw "OG export failed: $InputSvg to $OutputFile" }
    }
    finally {
        Pop-Location
    }
}

function Write-FaviconIco {
    param([string[]]$PngPaths, [string]$IcoPath)
    if (Get-Command magick -ErrorAction SilentlyContinue) {
        magick $PngPaths -define icon:auto-resize=16,32,48 $IcoPath
        return
    }
    Add-Type -AssemblyName System.Drawing
    $images = @()
    foreach ($p in $PngPaths) {
        $images += [System.Drawing.Image]::FromFile($p)
    }
    try {
        $iconHandle = [System.Drawing.Icon]::FromHandle($images[-1].GetHicon())
        $fs = [System.IO.File]::Create($IcoPath)
        $iconHandle.Save($fs)
        $fs.Close()
    }
    finally {
        foreach ($img in $images) { $img.Dispose() }
    }
}

Write-Host "Exporting web icons..."
$iconApp = Join-Path $BrandDir "icon-app.svg"
$iconMask = Join-Path $BrandDir "icon-maskable.svg"
$wordmark = Join-Path $BrandDir "wordmark-og.svg"

$tmp = Join-Path $env:TEMP "habitinator-brand-export"
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
New-Item -ItemType Directory -Path $tmp | Out-Null

Export-Svg $iconApp (Join-Path $tmp "favicon-16.png") 16
Export-Svg $iconApp (Join-Path $tmp "favicon-32.png") 32
Export-Svg $iconApp (Join-Path $tmp "favicon-48.png") 48
Export-Svg $iconApp (Join-Path $WebRoot "favicon.png") 192
Export-Svg $iconApp (Join-Path $WebRoot "apple-touch-icon.png") 180
Export-Svg $iconMask (Join-Path $WebRoot "icons\icon-maskable-512.png") 512
Write-Host "Exporting OG wordmark (Plus Jakarta Sans via resvg-js)..."
Export-WordmarkOg $wordmark (Join-Path $WebRoot "og-image.png")

Write-FaviconIco @(
    (Join-Path $tmp "favicon-16.png"),
    (Join-Path $tmp "favicon-32.png"),
    (Join-Path $tmp "favicon-48.png")
) (Join-Path $WebRoot "favicon.ico")

Copy-Item (Join-Path $BrandDir "icon-app.svg") (Join-Path $WebRoot "favicon.svg") -Force

Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Done. Updated $WebRoot"
