using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Tokendial.Core.Model;
using Tokendial.Core.Sessions;

namespace Tokendial.App.Panel;

/// <summary>
/// Tokendial's face: the dial's own 240° arc around a dark disc, with the face typed in glyphs inside it.
/// It is always drawn in the dark palette, like a device's screen, so it reads the same over any window
/// behind it and in either theme.
/// </summary>
public sealed class Mascot : FrameworkElement
{
    private const double ArcGapRatio = 0.6;
    private const double EyeSizeRatio = 0.25;
    private const double EyeLineRatio = 0.43;
    private const double MouthSizeRatio = 0.22;
    private const double MouthLineRatio = 0.65;
    private const double GazeRatio = 0.05;

    private static readonly Theme.Palette Screen = Theme.DarkPalette;
    private static readonly Typeface Glyphs = new(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(Mascot),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FaceProperty = DependencyProperty.Register(nameof(Face), typeof(Face), typeof(Mascot),
        new FrameworkPropertyMetadata(Face.Empty, FrameworkPropertyMetadataOptions.AffectsRender));
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

    /// <summary>The arc's fill, animated on its own so a new reading sweeps rather than jumps.</summary>
    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public Face Face { get => (Face)GetValue(FaceProperty); set => SetValue(FaceProperty, value); }

    /// <summary>Carries the eyes from side to side while an agent works, -1 to 1.</summary>
    public double Gaze { get => (double)GetValue(GazeProperty); set => SetValue(GazeProperty, value); }

    /// <summary>Above one half the eyes are open. The blink drives it; everything else leaves it at one.</summary>
    public double Lid { get => (double)GetValue(LidProperty); set => SetValue(LidProperty, value); }

    public void Show(Face face, bool animate)
    {
        Face = face;
        var duration = Theme.DurationOf(Theme.Reading);
        if (!animate || duration.TimeSpan == TimeSpan.Zero) { BeginAnimation(FractionProperty, null); Fraction = face.Fraction; }
        else BeginAnimation(FractionProperty, new DoubleAnimation(face.Fraction, duration) { EasingFunction = Theme.Reading });
        Move(face.Spent ? null : face.Mood);
    }

    /// <summary>
    /// Waiting nods, working looks around. Both stop under reduce motion, which leaves the glyphs saying the
    /// same thing without movement.
    /// </summary>
    private void Move(SessionState? state)
    {
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

    /// <summary>A blink every few seconds, in every state but spent: it is what stops a face reading as a diagram.</summary>
    private void StartBlinking()
    {
        if (Theme.ReduceMotion) return;
        var blink = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromSeconds(5.2)), RepeatBehavior = RepeatBehavior.Forever };
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0.96)));
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0.99)));
        BeginAnimation(LidProperty, blink);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var face = Face;
        var centre = new Point(Width / 2, Height / 2);
        dc.DrawEllipse(Theme.FaceGround, new Pen(Screen.SurfaceEdge, 1), centre, Width / 2 - 0.5, Height / 2 - 0.5);

        var radius = Width / 2 - Stroke * (0.5 + ArcGapRatio);
        var cap = new Func<Brush, Pen>(brush => new Pen(brush, Stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        dc.DrawGeometry(null, cap(Screen.Track), Dial.Arc(centre, radius, Dial.StartAngle, Dial.Sweep));
        var sweep = Dial.Sweep * Math.Clamp(Fraction, 0, 1);
        if (sweep >= 0.5) dc.DrawGeometry(null, cap(BandBrush(face.Band)), Dial.Arc(centre, radius, Dial.StartAngle, sweep));

        var ink = face.Spent ? Screen.TextDisabled : Screen.TextPrimary;
        var eyes = Lid < 0.5 && !face.Spent ? Face.Blink : face.Eyes;
        DrawGlyphs(dc, eyes, Width * EyeSizeRatio, ink, new Point(centre.X + Gaze * Width * GazeRatio, Height * EyeLineRatio));
        DrawGlyphs(dc, face.Mouth, Width * MouthSizeRatio, face.Mood == SessionState.Waiting && !face.Spent ? Screen.Watch : ink, new Point(centre.X, Height * MouthLineRatio));
    }

    private void DrawGlyphs(DrawingContext dc, string text, double size, Brush brush, Point centre)
    {
        var glyphs = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Glyphs, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(glyphs, new Point(centre.X - glyphs.WidthIncludingTrailingWhitespace / 2, centre.Y - glyphs.Height / 2));
    }

    private static Brush BandBrush(Band? band) => band switch
    {
        Band.Ample => Screen.Ample,
        Band.Watch => Screen.Watch,
        Band.Critical => Screen.Critical,
        _ => Screen.TextDisabled
    };
}
