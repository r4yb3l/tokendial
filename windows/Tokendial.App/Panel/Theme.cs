using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Tokendial.Core.Model;

namespace Tokendial.App.Panel;

/// <summary>docs/design/tokens.md, as WPF resources. Frozen brushes; every number in device-independent points.</summary>
public static class Theme
{
    public static readonly Brush Surface = Freeze(new SolidColorBrush(Color.FromArgb(128, 16, 17, 20)));
    public static readonly Brush SurfaceEdge = Freeze(new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)));
    public static readonly Brush CardSurface = Freeze(new SolidColorBrush(Color.FromArgb(235, 16, 17, 20)));
    public static readonly Brush Track = Freeze(new SolidColorBrush(Color.FromArgb(41, 255, 255, 255)));
    public static readonly Brush Ample = Freeze(new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)));
    public static readonly Brush Watch = Freeze(new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)));
    public static readonly Brush Critical = Freeze(new SolidColorBrush(Color.FromRgb(0xFB, 0x71, 0x85)));
    public static readonly Brush Working = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF7)));
    public static readonly Brush Waiting = Watch;
    public static readonly Brush TextPrimary = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF7)));
    public static readonly Brush TextSecondary = Freeze(new SolidColorBrush(Color.FromRgb(0x9A, 0x9D, 0xA6)));
    public static readonly Brush TextDisabled = Freeze(new SolidColorBrush(Color.FromRgb(0x5C, 0x5F, 0x68)));
    public static readonly Brush Hairline = Freeze(new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)));
    public static readonly Brush Transparent = Brushes.Transparent;
    public static readonly Color AmpleColor = Color.FromRgb(0x34, 0xD3, 0x99);
    public static readonly Color TextDisabledColor = Color.FromRgb(0x5C, 0x5F, 0x68);

    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI Variable, Segoe UI");
    public static readonly FontFamily DisplayFont = new("Segoe UI Variable Display, Segoe UI Variable, Segoe UI");

    public const double CompactHeight = 34;
    public const double CompactPadding = 14;
    public const double CompactSpacing = 10;
    public const double CompactDial = 24;
    public const double CompactMark = 11;
    public const double ExpandedMark = 13;
    public const double CompactStroke = 3;
    public const double ExpandedHeight = 132;
    public const double ExpandedPadding = 20;
    public const double CellWidth = 96;
    public const double ExpandedDial = 56;
    public const double ExpandedStroke = 6;
    public const double ActivityDial = 40;
    public const double ActivityStroke = 2;
    public const double CompactRadius = 17;
    public const double ExpandedRadius = 20;
    public const double HotZone = 24;
    public const double DockSlant = 14;
    public const double CardWidth = 280;
    public const double CardRadius = 16;
    public const double CardPadding = 14;

    public static Brush Of(Band? band) => band switch
    {
        Band.Ample => Ample,
        Band.Watch => Watch,
        Band.Critical => Critical,
        _ => TextDisabled
    };

    public static Brush Of(double? fraction) => fraction is double f ? Of(Bands.Of(f)) : TextDisabled;

    public static readonly SpringCurve Expand = new(0.38, 0.80);
    public static readonly SpringCurve Contents = new(0.32, 0.84);
    public static readonly SpringCurve Reading = new(0.80, 0.90);
    public static readonly SpringCurve Glide = new(0.45, 0.86);
    public static readonly Duration Crossfade = new(TimeSpan.FromSeconds(0.15));

    public static bool ReduceMotion => !SystemParameters.ClientAreaAnimation;

    public static Duration DurationOf(SpringCurve curve) => ReduceMotion ? new Duration(TimeSpan.Zero) : new Duration(curve.SettleTime);

    public static double Stagger(int index) => ReduceMotion ? 0 : Math.Min(index * 0.04, 0.16);

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}

/// <summary>A critically- or under-damped spring sampled over the time it takes to settle within a thousandth.</summary>
public sealed class SpringCurve : EasingFunctionBase
{
    private readonly double omega;
    private readonly double zeta;

    public SpringCurve(double response, double damping)
    {
        Response = response;
        Damping = damping;
        omega = 2 * Math.PI / response;
        zeta = damping;
        SettleTime = TimeSpan.FromSeconds(Math.Log(1000) / (zeta * omega));
        EasingMode = EasingMode.EaseIn;
    }

    public double Response { get; }
    public double Damping { get; }
    public TimeSpan SettleTime { get; }

    protected override double EaseInCore(double normalizedTime)
    {
        var t = normalizedTime * SettleTime.TotalSeconds;
        var decay = Math.Exp(-zeta * omega * t);
        if (zeta >= 1) return 1 - decay * (1 + omega * t);
        var damped = omega * Math.Sqrt(1 - zeta * zeta);
        return 1 - decay * (Math.Cos(damped * t) + zeta * omega / damped * Math.Sin(damped * t));
    }

    protected override Freezable CreateInstanceCore() => new SpringCurve(Response, Damping);
}
