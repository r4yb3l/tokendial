using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Tokendial.Linux.Interop;

namespace Tokendial.Linux.Panel;

/// <summary>
/// The hover card lives in a window of its own. On Windows it shares the dock's, which is made oversized to
/// hold it; on X11 that would mean recomputing the input shape every time the card appears, moves or goes,
/// because the shape is the only thing standing between the reserved area and every click landing on it.
/// macOS reached the same conclusion for its own reasons and uses a second NSPanel, so this is the shape the
/// product already has on one platform rather than a new idea.
/// </summary>
public sealed class CardWindow : Window
{
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
    public void ShowAt(Control content, PixelPoint anchor, PixelRect area)
    {
        Content = content;
        if (!IsVisible) Show();

        // The card sizes itself to its content, which is not known until it has been measured once.
        // Window has a Dispatcher property of its own, which hides the type.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var width = (int)Math.Round(Bounds.Width * (Screens.Primary?.Scaling ?? 1));
            var height = (int)Math.Round(Bounds.Height * (Screens.Primary?.Scaling ?? 1));
            var x = Math.Clamp(anchor.X - width / 2, area.X + 8, area.X + area.Width - width - 8);
            var y = Math.Min(anchor.Y, area.Y + area.Height - height - 8);
            Position = new PixelPoint(x, y);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    public void HideCard()
    {
        if (IsVisible) Hide();
    }
}
