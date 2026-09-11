using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Tokendial.Core.Providers;

using Tokens = Tokendial.Linux.Panel.Theme;

namespace Tokendial.Linux.Panel;

/// <summary>A provider's mark as one-colour geometry, or a raster alpha mask when no vector exists. From docs/design/marks/normalized.</summary>
public sealed class Mark
{
    public Rect ViewBox { get; init; }
    public IReadOnlyList<(Geometry Geometry, double Opacity)> Shapes { get; init; } = [];
    public Bitmap? Raster { get; init; }
}

public static class Marks
{
    private static readonly Lazy<Dictionary<string, Mark>> Table = new(Load);

    /// <summary>The brand colour a provider's tile takes once it is connected; a profile shares its tool's.</summary>
    private static readonly Dictionary<string, Color> Tints = new()
    {
        ["claude"] = Color.FromRgb(0xD9, 0x77, 0x57),
        ["codex"] = Color.FromRgb(0x38, 0xBD, 0xF8),
        ["copilot"] = Color.FromRgb(0xA7, 0x8B, 0xFA),
        ["cursor"] = Color.FromRgb(0xE2, 0xE8, 0xF0),
        ["antigravity"] = Color.FromRgb(0x60, 0xA5, 0xFA),
        ["gemini"] = Color.FromRgb(0x4E, 0x8D, 0xF5),
        ["glm"] = Color.FromRgb(0x3B, 0x82, 0xF6),
        ["grok"] = Color.FromRgb(0xF1, 0xF5, 0xF9),
        ["opencode"] = Color.FromRgb(0xFB, 0xBF, 0x24)
    };

    public static Color Tint(string providerId) =>
        Tints.GetValueOrDefault(ProviderFamily.Of(providerId), Color.FromRgb(0xCB, 0xD5, 0xE1));

    public static Mark? For(string providerId) => Table.Value.GetValueOrDefault(ProviderFamily.Of(providerId));

    private static Dictionary<string, Mark> Load()
    {
        var table = new Dictionary<string, Mark>(StringComparer.Ordinal);
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("marks.json");
        if (stream is null) return table;
        using var document = JsonDocument.Parse(stream);
        foreach (var entry in document.RootElement.EnumerateObject())
        {
            if (entry.Value.TryGetProperty("raster", out var raster))
            {
                using var png = assembly.GetManifestResourceStream(raster.GetString()!);
                if (png is null) continue;
                var image = new Bitmap(png);
                table[entry.Name] = new Mark { ViewBox = new Rect(0, 0, image.PixelSize.Width, image.PixelSize.Height), Raster = image };
                continue;
            }
            var vb = entry.Value.GetProperty("viewBox").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var shapes = new List<(Geometry, double)>();
            foreach (var shape in entry.Value.GetProperty("shapes").EnumerateArray())
            {
                // Avalonia parses the same path mini-language WPF does, fill-rule prefix included.
                var rule = shape.TryGetProperty("fillRule", out var r) && r.GetString() == "evenodd" ? "F0 " : "F1 ";
                var geometry = Geometry.Parse(rule + shape.GetProperty("d").GetString());
                shapes.Add((geometry, shape.TryGetProperty("opacity", out var o) ? o.GetDouble() : 1.0));
            }
            table[entry.Name] = new Mark { ViewBox = new Rect(vb[0], vb[1], vb[2], vb[3]), Shapes = shapes };
        }
        return table;
    }
}

/// <summary>Draws a mark scaled into its bounds in one brush. Square by default; the mark keeps its aspect.</summary>
public sealed class MarkView : Control
{
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<MarkView, IBrush?>(nameof(Fill), Tokens.TextSecondary);

    static MarkView() => AffectsRender<MarkView>(FillProperty);

    private Mark? mark;

    public MarkView(string providerId, double size)
    {
        mark = Marks.For(providerId);
        Width = Height = size;
        IsHitTestVisible = false;
    }

    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public bool HasMark => mark is not null;

    public void Show(string providerId)
    {
        mark = Marks.For(providerId);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (mark is null) return;
        var box = new Rect(0, 0, Width, Height);
        var scale = Math.Min(box.Width / mark.ViewBox.Width, box.Height / mark.ViewBox.Height);
        var drawn = new Size(mark.ViewBox.Width * scale, mark.ViewBox.Height * scale);
        var offset = new Point((box.Width - drawn.Width) / 2, (box.Height - drawn.Height) / 2);

        if (mark.Raster is not null)
        {
            // One provider ships a raster only; it is used as an alpha mask so it takes the same single
            // colour the vectors do rather than arriving with its own.
            using (context.PushOpacityMask(new ImageBrush(mark.Raster) { Stretch = Stretch.Fill }, new Rect(offset, drawn)))
                context.FillRectangle(Fill ?? Tokens.TextSecondary, new Rect(offset, drawn));
            return;
        }

        using (context.PushTransform(Matrix.CreateTranslation(-mark.ViewBox.X, -mark.ViewBox.Y)
                                     * Matrix.CreateScale(scale, scale)
                                     * Matrix.CreateTranslation(offset.X, offset.Y)))
        {
            foreach (var (geometry, opacity) in mark.Shapes)
            {
                if (opacity < 1)
                    using (context.PushOpacity(opacity)) context.DrawGeometry(Fill, null, geometry);
                else context.DrawGeometry(Fill, null, geometry);
            }
        }
    }
}
