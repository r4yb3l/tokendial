using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Tokendial.App.Interop;
using Tokendial.Core.Sessions;

namespace Tokendial.App.Panel;

/// <summary>
/// The floating face: a small window the user drags anywhere and that never takes focus from what they
/// are typing in, with a speech bubble beside it while an agent waits. Positions are physical pixels on the
/// virtual screen, so a face left on a second monitor at another scale comes back where it was.
/// </summary>
public sealed class MascotWindow : Window
{
    private const double Diameter = 72;
    private const double Inset = 10;
    private const double BubbleGap = 2;

    private readonly Mascot mascot = new(Diameter, Diameter * 0.085);
    private readonly SpeechBubble bubble = new();
    private Native.POINT grabCursor;
    private Native.RECT grabRect;
    private bool dragging;
    private bool moved;

    public MascotWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Title = "Tokendial";
        SizeToContent = SizeToContent.Manual;
        Width = Height = Diameter + 2 * Inset;
        UseLayoutRounding = false;
        Cursor = Cursors.SizeAll;

        mascot.HorizontalAlignment = HorizontalAlignment.Center;
        mascot.VerticalAlignment = VerticalAlignment.Center;
        Content = new Border { Background = Brushes.Transparent, Padding = new Thickness(Inset), Child = mascot };

        MouseLeftButtonDown += OnGrab;
        MouseMove += OnDrag;
        MouseLeftButtonUp += OnRelease;
        MouseRightButtonUp += (_, e) => { e.Handled = true; SettingsRequested?.Invoke(); };
        SourceInitialized += (_, _) => Native.MakeUnobtrusive(Handle);
        bubble.SizeChanged += (_, _) => PlaceBubble();
        bubble.Changed += PlaceBubble;
        Closed += (_, _) => bubble.Close();
    }

    /// <summary>Where the face ended up after a drag, in physical pixels. A click that never moved it reports nothing.</summary>
    public event Action<double, double>? Moved;
    public event Action? SettingsRequested;

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    public void Apply(PanelModel model, bool animate)
    {
        mascot.Show(model.Face, animate);
        bubble.Say(model.Sessions.Where(s => s.State == SessionState.Waiting)
            .Select(s => (s, model.Tiles.FirstOrDefault(t => t.Activity?.Sessions.Contains(s) == true)?.Name ?? s.Name))
            .ToList());
        PlaceBubble();
    }

    /// <summary>Puts the face where it was last left, or near the top-left of the main work area on a first run.</summary>
    public void PlaceAt(double? x, double? y)
    {
        var saved = x is double px && y is double py ? new Native.POINT { X = (int)px, Y = (int)py } : (Native.POINT?)null;
        var screen = saved is Native.POINT point ? Native.ScreenAt(point) : Native.PrimaryScreen();
        var work = screen.Work;
        var size = (int)(Width * screen.Scale);
        var left = saved?.X ?? work.Left + (int)(48 * screen.Scale);
        var top = saved?.Y ?? work.Top + (int)(48 * screen.Scale);
        left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - size));
        top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - size));
        Native.Place(Handle, left, top, size, size, show: true);
        PlaceBubble();
    }

    /// <summary>Beside the face on whichever side has room, its top level with the face's eyes.</summary>
    private void PlaceBubble()
    {
        if (!IsVisible || !bubble.HasSomethingToSay) { bubble.Hide(); return; }
        if (!bubble.IsVisible) bubble.Show();
        Native.GetWindowRect(Handle, out var face);
        var screen = Native.ScreenAt(new Native.POINT { X = face.Left + face.Width / 2, Y = face.Top + face.Height / 2 });
        var width = (int)(bubble.ActualWidth * screen.Scale);
        var height = (int)(bubble.ActualHeight * screen.Scale);
        var gap = (int)((BubbleGap - Inset) * screen.Scale);
        var right = face.Right + gap + width <= screen.Work.Right;
        bubble.FaceOnLeft = right;
        var left = right ? face.Right + gap : face.Left - gap - width;
        var top = Math.Clamp(face.Top, screen.Work.Top, Math.Max(screen.Work.Top, screen.Work.Bottom - height));
        Native.Place(new WindowInteropHelper(bubble).Handle, left, top, width, height, show: true);
    }

    private void OnGrab(object sender, MouseButtonEventArgs e)
    {
        Native.GetWindowRect(Handle, out grabRect);
        grabCursor = Native.Cursor();
        dragging = true;
        moved = false;
        CaptureMouse();
    }

    /// <summary>
    /// Moved through SetWindowPos rather than WPF's DragMove, which calls into window activation and would
    /// hand the face the focus the dock exists not to steal. The window keeps its offset from the cursor in
    /// physical pixels and takes the scale of the monitor it is entering.
    /// </summary>
    private void OnDrag(object sender, MouseEventArgs e)
    {
        if (!dragging) return;
        var cursor = Native.Cursor();
        var dx = cursor.X - grabCursor.X;
        var dy = cursor.Y - grabCursor.Y;
        if (!moved && Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance) return;
        moved = true;
        var size = (int)(Width * Native.ScreenAt(cursor).Scale);
        Native.Place(Handle, grabRect.Left + dx, grabRect.Top + dy, size, size, show: true);
        PlaceBubble();
    }

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        ReleaseMouseCapture();
        if (!moved) return;
        Native.GetWindowRect(Handle, out var rect);
        Moved?.Invoke(rect.Left, rect.Top);
    }
}
