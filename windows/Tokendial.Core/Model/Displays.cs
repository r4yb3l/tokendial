using Tokendial.Core.Settings;

namespace Tokendial.Core.Model;

/// <summary>A rectangle in physical pixels, in the desktop's own coordinates.</summary>
/// <remarks>
/// Core cannot name a WPF <c>RECT</c> or an Avalonia <c>PixelRect</c>, and does not need to: the two apps
/// convert at their edges. Left/Top/Right/Bottom are spelled out because the placement arithmetic below
/// reads as the Windows original does.
/// </remarks>
public readonly record struct PixelBox(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>One monitor, as much of it as placing a dock on it requires.</summary>
/// <param name="Name">
/// The platform's own name for the display, and the value <see cref="Settings.Settings.Display"/> stores:
/// an XRandR output on Linux (<c>HDMI-1</c>), a device path on Windows (<c>\\.\DISPLAY2</c>), the
/// localized name on macOS. The three never have to agree - settings files are already per-platform.
/// </param>
public sealed record ScreenInfo(string Name, bool IsPrimary, PixelBox Bounds, PixelBox WorkingArea, double Scaling);

/// <summary>Which monitor the dock belongs on.</summary>
public static class Displays
{
    /// <summary>
    /// The saved display if it is attached, the primary one otherwise.
    /// </summary>
    /// <remarks>
    /// Matched by name on every placement rather than remembered as an index or a handle, and that is the
    /// whole point: a monitor that is unplugged falls back to the primary one, and the moment it is plugged
    /// back in the dock returns to it on its own. Nothing strands the dock somewhere the user cannot reach
    /// it, and nothing asks them to set the preference again.
    /// </remarks>
    public static ScreenInfo? Choose(IReadOnlyList<ScreenInfo> screens, string? saved)
    {
        if (screens.Count == 0) return null;
        if (saved is { Length: > 0 })
        {
            var named = screens.FirstOrDefault(s => string.Equals(s.Name, saved, StringComparison.Ordinal));
            if (named is not null) return named;
        }
        return screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];
    }

    /// <summary>Whether a saved preference names a display that is not currently attached, so settings can say so.</summary>
    public static bool Missing(IReadOnlyList<ScreenInfo> screens, string? saved) =>
        saved is { Length: > 0 } && !screens.Any(s => string.Equals(s.Name, saved, StringComparison.Ordinal));
}

/// <summary>
/// Where the dock's window goes on a given monitor, in physical pixels.
/// </summary>
/// <remarks>
/// Lifted out of windows/Tokendial.App/Panel/PanelWindow.cs so that the Linux dock can gain its four edges
/// without a second copy of the arithmetic, and so both can be asserted from a test rather than from a
/// screenshot. The two asymmetries are deliberate and were in the Windows original: a horizontal dock is
/// centred on the <em>full</em> bounds, so a taskbar down one side does not shove the capsule off centre,
/// while a vertical one is centred on the <em>work area</em>, so it never runs under a taskbar.
/// </remarks>
public static class DockPlacement
{
    public static PixelBox For(ScreenInfo screen, DockEdge edge, int width, int height)
    {
        var work = screen.WorkingArea;
        var bounds = screen.Bounds;

        var x = edge switch
        {
            DockEdge.Left => work.Left,
            DockEdge.Right => work.Right - width,
            _ => bounds.Left + (bounds.Width - width) / 2
        };
        var y = edge switch
        {
            // A taskbar along the top pushes the dock below it; without one the dock sits on the very edge.
            DockEdge.Top => work.Top > bounds.Top ? work.Top : bounds.Top,
            DockEdge.Bottom => work.Bottom - height,
            _ => work.Top + (work.Height - height) / 2
        };
        return new PixelBox(x, y, width, height);
    }

    /// <summary>Whether an edge runs down the side of the screen rather than along the top or bottom.</summary>
    public static bool IsVertical(DockEdge edge) => edge is DockEdge.Left or DockEdge.Right;

    /// <summary>The room a dock has along the edge it hangs from, in physical pixels.</summary>
    public static int Available(ScreenInfo screen, DockEdge edge) =>
        IsVertical(edge) ? screen.WorkingArea.Height : screen.WorkingArea.Width;
}
