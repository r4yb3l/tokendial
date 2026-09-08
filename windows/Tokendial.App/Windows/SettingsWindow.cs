using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Tokendial.App.Panel;
using Tokendial.Core.Alerts;
using Tokendial.Core.I18n;
using Tokendial.Core.Providers;
using Tokendial.Core.Settings;
using Tokendial.Core.Store;

namespace Tokendial.App.Windows;

/// <summary>Providers, panel, alerts, language and startup. Every change saves immediately and takes effect through the host.</summary>
public sealed class SettingsWindow
{
    private readonly Settings settings;
    private readonly UsageStore store;
    private readonly Action save;
    private readonly Action<bool> launchAtLogin;
    private readonly Action testAlert;
    private readonly string version;
    private readonly Window window;
    private readonly StackPanel providersList = new();
    private bool building;

    public SettingsWindow(Settings settings, UsageStore store, Action save, Action<bool> launchAtLogin, Action testAlert, string version)
    {
        this.settings = settings;
        this.store = store;
        this.save = save;
        this.launchAtLogin = launchAtLogin;
        this.testAlert = testAlert;
        this.version = version;
        window = Chrome.Frame(Strings.T("settings.title"), 560, 760, Build());
        window.FlowDirection = Strings.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        window.Closed += (_, _) => Closed?.Invoke();
        store.Changed += OnStoreChanged;
        RefreshProviders();
    }

    public event Action? Closed;
    public event Action<PanelMode>? PanelModeChanged;
    public event Action<AlertConfig>? AlertsChanged;
    public event Action<string?>? LanguageChanged;

    public void Show()
    {
        if (!window.IsVisible) window.Show();
        window.Activate();
    }

    public void Close() => window.Close();

    /// <summary>The language changed: rebuild every label in place.</summary>
    public void Relocalize()
    {
        window.Title = Strings.T("settings.title");
        window.FlowDirection = Strings.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        window.Content = Build();
        RefreshProviders();
    }

    private UIElement Build()
    {
        building = true;
        var page = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
        if (providersList.Parent is Border oldSection) oldSection.Child = null;
        page.Children.Add(Chrome.Section(Strings.T("settings.providers"), Strings.T("settings.providersHint"), Detach(providersList)));

        var panel = new StackPanel();
        panel.Children.Add(Chrome.Radio("panel", Strings.T("settings.panel.hover"), settings.Panel == PanelMode.ExpandOnHover, () => SetPanel(PanelMode.ExpandOnHover), Strings.T("settings.panel.hoverHint")));
        panel.Children.Add(Chrome.Radio("panel", Strings.T("settings.panel.always"), settings.Panel == PanelMode.AlwaysExpanded, () => SetPanel(PanelMode.AlwaysExpanded)));
        panel.Children.Add(Chrome.Radio("panel", Strings.T("settings.panel.hidden"), settings.Panel == PanelMode.Hidden, () => SetPanel(PanelMode.Hidden), Strings.T("settings.panel.hiddenHint")));
        page.Children.Add(Chrome.Section(Strings.T("settings.panel"), null, panel));

        var alerts = new StackPanel();
        alerts.Children.Add(Chrome.Check(Strings.T("settings.thresholds"), settings.AlertThresholds, v => { settings.AlertThresholds = v; Save(); }, Strings.T("settings.thresholdsHint")));
        alerts.Children.Add(Chrome.Row(Strings.T("settings.thresholdsField"), Chrome.Field(string.Join(", ", settings.Thresholds), text =>
        {
            var parsed = text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries).Select(t => int.TryParse(t, out var n) ? n : -1).Where(n => n is > 0 and <= 100).Distinct().OrderBy(n => n).ToList();
            if (parsed.Count > 0) { settings.Thresholds = parsed; Save(); }
        }), Strings.T("settings.thresholdsFieldHint")));
        alerts.Children.Add(Chrome.Check(Strings.T("settings.resetSoon"), settings.AlertResetSoon, v => { settings.AlertResetSoon = v; Save(); }, Strings.T("settings.resetSoonHint")));
        alerts.Children.Add(Chrome.Row(Strings.T("settings.leadTime"), Chrome.Field(settings.ResetLeadMinutes.ToString(), text => { if (int.TryParse(text, out var n) && n is >= 1 and <= 120) { settings.ResetLeadMinutes = n; Save(); } }, 80)));
        alerts.Children.Add(Chrome.Check(Strings.T("settings.waiting"), settings.AlertWaiting, v => { settings.AlertWaiting = v; Save(); }, Strings.T("settings.waitingHint")));
        alerts.Children.Add(Chrome.Row(Strings.T("settings.afterWaiting"), Chrome.Field(settings.WaitingDebounceSeconds.ToString(), text => { if (int.TryParse(text, out var n) && n is >= 5 and <= 600) { settings.WaitingDebounceSeconds = n; Save(); } }, 80)));
        alerts.Children.Add(Chrome.Check(Strings.T("settings.limit"), settings.AlertLimit, v => { settings.AlertLimit = v; Save(); }, Strings.T("settings.limitHint")));
        var delivery = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        delivery.Children.Add(Chrome.Radio("delivery", Strings.T("settings.delivery.banners"), settings.Delivery == AlertDelivery.TokendialBanners, () => SetDelivery(AlertDelivery.TokendialBanners), Strings.T("settings.delivery.bannersHint")));
        delivery.Children.Add(Chrome.Radio("delivery", Strings.T("settings.delivery.system"), settings.Delivery == AlertDelivery.WindowsToasts, () => SetDelivery(AlertDelivery.WindowsToasts), Strings.T("settings.delivery.systemHint")));
        delivery.Children.Add(Chrome.Radio("delivery", Strings.T("settings.delivery.both"), settings.Delivery == AlertDelivery.WindowsThenBanners, () => SetDelivery(AlertDelivery.WindowsThenBanners)));
        alerts.Children.Add(delivery);
        var test = Chrome.Button(Strings.T("settings.testAlert"), testAlert);
        test.Margin = new Thickness(0, 10, 0, 0);
        test.HorizontalAlignment = HorizontalAlignment.Left;
        alerts.Children.Add(test);
        page.Children.Add(Chrome.Section(Strings.T("settings.alerts"), Strings.T("settings.alertsHint"), alerts));

