using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Tokendial.Core;
using Tokendial.Core.Model;
using Tokendial.Core.Sessions;

namespace Tokendial.App.Panel;

/// <summary>
/// Tokendial's face: the dial's own 240° arc with a pair of eyes and a mouth in its opening.
/// The arc carries the worst band across every provider; the face carries the strongest state across every session.
/// </summary>
public sealed class Mascot : FrameworkElement
{
    private const double EyeOffsetRatio = 0.109;
    private const double EyeLineRatio = 0.449;
    private const double EyeRadiusRatio = 0.050;
    private const double MouthLineRatio = 0.576;
    private const double MouthHalfRatio = 0.066;
    private const double MinMouthDiameter = 32;

    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(Mascot),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BandProperty = DependencyProperty.Register(nameof(Band), typeof(Band?), typeof(Mascot),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(SessionState?), typeof(Mascot),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GazeProperty = DependencyProperty.Register(nameof(Gaze), typeof(double), typeof(Mascot),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LidProperty = DependencyProperty.Register(nameof(Lid), typeof(double), typeof(Mascot),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly TranslateTransform bob = new();
    private SessionState? animating;

    public Mascot(double diameter, double stroke)
    {
        Width = Height = diameter;
        Stroke = stroke;
        FlowDirection = FlowDirection.LeftToRight;
        IsHitTestVisible = false;
        UseLayoutRounding = false;
        SnapsToDevicePixels = false;
        RenderTransform = bob;
        StartBlinking();
    }

    public double Stroke { get; }
    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public Band? Band { get => (Band?)GetValue(BandProperty); set => SetValue(BandProperty, value); }
    public SessionState? State { get => (SessionState?)GetValue(StateProperty); set => SetValue(StateProperty, value); }

    /// <summary>Carries the pupils from side to side while an agent works, -1 to 1.</summary>
    public double Gaze { get => (double)GetValue(GazeProperty); set => SetValue(GazeProperty, value); }

    /// <summary>How open the eyes are, 0 to 1. The blink drives it; everything else leaves it at one.</summary>
    public double Lid { get => (double)GetValue(LidProperty); set => SetValue(LidProperty, value); }

    public void AnimateTo(double fraction)
    {
        var duration = Theme.DurationOf(Theme.Reading);
        if (duration.TimeSpan == TimeSpan.Zero) { BeginAnimation(FractionProperty, null); Fraction = fraction; return; }
        BeginAnimation(FractionProperty, new DoubleAnimation(Math.Clamp(fraction, 0, 1), duration) { EasingFunction = Theme.Reading });
    }

    /// <summary>
    /// Waiting nods, working looks around. Both stop under reduce motion, which leaves the eye and mouth
    /// shapes saying the same thing without movement.
    /// </summary>
    public void Show(SessionState? state)
    {
        State = state;
        if (animating == state) return;
        animating = state;
        bob.BeginAnimation(TranslateTransform.YProperty, null);
        BeginAnimation(GazeProperty, null);
        bob.Y = 0;
        Gaze = 0;
        if (Theme.ReduceMotion) return;
        if (state == SessionState.Waiting)
        {
            bob.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -Height * 0.04, new Duration(TimeSpan.FromSeconds(0.78)))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }
        else if (state == SessionState.Working) StartGazing();
    }

    /// <summary>Glances left and right with a pause at each end, which reads as thinking rather than as a metronome.</summary>
    private void StartGazing()
    {
        var gaze = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromSeconds(2.6)), RepeatBehavior = RepeatBehavior.Forever };
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        gaze.KeyFrames.Add(new EasingDoubleKeyFrame(-1, KeyTime.FromPercent(0), ease));
        gaze.KeyFrames.Add(new EasingDoubleKeyFrame(-1, KeyTime.FromPercent(0.22), ease));
        gaze.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.44), ease));
        gaze.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.70), ease));
        gaze.KeyFrames.Add(new EasingDoubleKeyFrame(-1, KeyTime.FromPercent(1), ease));
        BeginAnimation(GazeProperty, gaze);
    }

    /// <summary>A blink every few seconds, in every state: it is what stops a face reading as a diagram.</summary>
    private void StartBlinking()
    {
        if (Theme.ReduceMotion) return;
        var blink = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromSeconds(5.2)), RepeatBehavior = RepeatBehavior.Forever };
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.94)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(0.07, KeyTime.FromPercent(0.963)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.99)));
        BeginAnimation(LidProperty, blink);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var radius = (Width - Stroke) / 2;
        var centre = new Point(Width / 2, Height / 2);
        var trackPen = new Pen(Theme.Track, Stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, trackPen, Dial.Arc(centre, radius, Dial.StartAngle, Dial.Sweep));

        var sweep = Dial.Sweep * Math.Clamp(Fraction, 0, 1);
        if (sweep >= 0.5)
        {
            dc.DrawGeometry(null, new Pen(Body(), Stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                Dial.Arc(centre, radius, Dial.StartAngle, sweep));
            DrawSheen(dc, centre, radius, sweep);
        }

        DrawEyes(dc);
        if (Width >= MinMouthDiameter) DrawMouth(dc);
    }

    /// <summary>A vertical ramp along the arc: lighter where a light above would catch it, darker underneath.</summary>
    private Brush Body()
    {
        var colour = (Theme.Of(Band) as SolidColorBrush)?.Color ?? Theme.TextDisabledColor;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0.25, 1),
            GradientStops =
            {
                new GradientStop(Scale(colour, 1.26), 0),
                new GradientStop(colour, 0.52),
                new GradientStop(Scale(colour, 0.74), 1)
            }
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>A short bright run near the arc's start, which is what makes a flat stroke read as a tube.</summary>
    private void DrawSheen(DrawingContext dc, Point centre, double radius, double sweep)
    {
        var start = Dial.Sweep * 0.16;
        if (sweep <= start) return;
        var pen = new Pen(Theme.Sheen, Stroke * 0.3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, pen, Dial.Arc(centre, radius, Dial.StartAngle + start, Math.Min(Dial.Sweep * 0.1, sweep - start)));
    }

    /// <summary>
    /// Round eyes throughout: widened while an agent waits on you, drifting side to side while one works,
    /// narrowed to a tired lid once critical, and closed to a line when a window is spent. The blink squashes
    /// them rather than hiding them, so they are never two flat dashes.
    /// </summary>
    private void DrawEyes(DrawingContext dc)
    {
        var spent = Fraction >= 1;
        var brush = spent ? Theme.TextDisabled : Theme.Eyes;
        var radius = Width * EyeRadiusRatio;
        var wide = State == SessionState.Waiting;
        var tired = Band == Core.Model.Band.Critical && !spent;
        var rx = spent ? radius * 1.1 : wide ? radius * 1.14 : radius;
        var ry = (spent ? radius * 0.26 : wide ? radius * 1.2 : tired ? radius * 0.7 : radius) * Math.Clamp(Lid, 0.05, 1);
        var line = Height * EyeLineRatio;
        var drift = State == SessionState.Working ? Width * 0.026 * Gaze : 0;
        foreach (var side in new[] { -1, 1 })
        {
            var centre = new Point(Width / 2 + side * Width * EyeOffsetRatio + drift, line);
            dc.DrawEllipse(brush, null, centre, rx, ry);
        }
    }

    /// <summary>
    /// The mouth carries the mood plainly: a smile with room to spare, a flat line as the window tightens,
    /// a worried curve once critical, a small open circle while an agent waits on you, and a dash when nothing is left.
    /// </summary>
    private void DrawMouth(DrawingContext dc)
    {
        var spent = Fraction >= 1;
        var brush = spent ? Theme.TextDisabled : State == SessionState.Waiting ? Theme.Waiting : Theme.Eyes;
        var pen = new Pen(brush, Math.Max(1.1, Width * 0.030)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var line = Height * MouthLineRatio;
        var half = Width * MouthHalfRatio;
        var centre = new Point(Width / 2, line);

        if (!spent && State == SessionState.Waiting)
        {
            dc.DrawEllipse(null, pen, centre, half * 0.5, half * 0.5);
            return;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            if (spent || Band == Core.Model.Band.Watch)
            {
                ctx.BeginFigure(new Point(centre.X - half, line), false, false);
                ctx.LineTo(new Point(centre.X + half, line), true, false);
            }
            else
            {
                var curve = Width * 0.044 * (Band == Core.Model.Band.Critical ? -1 : 1);
                ctx.BeginFigure(new Point(centre.X - half, line - curve * 0.45), false, false);
                ctx.QuadraticBezierTo(new Point(centre.X, line + curve), new Point(centre.X + half, line - curve * 0.45), true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static Color Scale(Color colour, double factor) => Color.FromRgb(
        (byte)Math.Clamp(colour.R * factor, 0, 255),
        (byte)Math.Clamp(colour.G * factor, 0, 255),
        (byte)Math.Clamp(colour.B * factor, 0, 255));
}
