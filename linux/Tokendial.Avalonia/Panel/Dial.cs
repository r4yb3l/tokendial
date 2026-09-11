using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

// Avalonia's StyledElement already has a Theme property, which shadows the token class inside any
// control. The class keeps the name it has on Windows and macOS - the three are meant to read alike -
// and the alias is what lets it be reached from inside a control.
using Tokens = Tokendial.Linux.Panel.Theme;

namespace Tokendial.Linux.Panel;

/// <summary>
/// A 240° arc opening downward, 150° to 30° clockwise. The arc's end is the reading; there is no needle.
/// The Avalonia twin of windows/Tokendial.App/Panel/Dial.cs, angle for angle - the maths is the product's
/// identity and is not allowed to drift between platforms.
/// </summary>
public sealed class Dial : Control
{
    public const double StartAngle = 150;
    public const double Sweep = 240;

    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<Dial, double>(nameof(Fraction));
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<Dial, IBrush?>(nameof(Fill), Tokens.TextDisabled);
    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<Dial, IBrush?>(nameof(Track), Tokens.Track);
    /// <summary>No reading: the track alone, dimmed.</summary>
    public static readonly StyledProperty<bool> HollowProperty =
        AvaloniaProperty.Register<Dial, bool>(nameof(Hollow));

    static Dial() => AffectsRender<Dial>(FractionProperty, FillProperty, TrackProperty, HollowProperty);

    public Dial(double diameter, double stroke)
    {
        Width = Height = diameter;
        Stroke = stroke;
    }

    public double Stroke { get; }
    public double Fraction { get => GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public bool Hollow { get => GetValue(HollowProperty); set => SetValue(HollowProperty, value); }

    public override void Render(DrawingContext context)
    {
        var radius = (Width - Stroke) / 2;
        var centre = new Point(Width / 2, Height / 2);

        var trackPen = new Pen(Hollow ? Tokens.TextDisabled : Track, Stroke, lineCap: PenLineCap.Round);
        context.DrawGeometry(null, trackPen, Arc(centre, radius, StartAngle, Sweep));
        if (Hollow) return;

        var sweep = Sweep * Math.Clamp(Fraction, 0, 1);
        // Below half a degree the round caps would draw a dot where there is nothing to report.
        if (sweep < 0.5) return;
        var pen = new Pen(Fill, Stroke, lineCap: PenLineCap.Round);
        context.DrawGeometry(null, pen, Arc(centre, radius, StartAngle, sweep));
    }

    /// <summary>Angles clockwise from 3 o'clock, as on screen.</summary>
    public static Geometry Arc(Point centre, double radius, double startDegrees, double sweepDegrees)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(At(centre, radius, startDegrees), false);
            ctx.ArcTo(At(centre, radius, startDegrees + sweepDegrees), new Size(radius, radius), 0,
                sweepDegrees > 180, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        return geometry;
    }

    private static Point At(Point centre, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(centre.X + radius * Math.Cos(radians), centre.Y + radius * Math.Sin(radians));
    }
}
