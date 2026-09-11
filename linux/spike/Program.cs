using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Spike;

/// <summary>
/// Throwaway. It answers one question - can an Avalonia window on Cinnamon behave the way Tokendial's dock
/// behaves on Windows - and is deleted once it has. Everything it prints goes to stdout so the answers
/// survive in a log rather than in someone's memory of watching it.
/// </summary>
public static class Program
{
    public static bool AsDock = true;

    public static int Main(string[] args)
    {
        AsDock = !args.Contains("--utility");
        return AppBuilder.Configure<SpikeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}

public sealed class SpikeApp : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new SpikeWindow();
        Tray.Arm(this);
        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>
/// The tray is its own question, and a pointed one: Avalonia #16650 is an open bug titled "TrayIcon not
/// visible on Linux Mint 22 Cinnamon", and Tokendial's icon is drawn at runtime from the usage arc rather
/// than loaded from a theme, which is the part most likely to be unsupported.
/// </summary>
public static class Tray
{
    public static void Arm(Application app)
    {
        try
        {
            var icon = new TrayIcon
            {
                Icon = Draw(0.62),
                ToolTipText = "Tokendial spike",
                IsVisible = true,
                Menu = new NativeMenu { Items = { new NativeMenuItem("Quit") } }
            };
            TrayIcon.SetIcons(app, [icon]);
            Console.WriteLine("tray           : TrayIcon constructed with a runtime-drawn icon");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"tray           : FAILED to construct - {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>The usage arc, rendered to a bitmap the way the Windows tray does it with GDI+.</summary>
    private static WindowIcon Draw(double fraction)
    {
        var size = new PixelSize(32, 32);
        using var target = new RenderTargetBitmap(size, new Vector(96, 96));
        using (var ctx = target.CreateDrawingContext())
        {
            var track = new Pen(new SolidColorBrush(Color.FromRgb(38, 47, 64)), 5, lineCap: PenLineCap.Round);
            var dial = new Pen(new SolidColorBrush(Color.FromRgb(52, 211, 153)), 5, lineCap: PenLineCap.Round);
            ctx.DrawGeometry(null, track, Arc(16, 16, 11, 150, 240));
            ctx.DrawGeometry(null, dial, Arc(16, 16, 11, 150, 240 * fraction));
        }
        using var stream = new MemoryStream();
        target.Save(stream);
        stream.Position = 0;
        return new WindowIcon(stream);
    }

    private static StreamGeometry Arc(double cx, double cy, double r, double start, double sweep)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        var a0 = start * Math.PI / 180;
        var a1 = (start + sweep) * Math.PI / 180;
        g.BeginFigure(new Point(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0)), false);
        g.ArcTo(new Point(cx + r * Math.Cos(a1), cy + r * Math.Sin(a1)), new Size(r, r), 0,
            sweep > 180, SweepDirection.Clockwise);
        g.EndFigure(false);
        return geometry;
    }
}

public sealed class SpikeWindow : Window
{
    // The dock's real numbers, from docs/design/tokens.md, so the spike is the shape that has to work
    // rather than a rectangle that proves nothing.
    private const double CapsuleWidth = 360;
    private const double CapsuleHeight = 64;
    private const double HotZone = 24;
    private const double Slant = 14;

    private readonly TextBlock readout = new()
    {
        Foreground = Brushes.White,
        FontSize = 13,
        Margin = new Thickness(16, 0, 0, 0),
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
    };

    private readonly Border capsule;
    private DispatcherTimer? poll;
    private int hovers;

