using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Tokendial.App.Panel;
using Tokendial.Core.Alerts;
using Tokendial.Core.Providers;
using Tokendial.Core.Settings;
using Tokendial.Core.Store;

namespace Tokendial.App.Windows;

/// <summary>Providers, panel, alerts and startup. Every change saves immediately and takes effect through the host.</summary>
public sealed class SettingsWindow
{
    private readonly Settings settings;
    private readonly UsageStore store;
    private readonly Action save;
    private readonly Window window;
    private readonly StackPanel providersList = new();

    public SettingsWindow(Settings settings, UsageStore store, Action save, Action<bool> launchAtLogin, Action testAlert, string version)
    {
        this.settings = settings;
        this.store = store;
        this.save = save;

        var page = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
        page.Children.Add(Chrome.Section("Providers",
            "Tokendial reads the sign-in each coding tool already keeps on this PC and asks that tool's usage endpoint. Nothing is written back and nothing leaves the machine except that request.",
            providersList));

        var panel = new StackPanel();
        panel.Children.Add(Chrome.Radio("panel", "Expand when the cursor reaches the top edge", settings.Panel == PanelMode.ExpandOnHover, () => SetPanel(PanelMode.ExpandOnHover), "A compact row of dials otherwise."));
        panel.Children.Add(Chrome.Radio("panel", "Always expanded", settings.Panel == PanelMode.AlwaysExpanded, () => SetPanel(PanelMode.AlwaysExpanded)));
        panel.Children.Add(Chrome.Radio("panel", "Hidden", settings.Panel == PanelMode.Hidden, () => SetPanel(PanelMode.Hidden), "Alerts and the tray icon only."));
        page.Children.Add(Chrome.Section("Panel", null, panel));

        var alerts = new StackPanel();
        alerts.Children.Add(Chrome.Check("Usage thresholds", settings.AlertThresholds, v => { settings.AlertThresholds = v; Save(); }, "Once per threshold per window."));
        alerts.Children.Add(Chrome.Row("Thresholds (%)", Chrome.Field(string.Join(", ", settings.Thresholds), text =>
        {
            var parsed = text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries).Select(t => int.TryParse(t, out var n) ? n : -1).Where(n => n is > 0 and <= 100).Distinct().OrderBy(n => n).ToList();
            if (parsed.Count > 0) { settings.Thresholds = parsed; Save(); }
        }), "Comma-separated, for the headline window of each provider."));
        alerts.Children.Add(Chrome.Check("Reset soon and available again", settings.AlertResetSoon, v => { settings.AlertResetSoon = v; Save(); }, "Before a window you have leaned on resets, and once a limit lifts."));
        alerts.Children.Add(Chrome.Row("Lead time (minutes)", Chrome.Field(settings.ResetLeadMinutes.ToString(), text => { if (int.TryParse(text, out var n) && n is >= 1 and <= 120) { settings.ResetLeadMinutes = n; Save(); } }, 80)));
        alerts.Children.Add(Chrome.Check("An agent is waiting for you", settings.AlertWaiting, v => { settings.AlertWaiting = v; Save(); }, "Repeats every five minutes while it keeps waiting."));
        alerts.Children.Add(Chrome.Row("After waiting (seconds)", Chrome.Field(settings.WaitingDebounceSeconds.ToString(), text => { if (int.TryParse(text, out var n) && n is >= 5 and <= 600) { settings.WaitingDebounceSeconds = n; Save(); } }, 80)));
        alerts.Children.Add(Chrome.Check("Limit reached", settings.AlertLimit, v => { settings.AlertLimit = v; Save(); }, "With the time it lifts."));
        var delivery = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        delivery.Children.Add(Chrome.Radio("delivery", "Tokendial banners", settings.Delivery == AlertDelivery.TokendialBanners, () => SetDelivery(AlertDelivery.TokendialBanners), "Top-right corner, in your Windows accent colour. Shown even under Do not disturb."));
        delivery.Children.Add(Chrome.Radio("delivery", "Windows notifications", settings.Delivery == AlertDelivery.WindowsToasts, () => SetDelivery(AlertDelivery.WindowsToasts), "Kept in the notification centre; silenced by Do not disturb."));
        delivery.Children.Add(Chrome.Radio("delivery", "Windows notifications, banners when Windows is silent", settings.Delivery == AlertDelivery.WindowsThenBanners, () => SetDelivery(AlertDelivery.WindowsThenBanners)));
        alerts.Children.Add(delivery);
        var test = Chrome.Button("Send a test alert", testAlert);
        test.Margin = new Thickness(0, 10, 0, 0);
        test.HorizontalAlignment = HorizontalAlignment.Left;
        alerts.Children.Add(test);
        page.Children.Add(Chrome.Section("Alerts", "Silent while the panel is expanded under your cursor. At most one alert per provider each minute.", alerts));

