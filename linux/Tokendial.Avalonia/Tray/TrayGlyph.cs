using Tokendial.Core.Model;

namespace Tokendial.Linux.Tray;

/// <summary>
/// What the tray dial shows, and nothing finer: the band that colours it and the sweep in whole percent.
/// At 32 px one percent of the arc is half a pixel, so two readings with the same glyph look identical and
/// the icon is not rebuilt between them.
/// </summary>
internal readonly record struct TrayGlyph(Band? Band, int Percent)
{
    public static readonly TrayGlyph Empty = new(null, 0);

    public static TrayGlyph Of(double? worstFraction) => worstFraction is double f && f > 0.01
        ? new TrayGlyph(Bands.Of(f), (int)Math.Round(Math.Clamp(f, 0, 1) * 100))
        : Empty;
}
