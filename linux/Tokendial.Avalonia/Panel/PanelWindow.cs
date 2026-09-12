using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Tokendial.Core.Model;
using Tokendial.Core.Settings;
using Tokendial.Linux.Interop;

// Avalonia's StyledElement already has a Theme property, which shadows the token class inside any
// control. The class keeps the name it has on Windows and macOS - the three are meant to read alike -
// and the alias is what lets it be reached from inside a control.
using Tokens = Tokendial.Linux.Panel.Theme;

namespace Tokendial.Linux.Panel;

/// <summary>
/// The dock on a screen edge: a row along the top or bottom, an upright column down the left or right
/// that wraps into more columns when the work area runs out. Every window-manager concession it needs
/// was measured on Cinnamon before this was written - see tasks/lessons.md - so the X11 calls here are
/// the ones that were proven.
/// </summary>
/// <remarks>
/// The window is always the expanded size and the capsule animates inside it, which is what the Windows
/// dock does: a window that resized with the animation would make the compositor fight the spring, and on
/// X11 the input shape would have to be rewritten on every frame. Only the shape changes when the state
/// does. All placement math lives in <see cref="DockGeometry"/>; this window applies it.
/// </remarks>
public sealed class PanelWindow : Window
{
    private readonly Canvas root = new();
    // Clipped: the expanded cells are laid out for the open capsule, so while they fade in they would
    // otherwise be drawn outside a capsule that has not grown yet - a flash of content floating over
    // the transparent margin. Clipping makes the shape reveal them, which is what the growth is for.
    private readonly Canvas capsule = new() { ClipToBounds = true };
    // Fully qualified: implicit usings bring System.IO.Path into scope alongside the shape.
    private readonly Avalonia.Controls.Shapes.Path dockFill = new();
    private readonly Avalonia.Controls.Shapes.Path dockEdge = new();
    private readonly StackPanel compactRow = new() { Spacing = Tokens.CompactSpacing };
    private readonly Canvas expandedRow = new() { IsVisible = false };

    private readonly CardWindow card = new();
    private readonly IReadOnlyList<string> providerIds;

    private readonly List<Dial> dials = [];
    private readonly List<MarkView> marks = [];
    private readonly List<Cell> cells = [];
    private readonly List<Tile> tiles = [];

    public event Action? SettingsRequested;

    /// <summary>
    /// The dock opened or closed. The alert engine silences thresholds while it is open, on the reasoning
    /// that someone reading the numbers does not need to be told them.
    /// </summary>
    public event Action<bool>? HoverChanged;

    private DispatcherTimer? poll;
    private EventHandler? screensHandler;
    private DockEdge edge;
    private DockGeometry.Layout? layout;
    private bool alive;
    private bool placing;
    private bool repositionQueued;
    private bool x11;
    private bool expanded;
    private int hovered = -1;
    private DateTimeOffset pinnedUntil = DateTimeOffset.MinValue;
    private string? display;