        var general = new StackPanel();
        general.Children.Add(Chrome.Check("Launch at login", settings.LaunchAtLogin, v => { settings.LaunchAtLogin = v; launchAtLogin(v); Save(); }));
        page.Children.Add(Chrome.Section("Startup", null, general));

        var about = new StackPanel();
        about.Children.Add(Chrome.Body($"Tokendial {version} · MIT License"));
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        links.Children.Add(Chrome.Button("Website", () => Open("https://tokendial.app")));
        links.Children.Add(Chrome.Button("Source and issues", () => Open("https://github.com/r4yb3l-qa/tokendial")));
        links.Children.Add(Chrome.Button("Open log folder", () => Open(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tokendial"))));
        about.Children.Add(links);
        page.Children.Add(Chrome.Section("About", null, about));

        window = Chrome.Frame("Tokendial settings", 560, 720, Chrome.Scroll(page));
        window.Closed += (_, _) => Closed?.Invoke();
        store.Changed += OnStoreChanged;
        RefreshProviders();
    }

    public event Action? Closed;
    public event Action<PanelMode>? PanelModeChanged;
    public event Action<AlertConfig>? AlertsChanged;

    public void Show()
    {
        if (!window.IsVisible) window.Show();
        window.Activate();
    }

    public void Close() => window.Close();

    private void SetPanel(PanelMode mode)
    {
        if (settings.Panel == mode) return;
        settings.Panel = mode;
        Save();
        PanelModeChanged?.Invoke(mode);
    }

    private void SetDelivery(AlertDelivery delivery)
    {
        if (settings.Delivery == delivery) return;
        settings.Delivery = delivery;
        save();
    }

    private void Save()
    {
        save();
        AlertsChanged?.Invoke(settings.AlertConfig);
    }

    private void OnStoreChanged() => window.Dispatcher.BeginInvoke(RefreshProviders);

    private void RefreshProviders()
    {
        providersList.Children.Clear();
        var readings = store.Readings.ToDictionary(r => r.ProviderId);
        foreach (var summary in store.Summaries)
        {
            var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var connected = summary.Connected;
            var toggle = new CheckBox { IsChecked = connected, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            toggle.Checked += (_, _) => Connect(summary.Id, true);
            toggle.Unchecked += (_, _) => Connect(summary.Id, false);
            grid.Children.Add(toggle);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Chrome.Label(summary.Name));
            var detail = Detail(summary, connected ? readings.GetValueOrDefault(summary.Id) : null);
            var detailBlock = Chrome.Body(detail);
            detailBlock.FontSize = 11;
            text.Children.Add(detailBlock);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (connected && summary.Account is null && summary.SignIn is SignInRoute.OpenApp app)
                actions.Children.Add(Chrome.Button($"Open {app.Name}", () => store.OpenSource(summary.Id)));
            if (connected && summary.Account?.ManageUrl is Uri manage)
                actions.Children.Add(Chrome.Button("Manage", () => Open(manage.ToString())));
            if (connected)
                actions.Children.Add(Chrome.Button("Refresh", () => _ = store.Poll(summary.Id)));
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);
            providersList.Children.Add(grid);
        }
    }

    private static string Detail(ProviderSummary summary, Core.Model.ProviderReading? reading)
    {
        if (!summary.Connected) return "Off";
        var account = summary.Account?.Summary;
        var status = reading?.Status switch
        {
            Core.Model.ReadingStatus.Live => reading.HasReading ? reading.HeadlineText : "Connected",
            Core.Model.ReadingStatus.NeedsSignIn => summary.SignIn.Explanation,
            Core.Model.ReadingStatus.Unsupported u => u.Why,
            Core.Model.ReadingStatus.Failed f => $"Unavailable ({f.Why})",
            Core.Model.ReadingStatus.Stale s when s.Since > DateTimeOffset.MinValue => $"Last read {Core.Model.Copy.Ago(s.Since, DateTimeOffset.UtcNow)}",
            _ => summary.Account is null ? summary.SignIn.Explanation : "Waiting for the first reading"
        };
        return string.IsNullOrEmpty(account) ? status : $"{account} · {status}";
    }

    private void Connect(string id, bool on)
    {
        if (on) { settings.Disconnected.Remove(id); store.Connect(id); }
        else { settings.Disconnected.Add(id); store.Disconnect(id); }
        settings.Known.Add(id);
        save();
    }

    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception error) { Core.Diagnostics.Log.Ui.Error($"open {target}: {error.Message}"); }
    }
}
