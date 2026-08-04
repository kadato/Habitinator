using System.Globalization;
using System.Text.RegularExpressions;

using BrandGenerator;

// Generates every derived Habitinator brand asset from the (src/App.Shared.RCL/wwwroot/brand/mark.svg).
// Run from the repo root:
//   dotnet run --project scripts/BrandGenerator
// A consistency test (BrandMarkConsistencyTests) fails if any generated file
// drifts from the canonical mark, so edit mark.svg, run this, commit both.

var repoRoot = FindRepoRoot();
if (repoRoot is null)
{
    await Console.Error.WriteLineAsync("Repo root not found (no .git directory above the current folder).");
    return 1;
}

const string Wwwroot = "wwwroot";
const string Brand = "brand";
const string AppWeb = "App.Web";

var markPath = Path.Combine(repoRoot, "src", "App.Shared.RCL", Wwwroot, Brand, "mark.svg");
if (!File.Exists(markPath))
{
    await Console.Error.WriteLineAsync($"Canonical mark not found: {markPath}");
    return 1;
}

var markSvg = await File.ReadAllTextAsync(markPath);
var bars = ParseBars(markSvg);
if (bars.Count != 3)
{
    await Console.Error.WriteLineAsync($"Expected 3 bars in {markPath}, found {bars.Count}.");
    return 1;
}

var minX = bars.Min(b => b.X);
var maxX = bars.Max(b => b.X + b.W);
var minY = bars.Min(b => b.Y);
var maxY = bars.Max(b => b.Y + b.H);
var centerX = (minX + maxX) / 2;
var centerY = (minY + maxY) / 2;

string MarkGroup(double scale, double tx, double ty) =>
    $"<g fill=\"#ffffff\" transform=\"translate({Num(tx)} {Num(ty)}) scale({Num(scale)})\">{Rects()}</g>";

string Rects() => string.Join("", bars.Select(b => $"<rect x=\"{Num(b.X)}\" y=\"{Num(b.Y)}\" width=\"{Num(b.W)}\" height=\"{Num(b.H)}\" rx=\"{Num(b.Rx)}\"/>"));

string Tile(bool gradient) => gradient
    ? "<rect width=\"100\" height=\"100\" rx=\"22\" fill=\"url(#tile-grad)\"/>"
    : "<rect width=\"100\" height=\"100\" rx=\"22\" fill=\"#3b82f6\"/>";

string MarkSvgFile(string tile, string inner) =>
    $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\">\n  {tile}\n  {inner}\n</svg>\n";

var outputs = new (string Path, string Content)[]
{
    (Path.Combine(repoRoot, "src", "App.Shared.RCL", Wwwroot, Brand, "mark-tile.svg"),
        MarkSvgFile(Tile(gradient: true), "<g fill=\"#ffffff\">" + Rects() + "</g>")
            .Replace("<svg", "<svg width=\"100\" height=\"100\"")
            .Replace("</svg>", "<defs>\n    <linearGradient id=\"tile-grad\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\">\n      <stop offset=\"0%\" stop-color=\"#3b82f6\"/>\n      <stop offset=\"100%\" stop-color=\"#8b5cf6\"/>\n    </linearGradient>\n  </defs>\n</svg>")),

    (Path.Combine(repoRoot, "src", AppWeb, Wwwroot, "favicon.svg"),
        MarkSvgFile(Tile(gradient: false), "<g fill=\"#ffffff\">" + Rects() + "</g>")),

    (Path.Combine(repoRoot, "src", AppWeb, Wwwroot, Brand, "icon-app.svg"),
        MarkSvgFile(Tile(gradient: false), "<g fill=\"#ffffff\">" + Rects() + "</g>")),

    (Path.Combine(repoRoot, "src", AppWeb, Wwwroot, Brand, "icon-mark.svg"),
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\" role=\"img\" aria-label=\"Habitinator mark\">\n  <g fill=\"#ffffff\">{Rects()}</g>\n</svg>\n"),

    (Path.Combine(repoRoot, "src", AppWeb, Wwwroot, Brand, "icon-maskable.svg"),
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\" role=\"img\" aria-label=\"Habitinator\">\n  <rect width=\"100\" height=\"100\" fill=\"#3b82f6\"/>\n  {MarkGroup(0.72, 14, 14)}\n</svg>\n"),

    (Path.Combine(repoRoot, "src", "App.MAUI", "Resources", "AppIcon", "appicon.svg"),
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"456\" height=\"456\" viewBox=\"0 0 456 456\">\n  <rect width=\"456\" height=\"456\" fill=\"#3b82f6\"/>\n</svg>\n"),

    (Path.Combine(repoRoot, "src", "App.MAUI", "Resources", "AppIcon", "appiconfg.svg"),
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"456\" height=\"456\" viewBox=\"0 0 456 456\">\n  {MarkGroup(3, 456 / 2d - centerX * 3, 456 / 2d - centerY * 3)}\n</svg>\n"),

    (Path.Combine(repoRoot, "src", "App.MAUI", "Resources", "Splash", "splash.svg"),
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"456\" height=\"456\" viewBox=\"0 0 456 456\">\n  <rect width=\"456\" height=\"456\" fill=\"#3b82f6\"/>\n  {MarkGroup(2, 456 / 2d - centerX * 2, 456 / 2d - centerY * 2)}\n</svg>\n"),

    (Path.Combine(repoRoot, "src", AppWeb, Wwwroot, Brand, "wordmark-og.svg"),
        BuildWordmark(MarkGroup(0.6, 14, 14))),
};

