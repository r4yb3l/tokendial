using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Tokendial.Core.Model;
using Tokendial.Core.Settings;
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
    private long generation;
    private Placement? placement;
    private sealed record Placement(PixelPoint Anchor, PixelRect Area, double Scale, DockEdge Edge);

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
        ScalingChanged += (_, _) => QueuePlacement();
        SizeChanged += (_, _) => QueuePlacement();
    }

    private void Dress()
    {
        var handle = TryGetPlatformHandle();
        if (handle is not { HandleDescriptor: "XID", Handle: not 0 } || !X11.Open()) return;
        X11.MakeDock(handle.Handle);
        X11.RefuseFocus(handle.Handle);
        // The card is something to read, not something to press: an empty input region means every click
        // goes to whatever is behind it, so it can never swallow one.
        X11.InputShape(handle.Handle);
    }

    /// <summary>
    /// Shows the card off an anchor on its outer side, kept inside the work area on both axes so it is
    /// never half off the screen - including a work area smaller than the card, or one at a negative
    /// origin. The card opens toward the interior on every edge: below a top dock, above a bottom one,
    /// beside a column.
    /// </summary>
    public void ShowAt(Control content, PixelPoint anchor, PixelRect area, double scale, DockEdge edge = DockEdge.Top)
    {
        generation++;
        Opacity = 0;
        Content = content;
        placement = new Placement(anchor, area, scale, edge);
        // Move invisibly onto the destination monitor before measuring at that window's render scale.
        Position = anchor;
        if (!primed)
        {
            primed = true;
            Show();
        }
        QueuePlacement();
    }

    private void QueuePlacement()
    {
        if (placement is not { } request) return;
        var seen = generation;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (seen != generation || placement is null) return;
            var scale = RenderScaling > 0 ? RenderScaling : request.Scale;
            var width = (int)Math.Round(Bounds.Width * scale);
            var height = (int)Math.Round(Bounds.Height * scale);
            Position = DockGeometry.ClampCard(request.Edge, width, height, request.Anchor, ScreenChoice.Box(request.Area));
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (seen == generation && placement is not null) Opacity = 1;
            }, DispatcherPriority.Render);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Swaps the contents of a card that is already up, without moving or re-showing it.</summary>
    public void Replace(Control content)
    {
        if (Opacity > 0) Content = content;
    }

    /// <summary>
    /// Dismisses the card and invalidates any measure queued by a ShowAt still in flight, so it cannot
    /// place - and thereby re-show - a card that is already gone.
    /// </summary>
    public void HideCard()
    {
        generation++;
        placement = null;
        Opacity = 0;
    }

    /// <summary>What the dock calls on its way out: hide, and unmap the window if it was ever mapped.</summary>
    public void Shutdown()
    {
        HideCard();
        if (primed) Close();
    }
}
