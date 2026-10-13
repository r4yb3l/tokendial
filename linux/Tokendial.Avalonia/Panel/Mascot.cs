using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Tokendial.Core.Model;
using Tokendial.Core.Sessions;

using Tokens = Tokendial.Linux.Panel.Theme;

namespace Tokendial.Linux.Panel;

/// <summary>
/// Tokendial's face: the dial's own 240° arc around a dark disc, with the face typed in glyphs inside it.
/// The Avalonia twin of windows/Tokendial.App/Panel/Mascot.cs, ratio for ratio; docs/design/tokens.md
/// ("Floating face") is the contract. Always the dark colours, like a device's screen.
/// </summary>
/// <remarks>
/// Motion is computed from one clock on a timer rather than through Avalonia's animation system: blink, gaze
/// and bob are three cycles of fixed length, and redrawing only when one of them visibly changes keeps an idle
/// face from repainting at frame rate. The Linux app has no reduce-motion setting to honour yet.
/// </remarks>
public sealed class Mascot : Control
{
    private const double ArcGapRatio = 0.6;
    private const double EyeSizeRatio = 0.25;
    private const double EyeLineRatio = 0.43;
    private const double MouthSizeRatio = 0.22;
    private const double MouthLineRatio = 0.65;
    private const double GazeRatio = 0.05;
    private const double BobRatio = 0.04;
    private const double BlinkCycle = 5.2;
    private const double BlinkClosesAt = 0.96;
    private const double BlinkOpensAt = 0.99;
    private const double GazeCycle = 2.6;
    private const double BobCycle = 0.78;
    private const double SweepSeconds = 0.6;

    private static readonly Typeface Glyphs = new(Tokens.FaceFont, FontStyle.Normal, FontWeight.Bold);
    private static readonly (double At, double Value)[] GazeKeys = [(0, -1), (0.22, -1), (0.44, 1), (0.70, 1), (1, -1)];

    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private Face face = Face.Empty;
    private double shownFraction;
    private double fromFraction;
    private double sweepStarted = double.NegativeInfinity;
    private (bool Shut, double Gaze, double Bob, double Fill) drawn;

    public Mascot(double diameter, double stroke)
    {
        Width = Height = diameter;
        Stroke = stroke;
        IsHitTestVisible = false;
        ticker.Tick += (_, _) => Tick();
        AttachedToVisualTree += (_, _) => ticker.Start();
        DetachedFromVisualTree += (_, _) => ticker.Stop();
    }

    public double Stroke { get; }

    public void Show(Face next)
    {
        if (next.Fraction != face.Fraction)
        {
            fromFraction = shownFraction;
            sweepStarted = clock.Elapsed.TotalSeconds;
        }
        face = next;
        InvalidateVisual();
    }

    private void Tick()
    {
        var now = Pose();
        if (now == drawn) return;
        drawn = now;
        InvalidateVisual();
    }

    /// <summary>Where each cycle is now, rounded to what a 72-point face can show.</summary>
    private (bool Shut, double Gaze, double Bob, double Fill) Pose()
    {
        var t = clock.Elapsed.TotalSeconds;
        var phase = t % BlinkCycle / BlinkCycle;
        var shut = !face.Spent && phase >= BlinkClosesAt && phase < BlinkOpensAt;
        var mood = face.Spent ? null : face.Mood;
        var gaze = mood == SessionState.Working ? Math.Round(Gaze(t % GazeCycle / GazeCycle), 2) : 0;
        var bob = mood == SessionState.Waiting ? Math.Round(-BobRatio * Height * (1 - Math.Cos(2 * Math.PI * t / (2 * BobCycle))) / 2, 1) : 0;
        var progress = Math.Clamp((t - sweepStarted) / SweepSeconds, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        var fill = Math.Round(fromFraction + (face.Fraction - fromFraction) * eased, 3);
        return (shut, gaze, bob, fill);
    }

    private static double Gaze(double phase)
    {
        for (var i = 1; i < GazeKeys.Length; i++)
        {
            if (phase > GazeKeys[i].At) continue;
            var (fromAt, from) = GazeKeys[i - 1];
            var (toAt, to) = GazeKeys[i];
            var local = (phase - fromAt) / (toAt - fromAt);
            return from + (to - from) * (1 - Math.Cos(Math.PI * local)) / 2;
        }
        return GazeKeys[^1].Value;
    }

    public override void Render(DrawingContext context)
    {
        var (shut, gaze, bob, fill) = drawn = Pose();
        shownFraction = fill;
        using var lift = context.PushTransform(Matrix.CreateTranslation(0, bob));
        var centre = new Point(Width / 2, Height / 2);
        context.DrawEllipse(Tokens.FaceGround, new Pen(Tokens.SurfaceEdge, 1), centre, Width / 2 - 0.5, Height / 2 - 0.5);

        var radius = Width / 2 - Stroke * (0.5 + ArcGapRatio);
        context.DrawGeometry(null, new Pen(Tokens.Track, Stroke, lineCap: PenLineCap.Round), Dial.Arc(centre, radius, Dial.StartAngle, Dial.Sweep));
        var sweep = Dial.Sweep * Math.Clamp(fill, 0, 1);
        if (sweep >= 0.5) context.DrawGeometry(null, new Pen(Tokens.Of(face.Band), Stroke, lineCap: PenLineCap.Round), Dial.Arc(centre, radius, Dial.StartAngle, sweep));

        var ink = face.Spent ? Tokens.TextDisabled : Tokens.TextPrimary;
        DrawGlyphs(context, shut ? Face.Blink : face.Eyes, Width * EyeSizeRatio, ink, new Point(centre.X + gaze * Width * GazeRatio, Height * EyeLineRatio));
        DrawGlyphs(context, face.Mouth, Width * MouthSizeRatio, face.Mood == SessionState.Waiting && !face.Spent ? Tokens.Waiting : ink, new Point(centre.X, Height * MouthLineRatio));
    }

    private static void DrawGlyphs(DrawingContext context, string text, double size, IBrush brush, Point centre)
    {
        var glyphs = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Glyphs, size, brush);
        context.DrawText(glyphs, new Point(centre.X - glyphs.WidthIncludingTrailingWhitespace / 2, centre.Y - glyphs.Height / 2));
    }
}