static string BuildWordmark(string mark) =>
    """
<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="630" viewBox="0 0 1200 630" role="img" aria-label="Habitinator">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0%" stop-color="#0f1419"/>
      <stop offset="100%" stop-color="#111827"/>
    </linearGradient>
    <style type="text/css"><![CDATA[
      @font-face {
        font-family: 'Plus Jakarta Sans';
        font-style: normal;
        font-weight: 700;
        font-display: swap;
        src: url('fonts/PlusJakartaSans-Bold.woff2') format('woff2');
      }
      @font-face {
        font-family: 'Plus Jakarta Sans';
        font-style: normal;
        font-weight: 500;
        font-display: swap;
        src: url('fonts/PlusJakartaSans-Medium.woff2') format('woff2');
      }
      .title { font-family: 'Plus Jakarta Sans', sans-serif; font-weight: 700; }
      .tagline { font-family: 'Plus Jakarta Sans', sans-serif; font-weight: 500; }
    ]]></style>
  </defs>
  <rect width="1200" height="630" fill="url(#bg)"/>
  <circle cx="1080" cy="90" r="120" fill="#3b82f6" opacity="0.12"/>
  <circle cx="120" cy="540" r="160" fill="#3b82f6" opacity="0.08"/>

  <g transform="translate(80 80)">
    <rect width="88" height="88" rx="20" fill="#3b82f6"/>
    {MARK}
  </g>

  <text class="title" x="200" y="280" fill="#ffffff" font-size="88" letter-spacing="-1">Habitinator</text>
  <text class="tagline" x="200" y="360" fill="#94a3b8" font-size="36">Habits, dailies, and to-dos on one board.</text>
  <rect x="200" y="400" width="120" height="6" rx="3" fill="#3b82f6"/>
</svg>
""".Replace("{MARK}", mark);

foreach (var (OutputPath, Content) in outputs)
{
    Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? ".");
    await File.WriteAllTextAsync(OutputPath, Content);
    await Console.Out.WriteLineAsync($"Wrote {Path.GetRelativePath(repoRoot, OutputPath)}");
}

// Raster exports via BrandExporter (SVG -> PNG).
var pngExports = new (string Svg, string Png, int Width, int Height)[]
{
    (Path.Combine(repoRoot, "src", AppWeb, Wwwroot, "favicon.svg"),
        Path.Combine(repoRoot, "src", AppWeb, Wwwroot, "favicon.png"), 192, 192),
    (Path.Combine(repoRoot, "src", AppWeb, Wwwroot, Brand, "icon-app.svg"),
        Path.Combine(repoRoot, "src", AppWeb, Wwwroot, "apple-touch-icon.png"), 180, 180),
    (Path.Combine(repoRoot, "src", AppWeb, Wwwroot, Brand, "icon-maskable.svg"),
        Path.Combine(repoRoot, "src", AppWeb, Wwwroot, "icons", "icon-maskable-512.png"), 512, 512),
    (Path.Combine(repoRoot, "src", AppWeb, Wwwroot, Brand, "wordmark-og.svg"),
        Path.Combine(repoRoot, "src", AppWeb, Wwwroot, "og-image.png"), 1200, 630),
};

var exporterProject = Path.Combine(repoRoot, "scripts", "BrandExporter", "BrandExporter.csproj");
if (File.Exists(exporterProject))
{
    foreach (var (Svg, Png, Width, Height) in pngExports)
    {
        var result = await RunAsync("dotnet",
            $"run --project \"{exporterProject}\" -- \"{Svg}\" \"{Png}\" {Width} {Height}");
        if (result != 0)
        {
            await Console.Error.WriteLineAsync($"BrandExporter failed for {Svg} (exit {result}).");
            return 1;
        }
    }
}
else
{
    await Console.Error.WriteLineAsync($"BrandExporter project not found at {exporterProject}; skipping raster exports.");
}

await Console.Out.WriteLineAsync("Brand assets regenerated.");
return 0;

static List<Bar> ParseBars(string svg) =>
    Regex.Matches(svg, "<rect x=\"([\\d.]+)\" y=\"([\\d.]+)\" width=\"([\\d.]+)\" height=\"([\\d.]+)\" rx=\"([\\d.]+)\"/>",
            RegexOptions.None, TimeSpan.FromSeconds(1))
        .Select(m => new Bar(
            double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
            double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
            double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
            double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture),
            double.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture)))
        .ToList();

static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

static string? FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            return dir.FullName;
        }
        dir = dir.Parent;
    }
    return null;
}

static async Task<int> RunAsync(string fileName, string arguments)
{
    using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        UseShellExecute = false,
    });
    if (process is null)
    {
        return -1;
    }
    await process.WaitForExitAsync();
    return process.ExitCode;
}

namespace BrandGenerator
{
    internal sealed record Bar(double X, double Y, double W, double H, double Rx);
}
