using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Tokendial.Core.Providers;

namespace Tokendial.App.Panel;

/// <summary>A provider's mark as one-colour geometry, or a raster alpha mask when no vector exists. From docs/design/marks/normalized.</summary>
public sealed class Mark
{
    public Rect ViewBox { get; init; }
    public IReadOnlyList<(Geometry Geometry, double Opacity)> Shapes { get; init; } = [];
    public BitmapSource? Raster { get; init; }
}

public static class Marks
{
    private static readonly Lazy<Dictionary<string, Mark>> Table = new(Load);

    /// <summary>The mark for a provider id; Claude profiles share the Claude mark.</summary>
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

    /// <summary>The brand colour a provider's tile takes once it is connected; a profile shares its tool's.</summary>
    public static Color Tint(string providerId) =>
        Tints.GetValueOrDefault(ProviderFamily.Of(providerId), Color.FromRgb(0xCB, 0xD5, 0xE1));

    public static Mark? For(string providerId)
    {
        return Table.Value.GetValueOrDefault(ProviderFamily.Of(providerId));
    }

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
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = png;
                image.EndInit();
                image.Freeze();
                table[entry.Name] = new Mark { ViewBox = new Rect(0, 0, image.PixelWidth, image.PixelHeight), Raster = image };
                continue;
            }
            var vb = entry.Value.GetProperty("viewBox").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var shapes = new List<(Geometry, double)>();
            foreach (var shape in entry.Value.GetProperty("shapes").EnumerateArray())
            {
                var rule = shape.TryGetProperty("fillRule", out var r) && r.GetString() == "evenodd" ? "F0 " : "F1 ";
                var geometry = Geometry.Parse(rule + shape.GetProperty("d").GetString());
                geometry.Freeze();
                shapes.Add((geometry, shape.TryGetProperty("opacity", out var o) ? o.GetDouble() : 1.0));
            }
            table[entry.Name] = new Mark { ViewBox = new Rect(vb[0], vb[1], vb[2], vb[3]), Shapes = shapes };
        }
        return table;
    }
}

/// <summary>Draws a mark scaled into its bounds in one brush. Square by default; the mark keeps its aspect.</summary>
public sealed class MarkView : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(MarkView),
        new FrameworkPropertyMetadata(Theme.TextSecondary, FrameworkPropertyMetadataOptions.AffectsRender));

    private Mark? mark;

    public MarkView(string providerId, double size)
    {
        Fill = Theme.TextSecondary;
        mark = Marks.For(providerId);
        Width = Height = size;
        FlowDirection = FlowDirection.LeftToRight;
        IsHitTestVisible = false;
        UseLayoutRounding = false;
    }

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public bool HasMark => mark is not null;

    private bool pulsing;

    /// <summary>A heartbeat between two colours while an agent waits on the user; a steady fill otherwise.</summary>
    public void Pulse(bool on, Color bright, Color dim, Brush steady)
    {
        if (on)
        {
            if (pulsing) return;
            pulsing = true;
            var brush = new SolidColorBrush(bright);
            Fill = brush;
            if (Theme.ReduceMotion) { brush.Color = bright; return; }
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new System.Windows.Media.Animation.ColorAnimation(bright, dim, new Duration(TimeSpan.FromSeconds(0.55)))
            {
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            });
            return;
        }
        pulsing = false;
        Fill = steady;
    }

    public void Show(string providerId)
    {
        mark = Marks.For(providerId);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (mark is null) return;
        var box = new Rect(0, 0, ActualWidth, ActualHeight);
        var scale = Math.Min(box.Width / mark.ViewBox.Width, box.Height / mark.ViewBox.Height);
        var drawn = new Size(mark.ViewBox.Width * scale, mark.ViewBox.Height * scale);
        var offset = new Point((box.Width - drawn.Width) / 2, (box.Height - drawn.Height) / 2);
        if (mark.Raster is not null)
        {
            dc.PushOpacityMask(new ImageBrush(mark.Raster) { Stretch = Stretch.Fill });
            dc.DrawRectangle(Fill, null, new Rect(offset, drawn));
            dc.Pop();
            return;
        }
        dc.PushTransform(new TranslateTransform(offset.X, offset.Y));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.PushTransform(new TranslateTransform(-mark.ViewBox.X, -mark.ViewBox.Y));
        foreach (var (geometry, opacity) in mark.Shapes)
        {
            if (opacity < 1) dc.PushOpacity(opacity);
            dc.DrawGeometry(Fill, null, geometry);
            if (opacity < 1) dc.Pop();
        }
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }
}
