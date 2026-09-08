using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Tokendial.Core.Sessions;

namespace Tokendial.App.Panel;

/// <summary>A 240° arc opening downward, 150° to 30° clockwise. The arc's end is the reading; there is no needle.</summary>
public sealed class Dial : FrameworkElement
{
    public const double StartAngle = 150;
    public const double Sweep = 240;

    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(Dial),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(Dial),
        new FrameworkPropertyMetadata(Theme.TextDisabled, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Brush), typeof(Dial),
        new FrameworkPropertyMetadata(Theme.Track, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty HollowProperty = DependencyProperty.Register(nameof(Hollow), typeof(bool), typeof(Dial),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public Dial(double diameter, double stroke)
    {
        Width = Height = diameter;
        Stroke = stroke;
        UseLayoutRounding = false;
        SnapsToDevicePixels = false;
    }

    public double Stroke { get; }
    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    /// <summary>No reading: the track alone, dimmed.</summary>
    public bool Hollow { get => (bool)GetValue(HollowProperty); set => SetValue(HollowProperty, value); }

    public void AnimateTo(double fraction)
    {
        var duration = Theme.DurationOf(Theme.Reading);
        if (duration.TimeSpan == TimeSpan.Zero) { BeginAnimation(FractionProperty, null); Fraction = fraction; return; }
        BeginAnimation(FractionProperty, new DoubleAnimation(Math.Clamp(fraction, 0, 1), duration) { EasingFunction = Theme.Reading });
    }

    protected override void OnRender(DrawingContext dc)
    {
        var radius = (Width - Stroke) / 2;
        var centre = new Point(Width / 2, Height / 2);
        var trackPen = new Pen(Hollow ? Theme.TextDisabled : Track, Stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, trackPen, Arc(centre, radius, StartAngle, Sweep));
        if (Hollow) return;
        var sweep = Sweep * Math.Clamp(Fraction, 0, 1);
        if (sweep < 0.5) return;
        var pen = new Pen(Fill, Stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, pen, Arc(centre, radius, StartAngle, sweep));
    }

    /// <summary>Angles clockwise from 3 o'clock, as on screen.</summary>
    public static Geometry Arc(Point centre, double radius, double startDegrees, double sweepDegrees)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(At(centre, radius, startDegrees), false, false);
            ctx.ArcTo(At(centre, radius, startDegrees + sweepDegrees), new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Point At(Point centre, double radius, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(centre.X + radius * Math.Cos(rad), centre.Y + radius * Math.Sin(rad));
    }
}

/// <summary>The activity indicator inside an expanded dial: a short arc that spins while an agent works and holds still while it waits on you.</summary>
public sealed class ActivityArc : FrameworkElement
{
    private readonly RotateTransform spin = new();
    private SessionState? state;

    public ActivityArc()
    {
        Width = Height = Theme.ActivityDial;
        RenderTransform = spin;
        RenderTransformOrigin = new Point(0.5, 0.5);
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
    }

    public void Show(SessionState? next)
    {
        if (next == state) return;
        state = next;
        spin.BeginAnimation(RotateTransform.AngleProperty, null);
        if (next is null || next == SessionState.Idle) { Visibility = Visibility.Collapsed; return; }
        Visibility = Visibility.Visible;
        if (next == SessionState.Working && !Theme.ReduceMotion)
        {
            spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1.4))) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else spin.Angle = 0;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (state is null || state == SessionState.Idle) return;
        var brush = state == SessionState.Waiting ? Theme.Waiting : Theme.Working;
        var pen = new Pen(brush, Theme.ActivityStroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var radius = (Width - Theme.ActivityStroke) / 2;
        var centre = new Point(Width / 2, Height / 2);
        if (state == SessionState.Waiting)
        {
            dc.DrawGeometry(null, pen, Dial.Arc(centre, radius, 150, 240));
            return;
        }
        dc.DrawGeometry(null, pen, Dial.Arc(centre, radius, -90, 100));
    }
}
