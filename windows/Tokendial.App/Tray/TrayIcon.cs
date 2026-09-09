using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using Tokendial.App.Interop;
using Tokendial.Core.I18n;
using Tokendial.Core.Model;

namespace Tokendial.App.Tray;

/// <summary>The notification-area icon: a small dial coloured by the worst band, and the menu that reaches settings and quit.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon icon = new() { Visible = false, Text = "Tokendial" };
    private readonly Forms.ToolStripMenuItem showItem = new();
    private readonly Forms.ToolStripMenuItem refreshItem = new();
    private readonly Forms.ToolStripMenuItem settingsItem = new();
    private readonly Forms.ToolStripMenuItem testItem = new();
    private readonly Forms.ToolStripMenuItem quitItem = new();
    private readonly Forms.ToolStripMenuItem updateItem = new() { Visible = false };
    private readonly Forms.ToolStripSeparator updateRule = new() { Visible = false };
    private string? readyVersion;
    private Drawing.Icon? current;

    public TrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(updateItem);
        menu.Items.Add(updateRule);
        menu.Items.Add(showItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(testItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(quitItem);
        icon.ContextMenuStrip = menu;
        showItem.Click += (_, _) => ShowRequested?.Invoke();
        refreshItem.Click += (_, _) => RefreshRequested?.Invoke();
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();
        testItem.Click += (_, _) => TestAlertRequested?.Invoke();
        quitItem.Click += (_, _) => QuitRequested?.Invoke();
        updateItem.Click += (_, _) => UpdateRequested?.Invoke();
        Relocalize();
        icon.DoubleClick += (_, _) => SettingsRequested?.Invoke();
        icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowRequested?.Invoke(); };
        Update(null, "Tokendial");
        icon.Visible = true;
    }

    public event Action? ShowRequested;
    public event Action? RefreshRequested;
    public event Action? SettingsRequested;
    public event Action? TestAlertRequested;
    public event Action? QuitRequested;
    public event Action? UpdateRequested;

    public void Relocalize()
    {
        showItem.Text = Strings.T("tray.show");
        refreshItem.Text = Strings.T("tray.refresh");
        settingsItem.Text = Strings.T("tray.settings");
        testItem.Text = Strings.T("tray.testAlert");
        quitItem.Text = Strings.T("tray.quit");
        if (readyVersion is not null) updateItem.Text = Strings.T("tray.restartToUpdate", ("version", readyVersion));
        icon.ContextMenuStrip!.RightToLeft = Strings.RightToLeft ? Forms.RightToLeft.Yes : Forms.RightToLeft.No;
    }

    /// <summary>Shows the restart item once a newer version is downloaded; null hides it again.</summary>
    public void OfferUpdate(string? version)
    {
        readyVersion = version;
        updateItem.Visible = updateRule.Visible = version is not null;
        if (version is not null) updateItem.Text = Strings.T("tray.restartToUpdate", ("version", version));
    }

    public void Update(double? worstFraction, string tooltip)
    {
        icon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        var next = Render(worstFraction);
        icon.Icon = next;
        current?.Dispose();
        current = next;
    }

    private static Drawing.Icon Render(double? fraction)
    {
        const int size = 32;
        using var bitmap = new Drawing.Bitmap(size, size);
        using var g = Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Drawing.Color.Transparent);
        var rect = new Drawing.RectangleF(4, 4, size - 8, size - 8);
        using var track = new Drawing.Pen(Drawing.Color.FromArgb(0x5C, 0x5F, 0x68), 4.5f) { StartCap = Drawing.Drawing2D.LineCap.Round, EndCap = Drawing.Drawing2D.LineCap.Round };
        g.DrawArc(track, rect, 150, 240);
        if (fraction is double f && f > 0.01)
        {
            var band = Bands.Of(f);
            var colour = band switch { Band.Ample => Drawing.Color.FromArgb(0x34, 0xD3, 0x99), Band.Watch => Drawing.Color.FromArgb(0xFB, 0xBF, 0x24), _ => Drawing.Color.FromArgb(0xFB, 0x71, 0x85) };
            using var pen = new Drawing.Pen(colour, 4.5f) { StartCap = Drawing.Drawing2D.LineCap.Round, EndCap = Drawing.Drawing2D.LineCap.Round };
            g.DrawArc(pen, rect, 150, (float)(240 * Math.Clamp(f, 0, 1)));
        }
        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)temp.Clone();
        }
        finally { Native.DestroyIcon(handle); }
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        current?.Dispose();
    }
}
