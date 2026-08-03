using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using FluentAssertions;

namespace App.Shared.Tests;

/// <summary>
/// Guards against brand mark drift. The canonical mark lives in
/// src/App.Shared.RCL/wwwroot/brand/mark.svg and every other mark-bearing SVG
/// is generated from it by scripts/BrandGenerator. If any generated file
/// deviates from the canonical bar geometry,
/// this suite fails so the drift is caught in CI instead of on a device.
/// </summary>
public sealed class BrandMarkConsistencyTests
{
    private static readonly string[] DerivedMarkFiles =
    [
        Path.Combine("src", "App.Shared.RCL", "wwwroot", "brand", "mark-tile.svg"),
        Path.Combine("src", "App.Web", "wwwroot", "favicon.svg"),
        Path.Combine("src", "App.Web", "wwwroot", "brand", "icon-app.svg"),
        Path.Combine("src", "App.Web", "wwwroot", "brand", "icon-mark.svg"),
        Path.Combine("src", "App.Web", "wwwroot", "brand", "icon-maskable.svg"),
        Path.Combine("src", "App.Web", "wwwroot", "brand", "wordmark-og.svg"),
        Path.Combine("src", "App.MAUI", "Resources", "AppIcon", "appiconfg.svg"),
        Path.Combine("src", "App.MAUI", "Resources", "Splash", "splash.svg"),
    ];

    [Fact]
    public void AllDerivedBrandSvgsMatchCanonicalMark()
    {
        var repoRoot = FindRepoRoot();
        var canonical = ParseMarkBars(Path.Combine(repoRoot, "src", "App.Shared.RCL", "wwwroot", "brand", "mark.svg"));

        foreach (var file in DerivedMarkFiles)
        {
            var path = Path.Combine(repoRoot, file);
            var bars = ParseMarkBars(path);
            var scale = bars[0].W / canonical[0].W;

            bars.Should().HaveCount(3, $"{file} must contain exactly the 3 mark bars");
            for (var i = 0; i < canonical.Count; i++)
            {
                var expected = canonical[i];
                var actual = bars[i];
                actual.X.Should().BeApproximately(bars[0].X + (expected.X - canonical[0].X) * scale, 0.1, $"{file} bar {i + 1} x");
                actual.Y.Should().BeApproximately(bars[0].Y + (expected.Y - canonical[0].Y) * scale, 0.1, $"{file} bar {i + 1} y");
                actual.W.Should().BeApproximately(expected.W * scale, 0.1, $"{file} bar {i + 1} width");
                actual.H.Should().BeApproximately(expected.H * scale, 0.1, $"{file} bar {i + 1} height");
                actual.Rx.Should().BeApproximately(expected.Rx * scale, 0.1, $"{file} bar {i + 1} corner radius");
            }

            var markGroup = bars[0].Element.Parent
                ?? throw new InvalidOperationException($"{file}: mark bars have no parent group");
            markGroup.Elements().Count(e => e.Name.LocalName == "circle").Should().Be(0, $"{file} mark must not contain a circle");
            markGroup.Elements().Count(e => e.Name.LocalName == "path").Should().Be(0, $"{file} mark must not contain a path");
        }
    }

    [Fact]
    public void LoadingSplashMarkupsReferenceTheSharedTile()
    {
        var repoRoot = FindRepoRoot();
        var mauiSplash = File.ReadAllText(Path.Combine(repoRoot, "src", "App.MAUI", "wwwroot", "index.html"));
        var wasmSplash = File.ReadAllText(Path.Combine(repoRoot, "src", "App.Web", "Components", "App.razor"));

        mauiSplash.Should().Contain("_content/App.Shared.RCL/brand/mark-tile.svg");
        wasmSplash.Should().Contain("_content/App.Shared.RCL/brand/mark-tile.svg");
        mauiSplash.Should().NotContain("<rect", "the MAUI splash must not inline its own mark");
        wasmSplash.Should().NotContain("<rect", "the WASM splash must not inline its own mark");
    }

    private static List<MarkBar> ParseMarkBars(string path)
    {
        var doc = XDocument.Load(path);
        var bars = new List<MarkBar>();
        foreach (var rect in doc.Descendants().Where(e => e.Name.LocalName == "rect"))
        {
            if (rect.Attribute("x") is null || EffectiveFill(rect) != "#ffffff")
            {
                continue;
            }

            var x = double.Parse(rect.Attribute("x")!.Value, CultureInfo.InvariantCulture);
            var y = double.Parse(rect.Attribute("y")!.Value, CultureInfo.InvariantCulture);
            var w = double.Parse(rect.Attribute("width")!.Value, CultureInfo.InvariantCulture);
            var h = double.Parse(rect.Attribute("height")!.Value, CultureInfo.InvariantCulture);
            var rx = double.Parse(rect.Attribute("rx")!.Value, CultureInfo.InvariantCulture);

            var (tx, ty, s) = EffectiveTransform(rect);
            bars.Add(new MarkBar(rect, tx + s * x, ty + s * y, s * w, s * h, s * rx));
        }

        return bars.OrderBy(b => b.X).ToList();
    }

    private static (double Tx, double Ty, double S) EffectiveTransform(XElement element)
    {
        var transforms = element.Ancestors()
            .Where(a => a.Name.LocalName == "g")
            .Reverse()
            .Select(a => a.Attribute("transform")?.Value ?? "")
            .ToList();

        double tx = 0, ty = 0, s = 1;
        foreach (var transform in transforms)
        {
            var translate = Regex.Match(transform, @"translate\((-?[\d.]+) (-?[\d.]+)\)");
            if (translate.Success)
            {
                tx += s * double.Parse(translate.Groups[1].Value, CultureInfo.InvariantCulture);
                ty += s * double.Parse(translate.Groups[2].Value, CultureInfo.InvariantCulture);
            }

            var scale = Regex.Match(transform, @"scale\((-?[\d.]+)\)");
            if (scale.Success)
            {
                s *= double.Parse(scale.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        return (tx, ty, s);
    }

    private static string? EffectiveFill(XElement element)
    {
        for (var e = element; e is not null; e = e.Parent)
        {
            var fill = e.Attribute("fill")?.Value;
            if (!string.IsNullOrEmpty(fill))
            {
                return fill;
            }
        }

        return null;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo root not found above the test output directory.");
    }

    private sealed record MarkBar(XElement Element, double X, double Y, double W, double H, double Rx);
}
