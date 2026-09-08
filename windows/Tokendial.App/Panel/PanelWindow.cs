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
/// The capsule at the top edge. A borderless, per-pixel-transparent, always-on-top
/// window that never takes focus; transparent pixels let clicks through, so hover
/// is found by polling the cursor against the capsule's bounds rather than from
/// mouse events the window would never receive.
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
            Width = PanelContent.CompactWidth(0) + 2 * Theme.DockSlant,
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
            Canvas.SetLeft(capsule, (root.Width - capsule.ActualWidth) / 2);
            var radius = expanded ? Theme.ExpandedRadius : Theme.CompactRadius;
            dockFill.Data = DockShape.Fill(e.NewSize.Width, e.NewSize.Height, Theme.DockSlant, radius);
            dockEdge.Data = DockShape.Edge(e.NewSize.Width, e.NewSize.Height, Theme.DockSlant, radius);
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

    public void Update(PanelModel next)
    {
        var countChanged = next.Tiles.Count != model.Tiles.Count;
        model = next;
        content.Apply(model, Now(), animate: placed && !countChanged);
        if (countChanged || !placed) Resize(animate: placed);
        if (card is not null && cardFor is not null) ShowCard(cardFor);
    }

    /// <summary>The monitor, its scale, or the work area changed: recompute the window's physical rectangle.</summary>
    public void Reposition()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        screen = Native.PrimaryScreen();
        var widthDip = PanelContent.ExpandedWidth(Math.Max(model.Tiles.Count, 1)) + 2 * Theme.ExpandedPadding + 2 * Theme.DockSlant;
        var heightDip = Theme.ExpandedHeight + Theme.HotZone + CardReserve;
        root.Width = widthDip;
        root.Height = heightDip;
        var width = (int)Math.Round(widthDip * screen.Scale);
        var height = (int)Math.Round(heightDip * screen.Scale);
        var x = screen.Bounds.Left + (screen.Bounds.Width - width) / 2;
        var y = screen.Work.Top > screen.Bounds.Top ? screen.Work.Top : screen.Bounds.Top;
        Native.Place(hwnd, x, y, width, height, show: mode != PanelMode.Hidden);
        Canvas.SetLeft(capsule, (root.Width - capsule.ActualWidth) / 2);
        placed = true;
    }

    private void PollHover()
    {
        if (!IsVisible || screen is null) return;
        var cursor = Native.Cursor();
        var inside = Contains(capsule, cursor, extraBelow: Theme.HotZone) || (card is not null && Contains(card, cursor, 0));
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

    private bool Contains(FrameworkElement element, Native.POINT cursor, double extraBelow)
    {
        if (element.ActualWidth <= 0 || !element.IsVisible) return false;
        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight + extraBelow));
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
        var n = Math.Max(model.Tiles.Count, 0);
        var width = (expanded ? PanelContent.ExpandedWidth(n) : PanelContent.CompactWidth(n)) + 2 * Theme.DockSlant;
        var height = expanded ? Theme.ExpandedHeight : Theme.CompactHeight;
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

    private void ShowCard(string id)
    {
        var tile = model.Tiles.FirstOrDefault(t => t.Id == id);
        var cell = content.CellElements.FirstOrDefault(c => c.Id == id).Element;
        if (tile is null || cell is null) { HideCard(); return; }
        var fresh = HoverCard.Build(tile, Now());
        fresh.IsHitTestVisible = true;
        var cellLeft = cell.TranslatePoint(new Point(0, 0), root).X;
        var left = Math.Clamp(cellLeft + Theme.CellWidth / 2 - Theme.CardWidth / 2, 0, root.Width - Theme.CardWidth);
        var top = Theme.ExpandedHeight + CardGap;
        if (card is not null) root.Children.Remove(card);
        card = fresh;
        cardFor = id;
        Canvas.SetLeft(card, left);
        Canvas.SetTop(card, top);
        root.Children.Add(card);
        if (!Theme.ReduceMotion)
        {
            card.Opacity = 0;
            card.RenderTransform = new TranslateTransform(0, -6);
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Theme.Crossfade));
            card.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Theme.DurationOf(Theme.Glide)) { EasingFunction = Theme.Glide });
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
