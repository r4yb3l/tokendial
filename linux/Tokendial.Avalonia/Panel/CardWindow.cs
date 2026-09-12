using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Tokendial.Linux.Interop;

namespace Tokendial.Linux.Panel;

/// <summary>
/// The hover card lives in a window of its own. On Windows it shares the dock's, which is made oversized to
/// hold it; on X11 that would mean recomputing the input shape every time the card appears, moves or goes,
/// because the shape is the only thing standing between the reserved area and every click landing on it.
/// macOS reached the same conclusion for its own reasons and uses a second NSPanel, so this is the shape the
/// product already has on one platform rather than a new idea.
/// </summary>
/// <remarks>
/// The window is mapped once and then never unmapped. Showing an X11 window puts it wherever the server
/// chooses until a move request lands, so a card that was shown and then positioned appeared for a frame in
/// the wrong place - a visible flash every time the pointer entered the dock. Mapped once, it is moved while
/// invisible and only then faded in.
/// </remarks>
public sealed class CardWindow : Window
{
    private bool primed;

    public CardWindow()
    {
        Title = "Tokendial card";
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Opacity = 0;
        Opened += (_, _) => Dress();
    }

    private void Dress()
    {
        var handle = TryGetPlatformHandle();
        if (handle is null || !X11.Open()) return;
        X11.MakeDock(handle.Handle);
        X11.RefuseFocus(handle.Handle);
        // The card is something to read, not something to press: an empty input region means every click
        // goes to whatever is behind it, so it can never swallow one.
        X11.InputShape(handle.Handle);
    }

    /// <summary>Shows the card under a point, kept inside the work area so it is never half off the screen.</summary>
    public void ShowAt(Control content, PixelPoint anchor, PixelRect area, double scale)
    {
        Opacity = 0;
        Content = content;

        if (!primed)
        {
            primed = true;
            // Far enough away that the first map cannot be seen wherever the server decides to put it.
            Position = new PixelPoint(area.X - 4000, area.Y - 4000);
            Show();
        }

        // The card sizes itself to its content, which is not known until it has been measured once.
        // Window has a Dispatcher property of its own, which hides the type.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var width = (int)Math.Round(Bounds.Width * scale);
            var height = (int)Math.Round(Bounds.Height * scale);
            // Clamped into the work area, with the low bound never allowed above the high one: a screen
            // narrower than the card would otherwise make Math.Clamp throw rather than place it badly.
            var low = area.X + 8;
            var high = Math.Max(low, area.X + area.Width - width - 8);
            Position = new PixelPoint(
                Math.Clamp(anchor.X - width / 2, low, high),
                Math.Min(anchor.Y, area.Y + area.Height - height - 8));
            // Visible only once it is where it belongs.
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Opacity = 1, DispatcherPriority.Render);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Swaps the contents of a card that is already up, without moving or re-showing it.</summary>
    public void Replace(Control content)
    {
        if (Opacity > 0) Content = content;
    }

    public void HideCard() => Opacity = 0;
}
