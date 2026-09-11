using Avalonia.Media;
using Tokendial.Core.Model;

namespace Tokendial.Linux.Panel;

/// <summary>
/// The same numbers the other two platforms use, taken from docs/design/tokens.md and mirroring
/// windows/Tokendial.App/Panel/Theme.cs value for value. Nothing here is chosen for Avalonia's
/// convenience: the contract is that a dial looks identical on all three systems.
/// </summary>
public static class Theme
{
    private static IBrush Rgb(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));
    private static IBrush Rgba(byte r, byte g, byte b, byte a) => new SolidColorBrush(Color.FromArgb(a, r, g, b));

    public static readonly IBrush Surface = Rgba(16, 17, 20, 128);
    public static readonly IBrush SurfaceEdge = Rgba(255, 255, 255, 20);
    public static readonly IBrush Track = Rgba(255, 255, 255, 41);
    public static readonly IBrush Ample = Rgb(0x34, 0xD3, 0x99);
    public static readonly IBrush Watch = Rgb(0xFB, 0xBF, 0x24);
    public static readonly IBrush Critical = Rgb(0xFB, 0x71, 0x85);
    public static readonly IBrush TextPrimary = Rgb(0xF5, 0xF5, 0xF7);
    public static readonly IBrush TextSecondary = Rgb(0x9A, 0x9D, 0xA6);
    public static readonly IBrush TextDisabled = Rgb(0x5C, 0x5F, 0x68);
    public static readonly IBrush CardSurface = Rgba(16, 17, 20, 235);
    public static readonly IBrush Hairline = Rgba(255, 255, 255, 28);
    public static readonly IBrush Working = Rgb(0xF5, 0xF5, 0xF7);
    public static readonly IBrush Waiting = Watch;

    // Segoe UI Variable and Cascadia do not exist here. Inter is Mint's closest metric match among the
    // fonts it ships; embedding the real ones is on the plan, and until then this is the honest stand-in.
    public static readonly FontFamily Font = new("Inter, Ubuntu, DejaVu Sans, sans-serif");

    public const double CompactHeight = 34;
    public const double CompactPadding = 14;
    public const double CompactSpacing = 10;
    public const double CompactDial = 24;
    public const double CompactStroke = 3;
    public const double CompactRadius = 17;
    public const double HotZone = 24;
    public const double DockSlant = 14;
    public const double CompactMark = 11;
    public const double ExpandedHeight = 132;
    public const double ExpandedPadding = 20;
    public const double ExpandedMark = 13;
    public const double ExpandedDial = 56;
    public const double ExpandedStroke = 6;
    public const double ExpandedRadius = 20;
    public const double CellWidth = 96;
    public const double CellHeight = 96;
    public const double CellGap = 14;
    public const double CardWidth = 280;
    public const double CardRadius = 16;
    public const double CardPadding = 14;

    /// <summary>Ample below the first threshold, watch between, critical above - the colour is the reading.</summary>
    public static IBrush Of(Band? band) => band switch
    {
        Band.Ample => Ample,
        Band.Watch => Watch,
        Band.Critical => Critical,
        _ => TextDisabled
    };
}
