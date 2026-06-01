using SkiaSharp;
using Svg.Skia;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: BrandExporter <input.svg> <output> <width> [height]");
    return 1;
}

var inputPath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
if (!int.TryParse(args[2], out var width) || width <= 0)
{
    Console.Error.WriteLine("Invalid width.");
    return 1;
}

var height = args.Length > 3 && int.TryParse(args[3], out var h) ? h : width;

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

using var svg = new SKSvg();
if (svg.Load(inputPath) is null)
{
    Console.Error.WriteLine($"Failed to load SVG: {inputPath}");
    return 1;
}

var picture = svg.Picture ?? throw new InvalidOperationException($"No picture in {inputPath}");
var bounds = picture.CullRect;
var scale = Math.Min(width / bounds.Width, height / bounds.Height);

using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
var canvas = surface.Canvas;
canvas.Clear(SKColors.Transparent);
canvas.Scale(scale);
canvas.Translate(-bounds.Left, -bounds.Top);
canvas.DrawPicture(picture);
canvas.Flush();

using var image = surface.Snapshot();
using var data = image.Encode(SKEncodedImageFormat.Png, 100);
await using var stream = File.Create(outputPath);
data.SaveTo(stream);

Console.WriteLine($"Wrote {outputPath} ({width}x{height})");
return 0;
