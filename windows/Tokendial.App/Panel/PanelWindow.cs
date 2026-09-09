using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Tokendial.App.Interop;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.Settings;

namespace Tokendial.App.Panel;

/// <summary>
/// The dock on a screen edge: top or bottom as a row, left or right as a column. A borderless,
/// per-pixel-transparent, always-on-top window that never takes focus; transparent pixels let
/// clicks through, so hover is found by polling the cursor against the dock's bounds rather
/// than from mouse events the window would never receive.
/// </summary>
public sealed class PanelWindow : Window
{
    private static readonly double CardGap = 6;
    private static readonly double CardReserve = 320;

    private readonly Canvas root = new() { UseLayoutRounding = false };
    private readonly Border capsule;
    private readonly Grid capsuleContent = new() { UseLayoutRounding = false };
    private readonly System.Windows.Shapes.Path dockFill = new() { Fill = Theme.Surface, Stretch = Stretch.None, UseLayoutRounding = false, IsHitTestVisible = true };
    private readonly System.Windows.Shapes.Path dockEdge = new() { Stroke = Theme.SurfaceEdge, StrokeThickness = 1, Stretch = Stretch.None, UseLayoutRounding = false, IsHitTestVisible = false };
    private readonly PanelContent content = new();
    private readonly DispatcherTimer hoverTimer = new(DispatcherPriority.Background);
    private readonly DispatcherTimer pinTimer = new(DispatcherPriority.Background);
    private Border? card;
    private string? cardFor;
    private PanelModel model = PanelModel.Empty;
    private PanelMode mode = PanelMode.ExpandOnHover;
    private DockEdge edge = DockEdge.Top;
    private bool hovering;
    private bool expanded;
    private bool pinned;
    private bool placed;
    private Native.Screen? screen;

    public PanelWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Title = "Tokendial";
        Focusable = false;
        SizeToContent = SizeToContent.Manual;
        UseLayoutRounding = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

        var shell = new Grid { UseLayoutRounding = false };
        shell.Children.Add(dockFill);
        shell.Children.Add(dockEdge);
        capsuleContent.Margin = new Thickness(Theme.DockSlant, 0, Theme.DockSlant, 0);
        shell.Children.Add(capsuleContent);
        capsule = new Border
        {
            Background = Brushes.Transparent,
            Width = PanelContent.CompactLength(0) + 2 * Theme.DockSlant,
            Height = Theme.CompactHeight,
            Child = shell,
            UseLayoutRounding = false,
            SnapsToDevicePixels = false
        };
        capsuleContent.Children.Add(content.CompactRow);
        content.Expanded.Opacity = 0;
        content.Expanded.Visibility = Visibility.Collapsed;
        capsuleContent.Children.Add(content.Expanded);
        capsule.SizeChanged += (_, e) =>
        {
            PlaceCapsule();
            ShapeDock(e.NewSize);
        };
        capsule.MouseRightButtonUp += (_, e) => { e.Handled = true; SettingsRequested?.Invoke(); };
        content.CellClicked += id => ProviderClicked?.Invoke(id);
        Canvas.SetTop(capsule, 0);
        root.Children.Add(capsule);
        Content = root;

