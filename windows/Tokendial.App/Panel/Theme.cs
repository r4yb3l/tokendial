using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Tokendial.Core.Model;

namespace Tokendial.App.Panel;

/// <summary>docs/design/tokens.md, as WPF resources. Frozen brushes; every number in device-independent points.</summary>
public static class Theme
{
    /// <summary>One look for the dock and the card: surfaces, text inks and the band colours.</summary>
    public sealed record Palette(Brush Surface, Brush SurfaceEdge, Brush CardSurface, Brush Track, Brush Ample, Brush Watch, Brush Critical, Brush Working,
        Brush TextPrimary, Brush TextSecondary, Brush TextDisabled, Brush Hairline, Color AmpleColor, Color TextDisabledColor);

    public static readonly Palette DarkPalette = new(
        Surface: Rgba(16, 17, 20, 128), SurfaceEdge: Rgba(255, 255, 255, 20), CardSurface: Rgba(16, 17, 20, 235), Track: Rgba(255, 255, 255, 41),
        Ample: Rgb(0x34, 0xD3, 0x99), Watch: Rgb(0xFB, 0xBF, 0x24), Critical: Rgb(0xFB, 0x71, 0x85), Working: Rgb(0xF5, 0xF5, 0xF7),
        TextPrimary: Rgb(0xF5, 0xF5, 0xF7), TextSecondary: Rgb(0x9A, 0x9D, 0xA6), TextDisabled: Rgb(0x5C, 0x5F, 0x68), Hairline: Rgba(255, 255, 255, 28),
        AmpleColor: Color.FromRgb(0x34, 0xD3, 0x99), TextDisabledColor: Color.FromRgb(0x5C, 0x5F, 0x68));

    public static readonly Palette LightPalette = new(
        Surface: Rgba(248, 249, 251, 204), SurfaceEdge: Rgba(15, 23, 42, 26), CardSurface: Rgba(252, 252, 253, 245), Track: Rgba(15, 23, 42, 31),
        Ample: Rgb(0x10, 0xB9, 0x81), Watch: Rgb(0xF5, 0x9E, 0x0B), Critical: Rgb(0xF4, 0x3F, 0x5E), Working: Rgb(0x1B, 0x1F, 0x27),
        TextPrimary: Rgb(0x1B, 0x1F, 0x27), TextSecondary: Rgb(0x5B, 0x62, 0x70), TextDisabled: Rgb(0xA3, 0xA9, 0xB4), Hairline: Rgba(15, 23, 42, 26),
        AmpleColor: Color.FromRgb(0x10, 0xB9, 0x81), TextDisabledColor: Color.FromRgb(0xA3, 0xA9, 0xB4));

    private static Palette current = DarkPalette;

    public static bool Dark { get; private set; } = true;

    /// <summary>Switch the look. Elements built afterwards pick it up; live ones are rebuilt by their windows.</summary>
    public static void Use(bool dark)
    {
        Dark = dark;
        current = dark ? DarkPalette : LightPalette;
    }

    public static Brush Surface => current.Surface;
    public static Brush SurfaceEdge => current.SurfaceEdge;
    public static Brush CardSurface => current.CardSurface;
    public static Brush Track => current.Track;
    public static Brush Ample => current.Ample;
    public static Brush Watch => current.Watch;
    public static Brush Critical => current.Critical;
    public static Brush Working => current.Working;
    public static Brush Waiting => current.Watch;
    public static Brush TextPrimary => current.TextPrimary;
    public static Brush TextSecondary => current.TextSecondary;
    public static Brush TextDisabled => current.TextDisabled;
    public static Brush Hairline => current.Hairline;
    public static Brush Transparent => Brushes.Transparent;
    public static Color AmpleColor => current.AmpleColor;
    public static Color TextDisabledColor => current.TextDisabledColor;

    private static Brush Rgb(byte r, byte g, byte b) => Freeze(new SolidColorBrush(Color.FromRgb(r, g, b)));
    private static Brush Rgba(byte r, byte g, byte b, byte a) => Freeze(new SolidColorBrush(Color.FromArgb(a, r, g, b)));

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
    public const double CellHeight = 96;
    public const double CellGap = 14;
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
