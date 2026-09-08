#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property OutputType=Exe
#:property PublishTrimmed=false
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Renders docs/design/marks/normalized into one PNG the way the Windows app draws them:
// Geometry.Parse over the path data, one colour, opacities per face. Doubles as a parse check.
// Run from the repo root: dotnet run tools/marks-preview.cs

using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var root = Path.Combine(Directory.GetCurrentDirectory(), "docs", "design", "marks", "normalized");
using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "marks.json")));
var marks = document.RootElement.EnumerateObject().OrderBy(p => p.Name).ToList();
var colours = new[] { ("#9A9DA6", "secondary"), ("#34D399", "ample"), ("#F5F5F7", "primary") };
double cell = 72, gap = 12, left = 90, top = 16;
var width = (int)(left + marks.Count * (cell + gap) + gap);
var height = (int)(top + colours.Length * (cell + gap) + 60);

var visual = new DrawingVisual();
using (var dc = visual.RenderOpen())
{
    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x10, 0x11, 0x14)), null, new Rect(0, 0, width, height));
    for (var row = 0; row < colours.Length; row++)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(colours[row].Item1)!;
        var y = top + row * (cell + gap);
        dc.DrawText(new FormattedText(colours[row].Item2, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.Gray, 1.0), new Point(12, y + cell / 2 - 8));
        for (var i = 0; i < marks.Count; i++)
        {
            var x = left + i * (cell + gap);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x1C, 0x1E, 0x24)), null, new Rect(x, y, cell, cell), 10, 10);
            Draw(dc, marks[i], brush, new Rect(x + 12, y + 12, cell - 24, cell - 24));
        }
    }
    var small = top + colours.Length * (cell + gap) + 8;
    var grey = (SolidColorBrush)new BrushConverter().ConvertFromString("#9A9DA6")!;
    for (var i = 0; i < marks.Count; i++)
    {
        var x = left + i * (cell + gap);
        Draw(dc, marks[i], grey, new Rect(x + 6, small + 8, 16, 16));
        Draw(dc, marks[i], grey, new Rect(x + 32, small + 4, 24, 24));
    }
}

var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
bitmap.Render(visual);
var encoder = new PngBitmapEncoder();
encoder.Frames.Add(BitmapFrame.Create(bitmap));
var output = Path.Combine(root, "preview.png");
using (var stream = File.Create(output)) encoder.Save(stream);
Console.WriteLine(output);

void Draw(DrawingContext dc, JsonProperty mark, Brush brush, Rect box)
{
    if (mark.Value.TryGetProperty("raster", out var raster))
    {
        var image = new BitmapImage(new Uri(Path.Combine(root, raster.GetString()!)));
        var scale = Math.Min(box.Width / image.PixelWidth, box.Height / image.PixelHeight);
        var size = new Size(image.PixelWidth * scale, image.PixelHeight * scale);
        var rect = new Rect(box.X + (box.Width - size.Width) / 2, box.Y + (box.Height - size.Height) / 2, size.Width, size.Height);
        dc.PushOpacityMask(new ImageBrush(image) { Stretch = Stretch.Uniform, Viewbox = new Rect(0, 0, 1, 1), AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center });
        dc.DrawRectangle(brush, null, rect);
        dc.Pop();
        return;
    }
    var vb = mark.Value.GetProperty("viewBox").EnumerateArray().Select(v => v.GetDouble()).ToArray();
    var group = new GeometryGroup { FillRule = FillRule.Nonzero };
    var shapes = mark.Value.GetProperty("shapes").EnumerateArray().Select(s => (Geometry.Parse(s.GetProperty("d").GetString()!), s.GetProperty("opacity").GetDouble())).ToList();
    var scale = Math.Min(box.Width / vb[2], box.Height / vb[3]);
    var transform = new TransformGroup();
    transform.Children.Add(new TranslateTransform(-vb[0], -vb[1]));
    transform.Children.Add(new ScaleTransform(scale, scale));
    transform.Children.Add(new TranslateTransform(box.X + (box.Width - vb[2] * scale) / 2, box.Y + (box.Height - vb[3] * scale) / 2));
    foreach (var (geometry, opacity) in shapes)
    {
        geometry.Transform = transform;
        var faded = brush.Clone();
        faded.Opacity = opacity;
        dc.DrawGeometry(faded, null, geometry);
    }
}