        hoverTimer.Interval = TimeSpan.FromMilliseconds(300);
        hoverTimer.Tick += (_, _) => PollHover();
        pinTimer.Tick += (_, _) => { pinTimer.Stop(); pinned = false; Reconcile(); };
        SourceInitialized += (_, _) => OnSourceReady();
    }

    public event Action<bool>? HoverChanged;
    public event Action<string>? ProviderClicked;
    public event Action? SettingsRequested;
    public event Action? ExpandedShown;

    public bool IsExpanded => expanded;
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    private bool Vertical => edge is DockEdge.Left or DockEdge.Right;
    private int Count => Math.Max(model.Tiles.Count, 0);

    /// <summary>How the expanded dock arranges itself on this screen, wrapping into more columns when a side dock would otherwise run off it.</summary>
    private Theme.DockLayout Box()
    {
        var available = screen is null ? 1080 : (Vertical ? screen.Work.Height : screen.Work.Width) / screen.Scale;
        return new Theme.DockLayout(Count, Vertical, available);
    }

    private void OnSourceReady()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.MakeUnobtrusive(hwnd);
        HwndSource.FromHwnd(hwnd)?.AddHook((_, msg, _, _, ref handled) =>
        {
            if (msg != Native.WM_MOUSEACTIVATE) return IntPtr.Zero;
            handled = true;
            return new IntPtr(Native.MA_NOACTIVATE);
        });
        Reposition();
    }

    public void SetMode(PanelMode next)
    {
        mode = next;
        if (mode == PanelMode.Hidden) { hoverTimer.Stop(); Hide(); return; }
        if (!IsVisible) { Show(); Reposition(); }
        hoverTimer.Start();
        Reconcile();
    }

    /// <summary>Move the dock to another edge: a row along the top or bottom, a column along the left or right, with the same tiles.</summary>
    public void SetEdge(DockEdge next)
    {
        edge = next;
        capsuleContent.Margin = Vertical ? new Thickness(0, Theme.DockSlant, 0, Theme.DockSlant) : new Thickness(Theme.DockSlant, 0, Theme.DockSlant, 0);
        HideCard();
        Reposition();
        Resize(animate: false);
        content.Apply(model, Now(), animate: false);
        ShapeDock(new Size(capsule.Width, capsule.Height));
    }

    /// <summary>A toast was clicked or a second instance launched: expand for a moment even without the cursor.</summary>
    public void Flash(TimeSpan? duration = null)
    {
        if (mode == PanelMode.Hidden) { SetMode(PanelMode.ExpandOnHover); }
        pinned = true;
        pinTimer.Interval = duration ?? TimeSpan.FromSeconds(5);
        pinTimer.Stop();
        pinTimer.Start();
        Reconcile();
    }

    /// <summary>Rebuild every label in the current language on the next update.</summary>
    public void Relocalize()
    {
        content.Relocalize();
        HideCard();
        Update(model);
    }

    /// <summary>The look changed: repaint the dock and rebuild the tiles in the new palette.</summary>
    public void Retheme()
    {
        dockFill.Fill = Theme.Surface;
        dockEdge.Stroke = Theme.SurfaceEdge;
        Relocalize();
    }

    public void Update(PanelModel next)
    {
        var countChanged = next.Tiles.Count != model.Tiles.Count;
        model = next;
        content.Apply(model, Now(), animate: placed && !countChanged);
        if (countChanged || !placed) Resize(animate: placed);
        if (card is not null && cardFor is not null) ShowCard(cardFor);
    }

    /// <summary>The monitor, its scale, the work area or the edge changed: recompute the window's physical rectangle.</summary>
    public void Reposition()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        screen = Native.PrimaryScreen();
        var box = Box();
        content.SetShape(Vertical, box.CellsPerColumn);
        double widthDip, heightDip;
        if (Vertical)
        {
            widthDip = box.Across + Theme.HotZone + CardGap + Theme.CardWidth + Theme.ExpandedPadding;
            heightDip = box.Along + 2 * Theme.DockSlant + CardReserve;
        }
        else
        {
            widthDip = box.Along + 2 * Theme.ExpandedPadding + 2 * Theme.DockSlant;
            heightDip = box.Across + Theme.HotZone + CardReserve;
        }
        root.Width = widthDip;
        root.Height = heightDip;
        var width = (int)Math.Round(widthDip * screen.Scale);
        var height = (int)Math.Round(heightDip * screen.Scale);
        var work = screen.Work;
        var bounds = screen.Bounds;
        var x = edge switch
        {
            DockEdge.Left => work.Left,
            DockEdge.Right => work.Right - width,
            _ => bounds.Left + (bounds.Width - width) / 2
        };
        var y = edge switch
        {
            DockEdge.Top => work.Top > bounds.Top ? work.Top : bounds.Top,
            DockEdge.Bottom => work.Bottom - height,
            _ => work.Top + (work.Height - height) / 2
        };
        Native.Place(hwnd, x, y, width, height, show: mode != PanelMode.Hidden);
        PlaceCapsule();
        placed = true;
    }

    /// <summary>Pin the capsule to its edge inside the window: centred along it, flush against it.</summary>
    private void PlaceCapsule()
    {
        if (double.IsNaN(root.Width) || double.IsNaN(root.Height)) return;
        var w = capsule.ActualWidth > 0 ? capsule.ActualWidth : capsule.Width;
        var h = capsule.ActualHeight > 0 ? capsule.ActualHeight : capsule.Height;
        switch (edge)
        {
            case DockEdge.Top: Canvas.SetLeft(capsule, (root.Width - w) / 2); Canvas.SetTop(capsule, 0); break;
            case DockEdge.Bottom: Canvas.SetLeft(capsule, (root.Width - w) / 2); Canvas.SetTop(capsule, root.Height - h); break;
            case DockEdge.Left: Canvas.SetLeft(capsule, 0); Canvas.SetTop(capsule, (root.Height - h) / 2); break;
            default: Canvas.SetLeft(capsule, root.Width - w); Canvas.SetTop(capsule, (root.Height - h) / 2); break;
        }
    }

    private void ShapeDock(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0 || double.IsNaN(size.Width) || double.IsNaN(size.Height)) return;
        var along = Vertical ? size.Height : size.Width;
        var across = Vertical ? size.Width : size.Height;
        var radius = expanded ? Theme.ExpandedRadius : Theme.CompactRadius;
        dockFill.Data = DockShape.Fill(along, across, Theme.DockSlant, radius, edge);
        dockEdge.Data = DockShape.Edge(along, across, Theme.DockSlant, radius, edge);
    }

    private void PollHover()
    {
        if (!IsVisible || screen is null) return;
        var cursor = Native.Cursor();
        var inside = Contains(capsule, cursor, Theme.HotZone) || (card is not null && Contains(card, cursor, 0));
        if (inside != hovering)
        {
            hovering = inside;
            Log.Ui.Debug($"hover {(hovering ? "on" : "off")}");
            HoverChanged?.Invoke(hovering);
            Reconcile();
        }
        hoverTimer.Interval = TimeSpan.FromMilliseconds(expanded ? 120 : 300);
        if (expanded && hovering) UpdateCard(cursor);
        else if (card is not null) HideCard();
    }

    /// <summary>Whether the cursor is over the element, with a strip of extra tolerance on the side facing the screen's interior.</summary>
    private bool Contains(FrameworkElement element, Native.POINT cursor, double extraInterior)
    {
        if (element.ActualWidth <= 0 || !element.IsVisible) return false;
        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        var extra = extraInterior * (screen?.Scale ?? 1);
        switch (edge)
        {
            case DockEdge.Top: bottomRight.Y += extra; break;
            case DockEdge.Bottom: topLeft.Y -= extra; break;
            case DockEdge.Left: bottomRight.X += extra; break;
            default: topLeft.X -= extra; break;
        }
        return cursor.X >= topLeft.X && cursor.X < bottomRight.X && cursor.Y >= topLeft.Y && cursor.Y < bottomRight.Y;
    }

    private void Reconcile()
    {
        var want = mode switch
        {
            PanelMode.AlwaysExpanded => true,
            PanelMode.Hidden => false,
            _ => hovering || pinned
        };
        if (want == expanded) return;
        expanded = want;
        Resize(animate: true);
        Crossfade();
        if (expanded) ExpandedShown?.Invoke();
        else HideCard();
    }

    private void Resize(bool animate)
    {
        var box = Box();
        content.SetShape(Vertical, box.CellsPerColumn);
        var along = (expanded ? box.Along : PanelContent.CompactLength(Count)) + 2 * Theme.DockSlant;
        var across = expanded ? box.Across : Theme.CompactHeight;
        var width = Vertical ? across : along;
        var height = Vertical ? along : across;
        var duration = animate ? Theme.DurationOf(Theme.Expand) : new Duration(TimeSpan.Zero);
        if (duration.TimeSpan == TimeSpan.Zero)
        {
            capsule.BeginAnimation(WidthProperty, null);
            capsule.BeginAnimation(HeightProperty, null);
            capsule.Width = width;
            capsule.Height = height;
            return;
        }
        capsule.BeginAnimation(WidthProperty, new DoubleAnimation(width, duration) { EasingFunction = Theme.Expand });
        capsule.BeginAnimation(HeightProperty, new DoubleAnimation(height, duration) { EasingFunction = Theme.Expand });
    }

    private void Crossfade()
    {
        var incoming = expanded ? (UIElement)content.Expanded : content.CompactRow;
        var outgoing = expanded ? (UIElement)content.CompactRow : content.Expanded;
        incoming.Visibility = Visibility.Visible;
        var duration = Theme.ReduceMotion ? new Duration(TimeSpan.Zero) : Theme.Crossfade;
        var fadeOut = new DoubleAnimation(0, duration);
        fadeOut.Completed += (_, _) => { if (outgoing.Opacity < 0.01) outgoing.Visibility = Visibility.Collapsed; };
        outgoing.BeginAnimation(OpacityProperty, fadeOut);
        incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration) { BeginTime = TimeSpan.FromSeconds(Theme.ReduceMotion ? 0 : 0.08) });
    }

    private void UpdateCard(Native.POINT cursor)
    {
        string? hit = null;
        foreach (var (id, element) in content.CellElements)
        {
            if (Contains(element, cursor, 0)) { hit = id; break; }
        }
        if (hit is null)
        {
            if (card is not null && Contains(card, cursor, 0)) return;
            HideCard();
            return;
        }
        if (hit != cardFor) ShowCard(hit);
    }

    /// <summary>The detail card beside the hovered cell, on the interior side of the dock: below a top dock, above a bottom one, beside a column.</summary>
    private void ShowCard(string id)
    {
        var tile = model.Tiles.FirstOrDefault(t => t.Id == id);
        var cell = content.CellElements.FirstOrDefault(c => c.Id == id).Element;
        if (tile is null || cell is null) { HideCard(); return; }
        var fresh = HoverCard.Build(tile, Now());
        fresh.IsHitTestVisible = true;
        fresh.Measure(new Size(Theme.CardWidth, double.PositiveInfinity));
        var cardHeight = fresh.DesiredSize.Height;
        var cellOrigin = cell.TranslatePoint(new Point(0, 0), root);
        var capsuleLeft = Canvas.GetLeft(capsule);
        var capsuleTop = Canvas.GetTop(capsule);
        double left, top;
        if (Vertical)
        {
            top = Math.Clamp(cellOrigin.Y + Theme.CellHeight / 2 - cardHeight / 2, 0, Math.Max(0, root.Height - cardHeight));
            left = edge == DockEdge.Left ? capsuleLeft + capsule.ActualWidth + CardGap : capsuleLeft - CardGap - Theme.CardWidth;
        }
        else
        {
            left = Math.Clamp(cellOrigin.X + Theme.CellWidth / 2 - Theme.CardWidth / 2, 0, root.Width - Theme.CardWidth);
            top = edge == DockEdge.Top ? capsuleTop + capsule.ActualHeight + CardGap : capsuleTop - CardGap - cardHeight;
        }
        if (card is not null) root.Children.Remove(card);
        card = fresh;
        cardFor = id;
        Canvas.SetLeft(card, left);
        Canvas.SetTop(card, top);
        root.Children.Add(card);
        if (!Theme.ReduceMotion)
        {
            card.Opacity = 0;
            var (dx, dy) = edge switch { DockEdge.Top => (0.0, -6.0), DockEdge.Bottom => (0.0, 6.0), DockEdge.Left => (-6.0, 0.0), _ => (6.0, 0.0) };
            var slide = new TranslateTransform(dx, dy);
            card.RenderTransform = slide;
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Theme.Crossfade));
            slide.BeginAnimation(dx != 0 ? TranslateTransform.XProperty : TranslateTransform.YProperty, new DoubleAnimation(0, Theme.DurationOf(Theme.Glide)) { EasingFunction = Theme.Glide });
        }
    }

    private void HideCard()
    {
        if (card is null) return;
        root.Children.Remove(card);
        card = null;
        cardFor = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        hoverTimer.Stop();
        pinTimer.Stop();
        base.OnClosed(e);
    }
}
