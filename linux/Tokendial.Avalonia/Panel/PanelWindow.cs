using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Tokendial.Core.Model;
using Tokendial.Linux.Interop;

// Avalonia's StyledElement already has a Theme property, which shadows the token class inside any
// control. The class keeps the name it has on Windows and macOS - the three are meant to read alike -
// and the alias is what lets it be reached from inside a control.
using Tokens = Tokendial.Linux.Panel.Theme;

namespace Tokendial.Linux.Panel;

/// <summary>
/// The dock: a trapezoid hanging from the top edge that carries a row of dials and opens into a grid when
/// the pointer reaches it. Every window-manager concession it needs was measured on Cinnamon before this was
/// written - see tasks/lessons.md - so the X11 calls here are the ones that were proven.
/// </summary>
/// <remarks>
/// The window is always the expanded size and the capsule animates inside it, which is what the Windows dock
/// does: a window that resized with the animation would make the compositor fight the spring, and on X11 the
/// input shape would have to be rewritten on every frame. Only the shape changes when the state does.
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
    private readonly StackPanel compactRow = new() { Orientation = Orientation.Horizontal, Spacing = Tokens.CompactSpacing };
    private readonly StackPanel expandedRow = new() { Orientation = Orientation.Horizontal, IsVisible = false };

    private readonly CardWindow card = new();
    private readonly IReadOnlyList<string> providerIds;

    private readonly List<Dial> dials = [];
    private readonly List<MarkView> marks = [];
    private readonly List<Cell> cells = [];
    private readonly List<Tile> tiles = [];

    public event Action? SettingsRequested;

    private DispatcherTimer? poll;
    private double compactAlong, expandedAlong;
    private bool expanded;
    private int hovered = -1;

    public PanelWindow(IReadOnlyList<string> providerIds)
    {
        this.providerIds = providerIds;
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

    private void OnOpened(object? sender, EventArgs e)
    {
        Build();

        var handle = TryGetPlatformHandle();
        if (handle is not null && X11.Open())
        {
            X11.MakeDock(handle.Handle);
            X11.RefuseFocus(handle.Handle);
            Place();
            Shape();
        }

        StartPolling();
    }


    private void Build()
    {
        var count = providerIds.Count;
        compactAlong = 2 * Tokens.DockSlant + 2 * Tokens.CompactPadding + count * Tokens.CompactDial + (count - 1) * Tokens.CompactSpacing;
        expandedAlong = 2 * Tokens.DockSlant + Math.Max(2 * Tokens.ExpandedPadding + count * Tokens.CellWidth,
                                                        Tokens.CardWidth + 2 * Tokens.ExpandedPadding);

        Width = expandedAlong;
        Height = Tokens.ExpandedHeight + Tokens.HotZone;

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

        capsule.Width = compactAlong;
        capsule.Height = Tokens.CompactHeight;
        Layout();
    }

    /// <summary>Redraws the trapezoid at the capsule's current size and re-centres what it carries.</summary>
    private void Layout()
    {
        var along = capsule.Width;
        var across = capsule.Height;
        if (double.IsNaN(along) || double.IsNaN(across)) return;

        var radius = expanded ? Tokens.ExpandedRadius : Tokens.CompactRadius;
        dockFill.Data = DockShape.Fill(along, across, Tokens.DockSlant, radius);
        dockEdge.Data = DockShape.Edge(along, across, Tokens.DockSlant, radius);

        Canvas.SetLeft(capsule, (Width - along) / 2);
        Canvas.SetTop(capsule, 0);

        var compactWidth = compactAlong - 2 * Tokens.DockSlant - 2 * Tokens.CompactPadding;
        Canvas.SetLeft(compactRow, (along - compactWidth) / 2);
        Canvas.SetTop(compactRow, (Tokens.CompactHeight - Tokens.CompactDial) / 2);

        Canvas.SetLeft(expandedRow, (along - providerIds.Count * Tokens.CellWidth) / 2);
        Canvas.SetTop(expandedRow, Tokens.ExpandedPadding);
    }

    /// <summary>Centred on the top edge of the work area, so the desktop panel is never covered.</summary>
    private void Place()
    {
        var area = Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var scale = Screens.Primary?.Scaling ?? 1.0;
        Position = new PixelPoint(area.X + (area.Width - (int)Math.Round(Width * scale)) / 2, area.Y);
    }

    /// <summary>
    /// Only the capsule takes clicks. Everything else in the window - the margins either side and the hot
    /// zone below - stays outside the input region so a click there reaches whatever is behind, while the
    /// cursor poll still sees the pointer in it.
    /// </summary>
    private void Shape()
    {
        var handle = TryGetPlatformHandle();
        if (handle is null) return;
        var scale = Screens.Primary?.Scaling ?? 1.0;
        var along = expanded ? expandedAlong : compactAlong;
        var across = expanded ? Tokens.ExpandedHeight : Tokens.CompactHeight;
        X11.InputShape(handle.Handle, new X11.XRectangle
        {
            X = (short)Math.Round((Width - along) / 2 * scale),
            Y = 0,
            Width = (ushort)Math.Round(along * scale),
            Height = (ushort)Math.Round(across * scale)
        });
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

    private void Track()
    {
        var (x, y, sameScreen) = X11.Pointer();
        if (!sameScreen) return;

        var scale = Screens.Primary?.Scaling ?? 1.0;
        var origin = Position;
        var local = new Point((x - origin.X) / scale, (y - origin.Y) / scale);

        var along = expanded ? expandedAlong : compactAlong;
        var across = expanded ? Tokens.ExpandedHeight : Tokens.CompactHeight;
        var left = (Width - along) / 2;
        // The strip below the capsule counts as hovering it, so the pointer does not have to stay on the
        // capsule for it to remain open.
        var inside = local.X >= left && local.X <= left + along && local.Y >= 0 && local.Y <= across + Tokens.HotZone;

        if (inside != expanded) Reconcile(inside);
        Card(inside, local, left, origin, scale);
    }

    private void Reconcile(bool open)
    {
        expanded = open;
        capsule.Width = open ? expandedAlong : compactAlong;
        capsule.Height = open ? Tokens.ExpandedHeight : Tokens.CompactHeight;
        // Swapped outright rather than cross-faded. Animating a container's opacity makes the renderer
        // compose it through a layer for the duration and then draw it straight to the surface at the end,
        // and that hand-off is visible as a flash - the text appears to repaint. With no layer there is
        // nothing to hand off, and the capsule growing over its own clip is the whole transition.
        compactRow.IsVisible = !open;
        expandedRow.IsVisible = open;
        Layout();
        Shape();
        if (!open) { hovered = -1; card.HideCard(); }
    }

    private void Card(bool inside, Point local, double left, PixelPoint origin, double scale)
    {
        if (!inside || !expanded) { if (hovered != -1) { hovered = -1; card.HideCard(); } return; }

        // The card hangs off the bottom of the open capsule. Showing it while the capsule is still growing
        // puts it where the dock is about to be rather than where it is, which reads as a jump.
        if (capsule.Bounds.Height < Tokens.ExpandedHeight - 1) return;

        var index = CellAt(local.X - left);
        if (index == hovered) return;
        hovered = index;

        if (index < 0 || tiles.Count <= index || tiles[index] is null) { card.HideCard(); return; }

        var cellLeft = (expandedAlong - providerIds.Count * Tokens.CellWidth) / 2 + index * Tokens.CellWidth;
        var anchor = new PixelPoint(
            origin.X + (int)Math.Round((left + cellLeft + Tokens.CellWidth / 2) * scale),
            origin.Y + (int)Math.Round((Tokens.ExpandedHeight + 6) * scale));
        card.ShowAt(HoverCard.Build(tiles[index], DateTimeOffset.Now), anchor,
            Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080));
    }

    /// <summary>Which expanded cell a position within the capsule falls in.</summary>
    private int CellAt(double x)
    {
        var first = (expandedAlong - providerIds.Count * Tokens.CellWidth) / 2;
        var index = (int)Math.Floor((x - first) / Tokens.CellWidth);
        return index >= 0 && index < cells.Count ? index : -1;
    }
}
