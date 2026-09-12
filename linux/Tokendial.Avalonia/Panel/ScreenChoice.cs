using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Tokendial.Core.Model;

namespace Tokendial.Linux.Panel;

/// <summary>
/// Avalonia's monitors, in the shape Core reasons about.
/// </summary>
/// <remarks>
/// Everything that places a window - the dock, its hover card, the banner column - asks this rather than
/// <c>Screens.Primary</c>, so all three land on the same monitor and the choice is made in one place.
/// <para>
/// Avalonia's own <c>WorkingArea</c> is used rather than the <c>_NET_WORKAREA</c> this app used to read by
/// hand: EWMH defines that property per virtual desktop, not per monitor, so on a dual-head desktop it is
/// the union of both screens. Avalonia intersects it with each monitor's bounds, which is the rectangle a
/// window actually wants.
/// </para>
/// </remarks>
public static class ScreenChoice
{
    public static IReadOnlyList<ScreenInfo> All(Screens screens) =>
        screens.All.Select(Of).ToList();

    /// <summary>The chosen monitor, or the primary one when nothing is chosen or the chosen one is unplugged.</summary>
    public static ScreenInfo? Target(Screens screens, string? name) => Displays.Choose(All(screens), name);

    private static ScreenInfo Of(Screen screen) => new(
        screen.DisplayName ?? "",
        screen.IsPrimary,
        Box(screen.Bounds),
        Box(screen.WorkingArea),
        screen.Scaling);

    public static PixelBox Box(PixelRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    public static PixelRect Rect(PixelBox box) => new(box.X, box.Y, box.Width, box.Height);

    /// <summary>The work area to place against, with a sane rectangle when there is no monitor to ask.</summary>
    public static PixelRect WorkingArea(ScreenInfo? screen) =>
        screen is null ? new PixelRect(0, 0, 1920, 1080) : Rect(screen.WorkingArea);

    public static double Scaling(ScreenInfo? screen) => screen?.Scaling ?? 1.0;
}
