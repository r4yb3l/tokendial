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
    private readonly NativeMenuItem settingsItem = new();
    private readonly NativeMenuItem quitItem = new();

    public TrayIcon(Application app)
    {
        var menu = new NativeMenu();
        menu.Items.Add(showItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quitItem);
        icon.Menu = menu;

        showItem.Click += (_, _) => ShowRequested?.Invoke();
        refreshItem.Click += (_, _) => RefreshRequested?.Invoke();
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
    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    public void Relocalize()
    {
        showItem.Header = Strings.T("tray.show");
        refreshItem.Header = Strings.T("tray.refresh");
        settingsItem.Header = Strings.T("tray.settings");
        quitItem.Header = Strings.T("tray.quit");
    }

    public void Update(double? worstFraction, string tooltip)
    {
        icon.ToolTipText = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        icon.Icon = Render(worstFraction);
    }

    /// <summary>The same 240° arc the dock draws, at the size a panel wants it.</summary>
    private static WindowIcon Render(double? fraction)
    {
        const int size = 32;
        var target = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var ctx = target.CreateDrawingContext())
        {
            var centre = new Point(size / 2.0, size / 2.0);
            const double radius = (size - 8) / 2.0;
            ctx.DrawGeometry(null, new Pen(Theme.TextDisabled, 4.5, lineCap: PenLineCap.Round),
                Dial.Arc(centre, radius, Dial.StartAngle, Dial.Sweep));
            if (fraction is double f && f > 0.01)
                ctx.DrawGeometry(null, new Pen(Theme.Of(Bands.Of(f)), 4.5, lineCap: PenLineCap.Round),
                    Dial.Arc(centre, radius, Dial.StartAngle, Dial.Sweep * Math.Clamp(f, 0, 1)));
        }
        using var stream = new MemoryStream();
        target.Save(stream);
        stream.Position = 0;
        return new WindowIcon(stream);
    }

    public void Dispose() => icon.IsVisible = false;
}