    public SpikeWindow()
    {
        Title = "Tokendial dock spike";
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Width = CapsuleWidth;
        Height = CapsuleHeight + HotZone;

        // Half-transparent on purpose: if per-pixel alpha is not working, this reads as a solid slab and
        // the screenshot says so immediately.
        capsule = new Border
        {
            Width = CapsuleWidth - 2 * Slant,
            Height = CapsuleHeight,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            CornerRadius = new CornerRadius(0, 0, 18, 18),
            Background = new SolidColorBrush(Color.FromArgb(128, 11, 14, 20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 52, 211, 153)),
            BorderThickness = new Thickness(1),
            Child = readout
        };

        Content = new Panel { Children = { capsule } };
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var handle = TryGetPlatformHandle();
        Console.WriteLine($"platform handle: {handle?.HandleDescriptor ?? "none"} = 0x{handle?.Handle:X}");
        if (handle is null || !X11.Open())
        {
            Console.WriteLine("FAIL: no X11 display; is this a Wayland-only session?");
            return;
        }

        Console.WriteLine($"session type   : {Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "?"}" +
                          $"   WAYLAND_DISPLAY={Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "unset"}");
        Console.WriteLine($"compositing    : {(X11.Composited() ? "yes (_NET_WM_CM_S0 owned)" : "NO - per-pixel alpha impossible")}");

        var screen = Screens.Primary;
        Console.WriteLine($"avalonia screen: bounds={screen?.Bounds}  workarea={screen?.WorkingArea}  scaling={screen?.Scaling}");
        Console.WriteLine($"x11 _NET_WORKAREA: {X11.WorkArea()?.ToString() ?? "absent"}");

        var window = handle.Handle;
        X11.MakeDock(window, Program.AsDock);
        X11.RefuseFocus(window);
        Console.WriteLine($"window type    : {(Program.AsDock ? "_NET_WM_WINDOW_TYPE_DOCK" : "_NET_WM_WINDOW_TYPE_UTILITY + ABOVE")}");

        Place(screen);
        ApplyInputShape(window, screen?.Scaling ?? 1.0);
        StartPolling();
    }

    /// <summary>Top edge, centred, hanging from the work area exactly as PanelWindow.Reposition does.</summary>
    private void Place(Screen? screen)
    {
        var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var scale = screen?.Scaling ?? 1.0;
        var widthPx = (int)Math.Round(Width * scale);
        var x = area.X + (area.Width - widthPx) / 2;
        var y = area.Y;
        Position = new PixelPoint(x, y);
        Console.WriteLine($"placed at      : {Position}  (asked for {x},{y}; window {widthPx}px wide at scale {scale})");
    }

    /// <summary>
    /// Only the capsule takes clicks. The hot-zone strip below it stays outside the input region so it can be
    /// polled for hover while a click there lands on whatever is behind - which is the behaviour WPF gets for
    /// free from a layered window and X11 gives no other way to get.
    /// </summary>
    private void ApplyInputShape(nint window, double scale)
    {
        var rect = new X11.XRectangle
        {
            X = (short)Math.Round(Slant * scale),
            Y = 0,
            Width = (ushort)Math.Round((CapsuleWidth - 2 * Slant) * scale),
            Height = (ushort)Math.Round(CapsuleHeight * scale)
        };
        X11.InputShape(window, rect);
        Console.WriteLine($"input shape    : x={rect.X} y={rect.Y} w={rect.Width} h={rect.Height} (everything else clicks through)");
    }

    /// <summary>
    /// Polled, not evented, for the same reason the Windows dock polls: a transparent window whose input
    /// region excludes the hot zone never receives enter or leave for the part that matters most.
    /// </summary>
    private void StartPolling()
    {
        var reported = false;
        poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        poll.Tick += (_, _) =>
        {
            var (x, y, sameScreen) = X11.Pointer();
            if (!sameScreen && !reported)
            {
                Console.WriteLine("WARNING: XQueryPointer reports same_screen=false - almost certainly XWayland. Hover cannot work.");
                reported = true;
            }

            var p = Position;
            var scale = Screens.Primary?.Scaling ?? 1.0;
            var inside = x >= p.X && x <= p.X + Width * scale && y >= p.Y && y <= p.Y + (Height + HotZone) * scale;
            if (inside) hovers++;
            readout.Text = $"cursor {x},{y}  {(inside ? "HOVER" : "outside")}  ticks:{hovers}  same_screen:{sameScreen}";
        };
        poll.Start();
        Console.WriteLine("polling        : XQueryPointer every 300 ms; the capsule shows the live reading");
        Console.WriteLine("--- window is up; capture a screenshot now ---");
    }
}