        var general = new StackPanel();
        general.Children.Add(Chrome.Check(Strings.T("settings.launchAtLogin"), settings.LaunchAtLogin, v => { settings.LaunchAtLogin = v; launchAtLogin(v); Save(); }));
        var languages = new ComboBox { Width = 200, SelectedIndex = 0, Foreground = Theme.TextPrimary };
        languages.Items.Add(Strings.T("settings.language.system"));
        foreach (var (code, name) in Strings.Languages)
        {
            languages.Items.Add(name);
            if (settings.Language == code) languages.SelectedIndex = languages.Items.Count - 1;
        }
        languages.SelectionChanged += (_, _) =>
        {
            if (building) return;
            var code = languages.SelectedIndex <= 0 ? null : Strings.Languages[languages.SelectedIndex - 1].Code;
            if (code == settings.Language) return;
            settings.Language = code;
            save();
            LanguageChanged?.Invoke(code);
        };
        general.Children.Add(Chrome.Row(Strings.T("settings.language"), languages));
        page.Children.Add(Chrome.Section(Strings.T("settings.startup"), null, general));

        var about = new StackPanel();
        about.Children.Add(Chrome.Body(Strings.T("settings.aboutLine", ("version", version))));
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        links.Children.Add(Chrome.Button(Strings.T("settings.website"), () => Open("https://tokendial.app")));
        links.Children.Add(Chrome.Button(Strings.T("settings.source"), () => Open("https://github.com/r4yb3l/tokendial")));
        links.Children.Add(Chrome.Button(Strings.T("settings.dataFolder"), () => Open(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tokendial"))));
        about.Children.Add(links);
        page.Children.Add(Chrome.Section(Strings.T("settings.about"), null, about));
        building = false;
        return Chrome.Scroll(page);
    }

    private static T Detach<T>(T element) where T : FrameworkElement
    {
        if (element.Parent is System.Windows.Controls.Panel parent) parent.Children.Remove(element);
        else if (element.Parent is Decorator decorator) decorator.Child = null;
        return element;
    }

    private void SetPanel(PanelMode mode)
    {
        if (building || settings.Panel == mode) return;
        settings.Panel = mode;
        Save();
        PanelModeChanged?.Invoke(mode);
    }

    private void SetDelivery(AlertDelivery delivery)
    {
        if (building || settings.Delivery == delivery) return;
        settings.Delivery = delivery;
        save();
    }

    private void Save()
    {
        if (building) return;
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
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var connected = summary.Connected;
            var toggle = new CheckBox { IsChecked = connected, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            toggle.Checked += (_, _) => Connect(summary.Id, true);
            toggle.Unchecked += (_, _) => Connect(summary.Id, false);
            grid.Children.Add(toggle);

            var mark = new MarkView(summary.Id, 18) { Fill = connected ? Theme.TextPrimary : Theme.TextDisabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(mark, 1);
            grid.Children.Add(mark);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Chrome.Label(summary.Name));
            var detailBlock = Chrome.Body(Detail(summary, connected ? readings.GetValueOrDefault(summary.Id) : null));
            detailBlock.FontSize = 11;
            text.Children.Add(detailBlock);
            Grid.SetColumn(text, 2);
            grid.Children.Add(text);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (connected && summary.Account is null && summary.SignIn is SignInRoute.OpenApp app)
                actions.Children.Add(Chrome.Button(Strings.T("settings.open", ("name", app.Name)), () => store.OpenSource(summary.Id)));
            if (connected && summary.Account?.ManageUrl is Uri manage)
                actions.Children.Add(Chrome.Button(Strings.T("settings.manage"), () => Open(manage.ToString())));
            if (connected)
                actions.Children.Add(Chrome.Button(Strings.T("settings.refresh"), () => _ = store.Poll(summary.Id)));
            Grid.SetColumn(actions, 3);
            grid.Children.Add(actions);
            providersList.Children.Add(grid);
        }
    }

    private static string Detail(ProviderSummary summary, Core.Model.ProviderReading? reading)
    {
        if (!summary.Connected) return Strings.T("status.off");
        var account = summary.Account?.Summary;
        var status = reading?.Status switch
        {
            Core.Model.ReadingStatus.Live => reading.HasReading ? reading.HeadlineText : Strings.T("status.connected"),
            Core.Model.ReadingStatus.NeedsSignIn => summary.SignIn.Explanation,
            Core.Model.ReadingStatus.Unsupported u => u.Why,
            Core.Model.ReadingStatus.Failed f => Strings.T("card.unavailable", ("why", f.Why)),
            Core.Model.ReadingStatus.Stale s when s.Since > DateTimeOffset.MinValue => Strings.T("card.lastRead", ("ago", Core.Model.Copy.Ago(s.Since, DateTimeOffset.UtcNow))),
            _ => summary.Account is null ? summary.SignIn.Explanation : Strings.T("status.waitingFirst")
        };
        return string.IsNullOrEmpty(account) ? status : $"{account} · {status}";
    }

    private void Connect(string id, bool on)
    {
        if (building) return;
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
