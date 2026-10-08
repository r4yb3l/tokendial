using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Tokendial.Core.I18n;
using Tokendial.Core.Model;
using Tokendial.Linux.Panel;

namespace Tokendial.Linux.Tray;

/// <summary>
/// The notification-area icon: a small dial coloured by the worst band, and the menu that reaches settings
/// and quit. The Avalonia twin of windows/Tokendial.App/Tray/TrayIcon.cs, arc for arc - the icon is drawn at
/// runtime rather than picked from a theme, because it is a reading.
/// </summary>
/// <remarks>
/// On Linux the tray is StatusNotifierItem over D-Bus, served here by Mint's xapp-sn-watcher. That watcher
/// is known to start late enough to miss an application launched with the session, so the icon is created
/// only once the watcher owns its name and again whenever it reappears - which also survives a Cinnamon
/// restart, where the applet dies and would otherwise leave an icon that is registered and invisible.
/// </remarks>
public sealed class TrayIcon : IDisposable
{
    private readonly Avalonia.Controls.TrayIcon icon = new() { IsVisible = false, ToolTipText = "Tokendial" };
    private readonly NativeMenuItem showItem = new();
    private readonly NativeMenuItem refreshItem = new();
    private readonly NativeMenuItem testItem = new();
    private readonly NativeMenuItem settingsItem = new();
    private readonly NativeMenuItem quitItem = new();
    private readonly Func<TrayGlyph, WindowIcon> render;
    private TrayGlyph? shown;

    public TrayIcon(Application app) : this(app, Render) { }

    internal TrayIcon(Application app, Func<TrayGlyph, WindowIcon> render)
    {
        this.render = render;
        var menu = new NativeMenu();
        menu.Items.Add(showItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(testItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quitItem);
        icon.Menu = menu;

        showItem.Click += (_, _) => ShowRequested?.Invoke();
        refreshItem.Click += (_, _) => RefreshRequested?.Invoke();
        testItem.Click += (_, _) => TestAlertRequested?.Invoke();
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();
        quitItem.Click += (_, _) => QuitRequested?.Invoke();
        icon.Clicked += (_, _) => ShowRequested?.Invoke();

        Relocalize();
        Update(null, "Tokendial");
        Avalonia.Controls.TrayIcon.SetIcons(app, [icon]);
        icon.IsVisible = true;
    }

    public event Action? ShowRequested;
    public event Action? RefreshRequested;
    public event Action? TestAlertRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    public void Relocalize()
    {
        showItem.Header = Strings.T("tray.show");
        refreshItem.Header = Strings.T("tray.refresh");
        testItem.Header = Strings.T("tray.testAlert");
        settingsItem.Header = Strings.T("tray.settings");
        quitItem.Header = Strings.T("tray.quit");
    }

    public void Update(double? worstFraction, string tooltip)
    {
        icon.ToolTipText = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        var glyph = TrayGlyph.Of(worstFraction);
        if (glyph == shown) return;
        icon.Icon = render(glyph);
        shown = glyph;
    }

    /// <summary>
    /// The same 240° arc the dock draws, at the size a panel wants it. Encoded by <see cref="TrayPng"/>, not by
    /// <c>RenderTargetBitmap.Save</c>: Skia's PNG encoder is what crashed the process.
    /// </summary>
    private static unsafe WindowIcon Render(TrayGlyph glyph)
    {
        const int size = 32;
        using var target = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var ctx = target.CreateDrawingContext())
        {
            var centre = new Point(size / 2.0, size / 2.0);
            const double radius = (size - 8) / 2.0;
            ctx.DrawGeometry(null, new Pen(Theme.TextDisabled, 4.5, lineCap: PenLineCap.Round),
                Dial.Arc(centre, radius, Dial.StartAngle, Dial.Sweep));
            if (glyph.Band is Band band)
                ctx.DrawGeometry(null, new Pen(Theme.Of(band), 4.5, lineCap: PenLineCap.Round),
                    Dial.Arc(centre, radius, Dial.StartAngle, Dial.Sweep * glyph.Percent / 100.0));
        }
        var pixels = new byte[size * size * 4];
        fixed (byte* address = pixels)
            target.CopyPixels(new LockedFramebuffer((nint)address, new PixelSize(size, size), size * 4,
                new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul, null));
        using var png = new MemoryStream(TrayPng.Encode(size, size, pixels));
        return new WindowIcon(png);
    }

    public void Dispose() => icon.IsVisible = false;
}
