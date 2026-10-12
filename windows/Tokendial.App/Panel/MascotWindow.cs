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
/// are typing in. It carries the worst band across every provider and the strongest state across every
/// session, which is the dock's reading without the dock.
/// </summary>
public sealed class MascotWindow : Window
{
    private const double Diameter = 72;
    private const double Inset = 10;

    private readonly Mascot mascot = new(Diameter, Diameter * 0.115);
    private Point grabOffset;
    private bool dragging;

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
        SourceInitialized += (_, _) => Native.MakeUnobtrusive(new WindowInteropHelper(this).Handle);
    }

    /// <summary>Where the face ended up, for the settings file. Reported after a drag, not during one.</summary>
    public event Action<double, double>? Moved;
    public event Action? SettingsRequested;

    public void Apply(PanelModel model, bool animate)
    {
        mascot.Band = model.WorstBand;
        if (animate) mascot.AnimateTo(model.WorstFraction);
        else { mascot.BeginAnimation(Mascot.FractionProperty, null); mascot.Fraction = model.WorstFraction; }
        mascot.Show(model.Mood);
    }

    /// <summary>Puts the face where it was last left, or near the top-left of the work area on a first run.</summary>
    public void PlaceAt(double? x, double? y, Native.Screen screen)
    {
        var scale = screen.Scale;
        var work = screen.Work;
        var width = Width * scale;
        var height = Height * scale;
        var left = x is double px ? px * scale : work.Left + 48 * scale;
        var top = y is double py ? py * scale : work.Top + 48 * scale;
        left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - width));
        top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - height));
        Native.Place(new WindowInteropHelper(this).Handle, (int)left, (int)top, (int)width, (int)height, show: true);
    }

    private void OnGrab(object sender, MouseButtonEventArgs e)
    {
        grabOffset = e.GetPosition(this);
        dragging = true;
        CaptureMouse();
    }

    /// <summary>
    /// Moved through SetWindowPos rather than WPF's DragMove, which calls into window activation and would
    /// hand the face the focus the dock exists not to steal.
    /// </summary>
    private void OnDrag(object sender, MouseEventArgs e)
    {
        if (!dragging) return;
        var screen = Native.PrimaryScreen();
        var cursor = Native.Cursor();
        var left = cursor.X - grabOffset.X * screen.Scale;
        var top = cursor.Y - grabOffset.Y * screen.Scale;
        Native.Place(new WindowInteropHelper(this).Handle, (int)left, (int)top, (int)(Width * screen.Scale), (int)(Height * screen.Scale), show: true);
    }

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        ReleaseMouseCapture();
        var screen = Native.PrimaryScreen();
        var cursor = Native.Cursor();
        Moved?.Invoke((cursor.X - grabOffset.X * screen.Scale) / screen.Scale, (cursor.Y - grabOffset.Y * screen.Scale) / screen.Scale);
    }
}