    public PanelWindow(IReadOnlyList<string> providerIds, string? display = null, DockEdge edge = DockEdge.Top)
    {
        this.providerIds = providerIds;
        this.display = display;
        this.edge = edge;
        Title = "Tokendial";
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        dockFill.Fill = Tokens.Surface;
        dockEdge.Stroke = Tokens.SurfaceEdge;
        dockEdge.StrokeThickness = 1;

        capsule.Children.Add(dockFill);
        capsule.Children.Add(dockEdge);
        capsule.Children.Add(compactRow);
        capsule.Children.Add(expandedRow);
        root.Children.Add(capsule);
        Content = root;

        // Right-click anywhere on the capsule reaches settings, as it does on Windows - the dock is the
        // only part of Tokendial that is always where you left it.
        capsule.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == Avalonia.Input.MouseButton.Right) SettingsRequested?.Invoke();
        };

        Opened += OnOpened;
    }

    /// <summary>Which edge the dock hangs from.</summary>
    public DockEdge Edge => edge;

    internal DockGeometry.Layout? LastLayout => layout;

    private void OnOpened(object? sender, EventArgs e)
    {
        Build();

        var handle = TryGetPlatformHandle();
        x11 = handle is { HandleDescriptor: "XID", Handle: not 0 } && X11.Open();
        if (x11)
        {
            X11.MakeDock(handle!.Handle);
            X11.RefuseFocus(handle.Handle);
        }
        alive = true;
        Reposition();

        // Fires on XRandR reconfiguration and when the work area changes, already on the UI thread.
        // Without it the dock stays where it was when a monitor arrives, leaves or a panel appears.
        // Stored so close can unsubscribe: a dead window must not reposition itself.
        screensHandler = (_, _) => { if (alive) Reposition(); };
        Screens.Changed += screensHandler;
        ScalingChanged += OnScalingChanged;

        StartPolling();
    }

    protected override void OnClosed(EventArgs e)
    {
        alive = false;
        poll?.Stop();
        poll = null;
        if (screensHandler is not null) Screens.Changed -= screensHandler;
        screensHandler = null;
        ScalingChanged -= OnScalingChanged;
        hovered = -1;
        card.Shutdown();
        base.OnClosed(e);
    }

    private void Build()
    {
        Orient();
        foreach (var id in providerIds)
        {
            var dial = new Dial(Tokens.CompactDial, Tokens.CompactStroke) { Hollow = true };
            var mark = new MarkView(id, Tokens.CompactMark)
            {
                Fill = Tokens.TextDisabled,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            dials.Add(dial);
            marks.Add(mark);
            tiles.Add(null!);
            // The mark sits inside the ring, which is why the dial is hollow in the middle.
            compactRow.Children.Add(new Avalonia.Controls.Panel
            {
                Width = Tokens.CompactDial, Height = Tokens.CompactDial, Children = { dial, mark }
            });

            var cell = new Cell(id);
            cells.Add(cell);
            expandedRow.Children.Add(cell.Root);
        }

        // The spring is the product's, not Avalonia's: three platforms solve the same damped spring so the
        // capsule opens with the same weight everywhere.
        capsule.Transitions =
        [
            new DoubleTransition { Property = WidthProperty, Duration = Spring.Expand.SettleTime, Easing = Spring.Expand },
            new DoubleTransition { Property = HeightProperty, Duration = Spring.Expand.SettleTime, Easing = Spring.Expand }
        ];
        capsule.PropertyChanged += (_, e) =>
        {
            if (e.Property == WidthProperty || e.Property == HeightProperty) Layout();
        };

        var at = Relayout(expanded: false);
        capsule.Width = at.CapsuleWidth;
        capsule.Height = at.CapsuleHeight;
        Layout();
    }

    /// <summary>Rows run along horizontal edges and down vertical ones; cells stay upright either way.</summary>
    private void Orient()
    {
        var vertical = DockPlacement.IsVertical(edge);
        compactRow.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
    }

    /// <summary>The window's actual render scale for pixel conversions, never zero.</summary>
    private double PxScale
    {
        get
        {
            if (RenderScaling > 0) return RenderScaling;
            return ScreenChoice.Scaling(Target());
        }
    }

    /// <summary>
    /// Recomputes the placement for the current edge, screen and scale, and applies it. The card is
    /// dismissed: whatever it showed belongs to a capsule that is no longer there.
    /// </summary>
    private DockGeometry.Layout Relayout(bool expanded) => Relayout(expanded, Target(), PxScale);

    private DockGeometry.Layout Relayout(bool expanded, ScreenInfo? target, double scale)
    {
        layout = DockGeometry.Compute(target, edge, providerIds.Count, expanded, scale, ScreenChoice.All(Screens));
        Width = layout.WindowWidth;
        Height = layout.WindowHeight;
        return layout;
    }

    /// <summary>Redraws the trapezoid at the capsule's current size and re-centres what it carries.</summary>
    private void Layout()
    {
        if (layout is null) return;
        var at = layout;
        var vertical = at.Vertical;
        var actualAlong = vertical ? capsule.Height : capsule.Width;
        var actualAcross = vertical ? capsule.Width : capsule.Height;
        if (double.IsNaN(actualAlong) || double.IsNaN(actualAcross)) return;

        var radius = expanded ? Tokens.ExpandedRadius : Tokens.CompactRadius;
        dockFill.Data = DockShape.Fill(actualAlong, actualAcross, Tokens.DockSlant, radius, edge);
        dockEdge.Data = DockShape.Edge(actualAlong, actualAcross, Tokens.DockSlant, radius, edge);

        var actualWidth = double.IsNaN(capsule.Width) ? 0 : capsule.Width;
        var actualHeight = double.IsNaN(capsule.Height) ? 0 : capsule.Height;
        switch (edge)
        {
            case DockEdge.Bottom:
                Canvas.SetLeft(capsule, (Width - actualWidth) / 2);
                Canvas.SetTop(capsule, Height - actualHeight);
                break;
            case DockEdge.Left:
                Canvas.SetLeft(capsule, 0);
                Canvas.SetTop(capsule, (Height - actualHeight) / 2);
                break;
            case DockEdge.Right:
                Canvas.SetLeft(capsule, Width - actualWidth);
                Canvas.SetTop(capsule, (Height - actualHeight) / 2);
                break;
            default:
                Canvas.SetLeft(capsule, (Width - actualWidth) / 2);
                Canvas.SetTop(capsule, 0);
                break;
        }

        var compactContent = at.CompactAlong - 2 * Tokens.DockSlant - 2 * Tokens.CompactPadding;
        if (vertical)
        {
            Canvas.SetLeft(compactRow, (actualWidth - Tokens.CompactDial) / 2);
            Canvas.SetTop(compactRow, (actualAlong - compactContent) / 2);
        }
        else
        {
            Canvas.SetLeft(compactRow, (actualAlong - compactContent) / 2);
            Canvas.SetTop(compactRow, (Tokens.CompactHeight - Tokens.CompactDial) / 2);
        }

        expandedRow.Width = actualWidth;
        expandedRow.Height = actualHeight;
        for (var i = 0; i < cells.Count; i++)
        {
            var rect = DockGeometry.CellRect(at, i);
            var cell = cells[i].Root;
            Canvas.SetLeft(cell, rect.X - at.CapsuleLeft);
            Canvas.SetTop(cell, rect.Y - at.CapsuleTop);
            cell.Width = rect.Width;
            cell.Height = rect.Height;
        }
    }

    /// <summary>The monitor the dock belongs on: the chosen one, or the primary one.</summary>
    private Tokendial.Core.Model.ScreenInfo? Target() => ScreenChoice.Target(Screens, display);

    /// <summary>
    /// Which monitor to live on. Re-read on every placement, so unplugging the chosen one moves the dock to
    /// the primary monitor and plugging it back in brings the dock home without anything being asked.
    /// </summary>
    public void SetDisplay(string? name)
    {
        if (display == name) return;
        display = name;
        Reposition();
    }

    /// <summary>
    /// Which edge to hang from. Snaps rather than springs there - the Windows dock does the same - and
    /// dismisses the card, which belongs to the old edge.
    /// </summary>
    public void SetEdge(DockEdge next)
    {
        if (next == edge) return;
        edge = next;
        hovered = -1;
        card.HideCard();
        Orient();
        // The capsule would otherwise spring from the old edge's size to the new one, sweeping across the
        // screen; the swap is instant and only hovering animates.
        var transitions = capsule.Transitions;
        capsule.Transitions = null;
        try
        {
            var at = Relayout(expanded);
            capsule.Width = at.CapsuleWidth;
            capsule.Height = at.CapsuleHeight;
        }
        finally
        {
            capsule.Transitions = transitions;
        }
        Reposition();
    }

    /// <summary>The monitor, its work area, the edge or the screen layout changed: put the dock where it now belongs.</summary>
    public void Reposition() => Reposition(Target(), PxScale);

    internal void Reposition(ScreenInfo? target, double scale)
    {
        if (placing) { QueueReposition(); return; }
        placing = true;
        try
        {
            var at = Relayout(expanded, target, scale);
            var transitions = capsule.Transitions;
            capsule.Transitions = null;
            try
            {
                capsule.Width = at.CapsuleWidth;
                capsule.Height = at.CapsuleHeight;
            }
            finally { capsule.Transitions = transitions; }
            // The capsule the card described is gone from where it was: never leave it showing stale.
            hovered = -1;
            card.HideCard();
            var handle = TryGetPlatformHandle();
            if (handle is not null)
            {
                Position = new PixelPoint(at.Placement.X, at.Placement.Y);
                if (x11) Shape(at);
            }
            Layout();
            Tokendial.Core.Diagnostics.Log.Ui.Info($"dock placement: saved={display ?? "automatic"}, " +
                $"target={target?.Name ?? "fallback"}, edge={edge}, scale={scale}, " +
                $"pixels={at.Placement}, grid={at.Grid.PerPrimary}x{at.Grid.Secondary}");
        }
        finally
        {
            placing = false;
        }
    }

    private void OnScalingChanged(object? sender, EventArgs e) => QueueReposition();

    private void QueueReposition()
    {
        if (!alive || repositionQueued) return;
        repositionQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            repositionQueued = false;
            if (alive) Reposition();
        });
    }

    /// <summary>
    /// Only the capsule takes clicks. Everything else in the window - the margins either side and the hot
    /// zone toward the interior - stays outside the input region so a click there reaches whatever is
    /// behind, while the cursor poll still sees the pointer in it.
    /// </summary>
    private void Shape(DockGeometry.Layout at)
    {
        var handle = TryGetPlatformHandle();
        if (handle is null) return;
        X11.InputShape(handle.Handle, at.Input);
    }



    /// <summary>
    /// Applies a snapshot. The window keeps no readings of its own and asks no provider anything; it is
    /// handed a model built off the store and the hub, exactly as the Windows dock is.
    /// </summary>
    public void Update(PanelModel model)
    {
        for (var i = 0; i < tiles.Count && i < model.Tiles.Count; i++)
        {
            var tile = model.Tiles.FirstOrDefault(t => t.Id == providerIds[i]);
            if (tile is null) continue;
            tiles[i] = tile;

            var dial = dials[i];
            dial.Hollow = !tile.HasReading;
            dial.Fill = Tokens.Of(tile.Band);
            dial.Fraction = tile.HasReading ? Math.Clamp(tile.Fraction ?? 0, 0, 1) : 0;
            marks[i].Fill = tile.HasReading ? Tokens.TextPrimary : Tokens.TextDisabled;
            cells[i].Apply(tile);
        }

        // A card already open should follow the numbers behind it rather than go stale until the pointer
        // moves; rebuilding it in place is cheap and is what keeps a reset countdown honest.
        if (hovered >= 0 && hovered < tiles.Count && tiles[hovered] is Tile shown)
            card.Replace(HoverCard.Build(shown, DateTimeOffset.Now));
    }

    /// <summary>
    /// Polled, not evented, for the reason the Windows dock polls: a transparent window whose input region
    /// excludes the hot zone never receives enter or leave for the part that matters most.
    /// </summary>
    private void StartPolling()
    {
        poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        poll.Tick += (_, _) => Track();
        poll.Start();
    }

    /// <summary>
    /// Holds the dock open for a while with no pointer involved. The tray's Show does this rather than
    /// moving the cursor, because moving someone's cursor is not a thing an application should do.
    /// </summary>
    public void Flash(TimeSpan? duration = null)
    {
        pinnedUntil = DateTimeOffset.UtcNow + (duration ?? TimeSpan.FromSeconds(6));
        if (!expanded) Reconcile(true);
    }

    private void Track()
    {
        // No X connection - headless tests, or a session without one - means no pointer to read.
        if (!x11 || layout is null) return;
        var (x, y, sameScreen) = X11.Pointer();
        if (!sameScreen) return;

        var scale = PxScale;
        var handle = TryGetPlatformHandle();
        if (handle is null) return;
        var (ox, oy, ok) = X11.RootOrigin(handle.Handle);
        if (!ok) return;
        var origin = new PixelPoint(ox, oy);
        var local = new Point((x - origin.X) / scale, (y - origin.Y) / scale);

        // The strip toward the interior counts as hovering the capsule, so the pointer does not have to
        // stay on the capsule for the dock to remain open.
        var inside = layout.Hot.Contains(local);

        // A pinned dock stays open until its moment passes, whatever the pointer is doing.
        var open = inside || DateTimeOffset.UtcNow < pinnedUntil;
        if (open != expanded) Reconcile(open);
        Card(inside, local, origin);
    }

    private void Reconcile(bool open)
    {
        var changed = expanded != open;
        expanded = open;
        var at = Relayout(open);
        capsule.Width = at.CapsuleWidth;
        capsule.Height = at.CapsuleHeight;
        // Swapped outright rather than cross-faded. Animating a container's opacity makes the renderer
        // compose it through a layer for the duration and then draw it straight to the surface at the end,
        // and that hand-off is visible as a flash - the text appears to repaint. With no layer there is
        // nothing to hand off, and the capsule growing over its own clip is the whole transition.
        compactRow.IsVisible = !open;
        expandedRow.IsVisible = open;
        Layout();
        if (x11) Shape(at);
        if (!open) { hovered = -1; card.HideCard(); }
        if (changed) HoverChanged?.Invoke(open);
    }

    private void Card(bool inside, Point local, PixelPoint origin)
    {
        if (layout is null) return;
        if (!inside || !expanded) { if (hovered != -1) { hovered = -1; card.HideCard(); } return; }

        // The card hangs off the capsule toward the interior. Showing it while the capsule is still
        // growing puts it where the dock is about to be rather than where it is, which reads as a jump.
        if (Math.Abs(capsule.Bounds.Width - layout.CapsuleWidth) > 1
            || Math.Abs(capsule.Bounds.Height - layout.CapsuleHeight) > 1) return;

        var index = DockGeometry.CellAt(layout, local);
        if (index == hovered) return;
        hovered = index;

        if (index < 0 || tiles.Count <= index || tiles[index] is null) { card.HideCard(); return; }

        var cell = DockGeometry.CellRect(layout, index);
        var anchor = DockGeometry.CardAnchor(layout, cell, PxScale);
        anchor = new PixelPoint(anchor.X + origin.X - layout.Placement.X,
            anchor.Y + origin.Y - layout.Placement.Y);
        // The chosen monitor's work area, not the primary one's: clamping to the primary screen would drag
        // the card off the dock and back onto another monitor entirely.
        card.ShowAt(HoverCard.Build(tiles[index], DateTimeOffset.Now), anchor, ScreenChoice.WorkingArea(Target()), PxScale, edge);
    }
}
