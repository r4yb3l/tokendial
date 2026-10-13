using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Tokendial.Core.Sessions;
using Tokendial.Linux.Interop;

namespace Tokendial.Linux.Panel;

/// <summary>
/// The floating face: a small window the user drags anywhere and that never takes the keyboard, with a speech
/// bubble beside it while an agent waits. The Avalonia twin of windows/Tokendial.App/Panel/MascotWindow.cs.
/// Positions are physical pixels on the X screen, so a face left on a second monitor comes back there.
/// </summary>
/// <remarks>
/// Dressed as a dock window with no input focus, as the dock and its card are. Where the face is comes from the
/// X server rather than from <c>Position</c>, which on X11 reports where a window was asked to go rather than
/// where it went (see <see cref="X11.RootOrigin"/>).
/// </remarks>
public sealed class MascotWindow : Window
{
    private const double Diameter = 72;
    private const double Inset = 10;
    private const double BubbleGap = 2;
    private const int DragThreshold = 4;

    private readonly Mascot mascot = new(Diameter, Diameter * 0.085);
    private readonly SpeechBubble bubble = new();
    private PixelPoint grabCursor;
    private PixelPoint grabOrigin;
    private bool dragging;
    private bool moved;

    public MascotWindow()
    {
        Title = "Tokendial face";
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Width = Height = Diameter + 2 * Inset;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Cursor = new Cursor(StandardCursorType.SizeAll);

        mascot.HorizontalAlignment = HorizontalAlignment.Center;
        mascot.VerticalAlignment = VerticalAlignment.Center;
        Content = new Border { Background = Brushes.Transparent, Padding = new Thickness(Inset), Child = mascot };

        PointerPressed += OnGrab;
        PointerMoved += OnDrag;
        PointerReleased += OnRelease;
        Opened += (_, _) =>
        {
            if (Handle is nint handle)
            {
                X11.MakeDock(handle);
                X11.RefuseFocus(handle);
            }
        };
        bubble.SizeChanged += (_, _) => PlaceBubble();
        bubble.Changed += PlaceBubble;
        Closed += (_, _) => bubble.Close();
    }

    /// <summary>Where the face ended up after a drag, in physical pixels. A click that never moved it reports nothing.</summary>
    public event Action<double, double>? Moved;
    public event Action? SettingsRequested;

    private nint? Handle => TryGetPlatformHandle() is { HandleDescriptor: "XID", Handle: not 0 } handle && X11.Open() ? handle.Handle : null;

    /// <summary>Where the face really is, from the server when there is one to ask.</summary>
    private PixelPoint Origin => Handle is nint handle && X11.RootOrigin(handle) is (var x, var y, true) ? new PixelPoint(x, y) : Position;

    public void Apply(PanelModel model)
    {
        mascot.Show(model.Face);
        bubble.Say(model.Sessions.Where(s => s.State == SessionState.Waiting)
            .Select(s => (s, model.Tiles.FirstOrDefault(t => t.Activity?.Sessions.Contains(s) == true)?.Name ?? s.Name))
            .ToList());
        PlaceBubble();
    }

    /// <summary>Puts the face where it was last left, or near the top-left of the main work area on a first run.</summary>
    public void PlaceAt(double? x, double? y)
    {
        var saved = x is double px && y is double py ? new PixelPoint((int)px, (int)py) : (PixelPoint?)null;
        var screen = (saved is PixelPoint point ? Screens.ScreenFromPoint(point) : null) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
        var work = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var scale = screen?.Scaling ?? 1.0;
        var size = (int)(Width * scale);
        var left = saved?.X ?? work.X + (int)(48 * scale);
        var top = saved?.Y ?? work.Y + (int)(48 * scale);
        Position = new PixelPoint(
            Math.Clamp(left, work.X, Math.Max(work.X, work.Right - size)),
            Math.Clamp(top, work.Y, Math.Max(work.Y, work.Bottom - size)));
        PlaceBubble();
    }

    /// <summary>Beside the face on whichever side has room, its top level with the face's.</summary>
    private void PlaceBubble()
    {
        if (!IsVisible || !bubble.HasSomethingToSay)
        {
            if (bubble.IsVisible) bubble.Hide();
            return;
        }
        var origin = Origin;
        var screen = Screens.ScreenFromPoint(origin) ?? Screens.Primary;
        var work = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var scale = screen?.Scaling ?? 1.0;
        var face = new PixelRect(origin, new PixelSize((int)(Width * scale), (int)(Height * scale)));
        var width = (int)(bubble.Bounds.Width * scale);
        var height = (int)(bubble.Bounds.Height * scale);
        var gap = (int)((BubbleGap - Inset) * scale);
        var right = face.Right + gap + width <= work.Right;
        bubble.FaceOnLeft = right;
        bubble.Position = new PixelPoint(
            right ? face.Right + gap : face.X - gap - width,
            Math.Clamp(face.Y, work.Y, Math.Max(work.Y, work.Bottom - height)));
        if (!bubble.IsVisible) bubble.Show();
    }

    private void OnGrab(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        grabCursor = Pointer();
        grabOrigin = Origin;
        dragging = true;
        moved = false;
        e.Pointer.Capture(this);
    }

    /// <summary>The window keeps its offset from the cursor in physical pixels, which is what both sides of the drag are measured in.</summary>
    private void OnDrag(object? sender, PointerEventArgs e)
    {
        if (!dragging) return;
        var cursor = Pointer();
        var dx = cursor.X - grabCursor.X;
        var dy = cursor.Y - grabCursor.Y;
        if (!moved && Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold) return;
        moved = true;
        Position = new PixelPoint(grabOrigin.X + dx, grabOrigin.Y + dy);
        PlaceBubble();
    }

    private void OnRelease(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            e.Handled = true;
            SettingsRequested?.Invoke();
            return;
        }
        if (!dragging) return;
        dragging = false;
        e.Pointer.Capture(null);
        if (!moved) return;
        var origin = Origin;
        Moved?.Invoke(origin.X, origin.Y);
    }

    /// <summary>The global pointer from the server; Avalonia's own position is relative to a window that is moving under it.</summary>
    private PixelPoint Pointer()
    {
        if (X11.Open() && X11.Pointer() is (var x, var y, true)) return new PixelPoint(x, y);
        return this.PointToScreen(new Point(0, 0));
    }
}
