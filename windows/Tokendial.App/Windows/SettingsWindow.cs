using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tokendial.App.Install;
using Tokendial.App.Panel;
using Tokendial.Core.Alerts;
using Tokendial.Core.I18n;
using Tokendial.Core.Install;
using Tokendial.Core.Providers;
using Tokendial.Core.Settings;
using Tokendial.Core.Store;

namespace Tokendial.App.Windows;

/// <summary>Providers, panel, alerts, language and startup in two independently scrolling columns. Every change saves immediately and takes effect through the host.</summary>
public sealed class SettingsWindow
{
    private const string ShieldIcon = "M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z";
    private const string BellIcon = "M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9";

    private readonly Settings settings;
    private readonly UsageStore store;
    private readonly InstallAssistant assistant;
    private readonly Action save;
    private readonly Action<bool> launchAtLogin;
    private readonly Action testAlert;
    private readonly string version;
    private readonly Window window;
    private readonly StackPanel providersList = new();
    private ScrollViewer? leftScroll;
    private ScrollViewer? rightScroll;
    private readonly TextBlock statusText = Text.Make("", 11, Chrome.Slate300);
    private bool building;

    public SettingsWindow(Settings settings, UsageStore store, InstallAssistant assistant, Action save, Action<bool> launchAtLogin, Action testAlert, string version)
    {
        this.settings = settings;
        this.store = store;
        this.assistant = assistant;
        this.save = save;
        this.launchAtLogin = launchAtLogin;
        this.testAlert = testAlert;
        this.version = version;
        window = Chrome.Frame(Strings.T("settings.title"), 1120, 860, Build(), badge: "v" + version, status: StatusPill());
        window.FlowDirection = Strings.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        window.Closed += (_, _) =>
        {
            store.Changed -= OnStoreChanged;
            assistant.Changed -= RefreshProviders;
            Closed?.Invoke();
        };
        store.Changed += OnStoreChanged;
        assistant.Changed += RefreshProviders;
        RefreshProviders();
    }

    public event Action? Closed;
    public event Action<PanelMode>? PanelModeChanged;
    public event Action<AlertConfig>? AlertsChanged;
    public event Action<string?>? LanguageChanged;
    public event Action<Appearance>? AppearanceChanged;
    public event Action<DockEdge>? EdgeChanged;

    public void Show()
    {
        if (!window.IsVisible) window.Show();
        window.Activate();
    }

    public void Close() => window.Close();

    /// <summary>The look changed: rebuild everything, title bar included, in the current palette.</summary>
    public void Retheme() => Relocalize();

    /// <summary>The language changed: rebuild every label in place under the same title bar.</summary>
    public void Relocalize()
    {
        window.Title = Strings.T("settings.title");
        window.FlowDirection = Strings.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var (left, right) = (leftScroll?.VerticalOffset ?? 0, rightScroll?.VerticalOffset ?? 0);
        Chrome.Rebuild(window, Build());
        RefreshProviders();
        window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            leftScroll?.ScrollToVerticalOffset(left);
            rightScroll?.ScrollToVerticalOffset(right);
        });
    }

    private Border StatusPill()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = Chrome.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        statusText.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(statusText);
        return new Border { Background = Chrome.WellStrong, BorderBrush = Chrome.LineFaint, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 3, 10, 3), Child = row };
    }

    private UIElement Build()
    {
        building = true;
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(460) });

        var left = new StackPanel { Margin = new Thickness(20) };
        leftScroll = Chrome.Scroll(left);
        var leftHost = new Border { BorderBrush = Chrome.LineSoft, BorderThickness = new Thickness(0, 0, 1, 0), Child = leftScroll };
        root.Children.Add(leftHost);
        var right = new StackPanel { Margin = new Thickness(20) };
        rightScroll = Chrome.Scroll(right);
        var rightHost = rightScroll;
        Grid.SetColumn(rightHost, 1);
        root.Children.Add(rightHost);

        BuildProviders(left);
        BuildGeneral(left);
        BuildAbout(left);
        BuildPanel(right);
        BuildAlerts(right);

        building = false;
        return root;
    }

    private void BuildProviders(StackPanel column)
    {
        column.Children.Add(Chrome.SectionTitle(Strings.T("settings.providers"), Strings.T("settings.providersSub"),
            Chrome.Pill(Strings.T("settings.readOnly"), Chrome.Accent, Chrome.BrandFaint, Chrome.BrandSoft, mono: true, radius: 4)));

        var callout = new Grid();
        callout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        callout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var shield = Chrome.Icon(ShieldIcon, 16, Chrome.Accent);
        shield.VerticalAlignment = VerticalAlignment.Top;
        shield.Margin = new Thickness(0, 1, 10, 0);
        callout.Children.Add(shield);
        var words = Text.Make(Strings.T("settings.providersHint"), 12, Chrome.Slate300);
        words.TextWrapping = TextWrapping.Wrap;
        words.TextTrimming = TextTrimming.None;
        words.LineHeight = 18;
        Grid.SetColumn(words, 1);
        callout.Children.Add(words);
        var calloutCard = Chrome.Card(callout, Chrome.Sheet, Chrome.Line, 12);
        calloutCard.Margin = new Thickness(0, 10, 0, 0);
        column.Children.Add(calloutCard);

        Detach(providersList);
        providersList.Margin = new Thickness(0, 12, 0, 0);
        column.Children.Add(providersList);
    }

    private void BuildGeneral(StackPanel column)
    {
        column.Children.Add(Chrome.Rule(new Thickness(0, 20, 0, 16)));
        column.Children.Add(Chrome.SmallTitle(Strings.T("settings.startup")));
        var general = new StackPanel();
        general.Children.Add(Chrome.Check(Strings.T("settings.launchAtLogin"), settings.LaunchAtLogin, v => { settings.LaunchAtLogin = v; launchAtLogin(v); Save(); }, Strings.T("settings.launchAtLoginHint")));
        general.Children.Add(Chrome.Check(Strings.T("settings.checkForUpdates"), settings.CheckForUpdates, v => { settings.CheckForUpdates = v; Save(); }, Strings.T("settings.checkForUpdatesHint")));
        general.Children.Add(Chrome.Rule(new Thickness(0, 12, 0, 12)));
        general.Children.Add(Text.Make(Strings.T("settings.language"), 12, Chrome.Slate300, FontWeights.Medium));
        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, -8) };
        void AddLanguage(string? code, string name)
        {
            chips.Children.Add(Chrome.Chip("language", name, settings.Language == code, () =>
            {
                if (building || settings.Language == code) return;
                settings.Language = code;
                save();
                LanguageChanged?.Invoke(code);
            }));
        }
        AddLanguage(null, Strings.T("settings.language.system"));
        foreach (var (code, name) in Strings.Languages) AddLanguage(code, name);
        general.Children.Add(chips);
        general.Children.Add(Chrome.Rule(new Thickness(0, 16, 0, 12)));
        general.Children.Add(Text.Make(Strings.T("settings.theme"), 12, Chrome.Slate300, FontWeights.Medium));
        var themes = new WrapPanel { Margin = new Thickness(0, 8, 0, -8) };
        void AddTheme(Appearance appearance, string key)
        {
            themes.Children.Add(Chrome.Chip("theme", Strings.T(key), settings.Appearance == appearance, () =>
            {
                if (building || settings.Appearance == appearance) return;
                settings.Appearance = appearance;
                save();
                AppearanceChanged?.Invoke(appearance);
            }));
        }
        AddTheme(Appearance.System, "settings.theme.system");
        AddTheme(Appearance.Dark, "settings.theme.dark");
        AddTheme(Appearance.Light, "settings.theme.light");
        general.Children.Add(themes);
        var card = Chrome.Card(general, Chrome.SheetSoft, Chrome.Edge, 16);
        card.Margin = new Thickness(0, 10, 0, 0);
        column.Children.Add(card);
    }

    private void BuildAbout(StackPanel column)
    {
        var title = Chrome.SmallTitle(Strings.T("settings.about"));
        title.Margin = new Thickness(0, 20, 0, 0);
        column.Children.Add(title);
        var about = new Grid();
        about.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        about.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Text.Make($"{Strings.T("app.name")} {version}", 12, Chrome.Slate200, FontWeights.Medium));
        var license = Chrome.Body(Strings.T("settings.license"));
        license.FontSize = 11;
        words.Children.Add(license);
        about.Children.Add(words);
        var links = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        links.Children.Add(Chrome.Button(Strings.T("settings.website"), () => Open("https://tokendial.app")));
        links.Children.Add(Chrome.Button(Strings.T("settings.source"), () => Open("https://github.com/r4yb3l/tokendial")));
        var folder = Chrome.Button(Strings.T("settings.dataFolder"), () => Open(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tokendial")));
        folder.Margin = new Thickness(0);
        links.Children.Add(folder);
        Grid.SetColumn(links, 1);
        about.Children.Add(links);
        var card = Chrome.Card(about, Chrome.SheetFaint, Chrome.EdgeSoft, 16);
        card.Margin = new Thickness(0, 10, 0, 0);
        column.Children.Add(card);
    }

    private void BuildPanel(StackPanel column)
    {
        column.Children.Add(Chrome.SectionTitle(Strings.T("settings.panel"), null, Text.Make(Strings.T("settings.panelSub"), 12, Chrome.Slate400)));
        var modes = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        modes.Children.Add(Chrome.RadioCard("panel", Strings.T("settings.panel.hover"), settings.Panel == PanelMode.ExpandOnHover, () => SetPanel(PanelMode.ExpandOnHover), Strings.T("settings.panel.hoverHint")));
        modes.Children.Add(Chrome.RadioCard("panel", Strings.T("settings.panel.always"), settings.Panel == PanelMode.AlwaysExpanded, () => SetPanel(PanelMode.AlwaysExpanded), Strings.T("settings.panel.alwaysHint")));
        modes.Children.Add(Chrome.RadioCard("panel", Strings.T("settings.panel.hidden"), settings.Panel == PanelMode.Hidden, () => SetPanel(PanelMode.Hidden), Strings.T("settings.panel.hiddenHint")));
        column.Children.Add(modes);
        var positionTitle = Text.Make(Strings.T("settings.position"), 12, Chrome.Slate300, FontWeights.Medium);
        positionTitle.Margin = new Thickness(0, 2, 0, 0);
        column.Children.Add(positionTitle);
        var edges = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        void AddEdge(DockEdge dockEdge, string key)
        {
            edges.Children.Add(Chrome.Chip("edge", Strings.T(key), settings.Edge == dockEdge, () =>
            {
                if (building || settings.Edge == dockEdge) return;
                settings.Edge = dockEdge;
                save();
                EdgeChanged?.Invoke(dockEdge);
            }));
        }
        AddEdge(DockEdge.Top, "settings.position.top");
        AddEdge(DockEdge.Bottom, "settings.position.bottom");
        AddEdge(DockEdge.Left, "settings.position.left");
        AddEdge(DockEdge.Right, "settings.position.right");
        column.Children.Add(edges);
    }

    private void BuildAlerts(StackPanel column)
    {
        column.Children.Add(Chrome.Rule(new Thickness(0, 6, 0, 16)));
        column.Children.Add(Chrome.SectionTitle(Strings.T("settings.alerts")));
        var hint = Chrome.Body(Strings.T("settings.alertsHint"));
        hint.FontSize = 11;
        hint.Margin = new Thickness(0, 4, 0, 0);
        column.Children.Add(hint);

        column.Children.Add(AlertCard(
            Chrome.Check(Strings.T("settings.thresholds"), settings.AlertThresholds, v => { settings.AlertThresholds = v; Save(); }, Strings.T("settings.thresholdsHint")),
            Picker("thresholds", Strings.T("settings.thresholdsField"), Strings.T("settings.thresholdsFieldHint"), Thresholds, settings.Thresholds,
                pct => $"{pct}%", pct => settings.Thresholds.Contains(pct), ToggleThreshold, multiple: true)));
        column.Children.Add(AlertCard(
            Chrome.Check(Strings.T("settings.resetSoon"), settings.AlertResetSoon, v => { settings.AlertResetSoon = v; Save(); }, Strings.T("settings.resetSoonHint")),
            Picker("leadTime", Strings.T("settings.leadTime"), null, LeadMinutes, [settings.ResetLeadMinutes],
                n => n.ToString(), n => settings.ResetLeadMinutes == n, n => { settings.ResetLeadMinutes = n; Save(); }, multiple: false)));
        column.Children.Add(AlertCard(
            Chrome.Check(Strings.T("settings.waiting"), settings.AlertWaiting, v => { settings.AlertWaiting = v; Save(); }, Strings.T("settings.waitingHint")),
            Picker("afterWaiting", Strings.T("settings.afterWaiting"), null, WaitingSeconds, [settings.WaitingDebounceSeconds],
                n => n.ToString(), n => settings.WaitingDebounceSeconds == n, n => { settings.WaitingDebounceSeconds = n; Save(); }, multiple: false)));
        column.Children.Add(AlertCard(
            Chrome.Check(Strings.T("settings.limit"), settings.AlertLimit, v => { settings.AlertLimit = v; Save(); }, Strings.T("settings.limitHint")), null));

        var deliveryTitle = Text.Make(Strings.T("settings.delivery"), 12, Chrome.Slate300, FontWeights.Medium);
        deliveryTitle.Margin = new Thickness(0, 6, 0, 6);
        column.Children.Add(deliveryTitle);
        column.Children.Add(Chrome.Radio("delivery", Strings.T("settings.delivery.banners"), settings.Delivery == AlertDelivery.TokendialBanners, () => SetDelivery(AlertDelivery.TokendialBanners), Strings.T("settings.delivery.bannersHint")));
        column.Children.Add(Chrome.Radio("delivery", Strings.T("settings.delivery.system"), settings.Delivery == AlertDelivery.WindowsToasts, () => SetDelivery(AlertDelivery.WindowsToasts), Strings.T("settings.delivery.systemHint")));
        column.Children.Add(Chrome.Radio("delivery", Strings.T("settings.delivery.both"), settings.Delivery == AlertDelivery.WindowsThenBanners, () => SetDelivery(AlertDelivery.WindowsThenBanners)));

        var test = Chrome.WideButton(BellIcon, Strings.T("settings.testAlert"), testAlert);
        test.Margin = new Thickness(0, 10, 0, 0);
        column.Children.Add(test);
    }

    /// <summary>The numbers these three settings can take. They used to be typed in, which let a slip of the keyboard silence every alert; the alert engine only ever wanted a handful of values anyway.</summary>
    private static readonly int[] Thresholds = [50, 60, 70, 80, 90, 95];
    private static readonly int[] LeadMinutes = [5, 10, 15, 30, 60];
    private static readonly int[] WaitingSeconds = [10, 20, 30, 60];

    /// <summary>A labelled row of chips, one per allowed value. A value saved before this window offered a fixed set keeps its own chip, so nothing is dropped behind the user's back.</summary>
    private UIElement Picker(string group, string label, string? hint, int[] choices, IEnumerable<int> current, Func<int, string> text, Func<int, bool> chosen, Action<int> pick, bool multiple)
    {
        var stack = new StackPanel();
        var caption = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        caption.Children.Add(Text.Make(label, 12, Chrome.Slate300));
        if (hint is not null)
        {
            var line = Chrome.Body(hint);
            line.FontSize = 11;
            caption.Children.Add(line);
        }
        stack.Children.Add(caption);
        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, -8) };
        foreach (var value in choices.Union(current).Distinct().OrderBy(v => v))
        {
            var each = value;
            if (multiple)
            {
                var box = Chrome.CheckChip(text(each), chosen(each));
                box.Click += (_, _) => { pick(each); box.IsChecked = chosen(each); };
                chips.Children.Add(box);
            }
            else chips.Children.Add(Chrome.Chip(group, text(each), chosen(each), () => { if (!building) pick(each); }));
        }
        stack.Children.Add(chips);
        return stack;
    }

    /// <summary>The last threshold cannot be cleared: with none left the alert would be on and silent.</summary>
    private void ToggleThreshold(int pct)
    {
        var next = settings.Thresholds.ToList();
        if (next.Contains(pct))
        {
            if (next.Count == 1) return;
            next.Remove(pct);
        }
        else next.Add(pct);
        next.Sort();
        settings.Thresholds = next;
        Save();
    }

    /// <summary>One alert kind: its switch, and under a rule the number it takes.</summary>
    private static Border AlertCard(CheckBox toggle, UIElement? input)
    {
        var stack = new StackPanel();
        stack.Children.Add(toggle);
        if (input is not null)
        {
            stack.Children.Add(Chrome.Rule(new Thickness(0, 10, 0, 2)));
            stack.Children.Add(input);
        }
        var card = Chrome.Card(stack, Chrome.SheetStrong, Chrome.Edge, 14);
        card.Margin = new Thickness(0, 10, 0, 0);
        return card;
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
        var active = 0;
        foreach (var summary in store.Summaries)
        {
            var connected = summary.Connected;
            var recipe = assistant.Recipe(summary.Id);
            var state = recipe is null ? InstallState.Connected : assistant.State(summary);
            var assisted = recipe is not null && state != InstallState.Connected;
            var live = connected && !assisted;
            if (live) active++;
            var reading = connected ? readings.GetValueOrDefault(summary.Id) : null;
            providersList.Children.Add(ProviderCard(summary, recipe, state, assisted, live, reading));
        }
        statusText.Text = Strings.Plural("settings.connectedCount", active);
    }

    private Border ProviderCard(ProviderSummary summary, InstallRecipe? recipe, InstallState state, bool assisted, bool live, Core.Model.ProviderReading? reading)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var toggle = Chrome.CheckBox(summary.Connected, on => Connect(summary.Id, on));
        toggle.VerticalAlignment = VerticalAlignment.Center;
        toggle.Margin = new Thickness(0, 0, 14, 0);
        row.Children.Add(toggle);

        var tint = Marks.Tint(summary.Id);
        var mark = new MarkView(summary.Id, 16) { Fill = live ? new SolidColorBrush(tint) : Chrome.Slate400, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (assisted && assistant.Waiting(summary.Id)) mark.Pulse(true, Theme.AmpleColor, Theme.TextDisabledColor, Chrome.Slate400);
        var tile = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = live ? new SolidColorBrush(Color.FromArgb(0x33, tint.R, tint.G, tint.B)) : Chrome.Surface750,
            BorderBrush = live ? new SolidColorBrush(Color.FromArgb(0x66, tint.R, tint.G, tint.B)) : Chrome.Surface700,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = mark
        };
        Grid.SetColumn(tile, 1);
        row.Children.Add(tile);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(Text.Make(summary.Name, 12, live ? Chrome.Strong : Chrome.Slate300, live ? FontWeights.SemiBold : FontWeights.Medium));
        if (live && reading is { HasReading: true } && reading.HeadlineFraction is double fraction)
        {
            var percent = (int)Math.Round(fraction * 100);
            var badge = Chrome.Pill(Strings.T("settings.usedBadge", ("pct", percent)), percent > 0 ? Chrome.Accent : Chrome.Slate300, percent > 0 ? Chrome.BrandSoft : Chrome.Surface700, Brushes.Transparent, mono: true, size: 10, radius: 4);
            badge.Padding = new Thickness(6, 1, 6, 1);
            badge.Margin = new Thickness(8, 0, 0, 0);
            badge.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(badge);
        }
        words.Children.Add(header);
        var detail = Chrome.Body(assisted && recipe is not null
            ? InstallSteps.Detail(summary.Id, summary.Name, recipe, state, assistant)
            : Detail(summary, reading));
        detail.FontSize = 11;
        detail.Foreground = live ? Chrome.Slate400 : Chrome.Slate500;
        detail.Margin = new Thickness(0, 2, 0, 0);
        words.Children.Add(detail);
        if (assisted) words.Children.Add(InstallSteps.Strip(state));
        Grid.SetColumn(words, 2);
        row.Children.Add(words);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        if (assisted && recipe is not null)
        {
            if (InstallSteps.Action(window, summary, recipe, state, assistant, () => store.OpenSource(summary.Id)) is Button next) { next.Margin = new Thickness(0); actions.Children.Add(next); }
            else
            {
                var pending = Text.Make(Strings.T("settings.inactive"), 11, Chrome.Slate500);
                pending.FontFamily = Chrome.Mono;
                actions.Children.Add(pending);
            }
        }
        else if (live)
        {
            if (summary.Account is null && summary.SignIn is SignInRoute.OpenApp app)
                actions.Children.Add(Chrome.Button(Strings.T("settings.open", ("name", app.Name)), () => store.OpenSource(summary.Id)));
            if (summary.Account?.ManageUrl is Uri manage)
                actions.Children.Add(Chrome.Button(Strings.T("settings.manage"), () => Open(manage.ToString())));
            var refresh = Chrome.Button(Strings.T("settings.refresh"), () => _ = store.Poll(summary.Id));
            refresh.Margin = new Thickness(0);
            actions.Children.Add(refresh);
        }
        else
        {
            var inactive = Text.Make(Strings.T("settings.inactive"), 11, Chrome.Slate500);
            inactive.FontFamily = Chrome.Mono;
            actions.Children.Add(inactive);
        }
        Grid.SetColumn(actions, 3);
        row.Children.Add(actions);

        var body = new StackPanel();
        body.Children.Add(row);
        if (live && reading is { HasReading: true } && reading.HeadlineFraction is double f) body.Children.Add(ProgressBar(f));

        Border card;
        if (live)
        {
            card = Chrome.Card(body, Chrome.ActiveCard, Chrome.BrandLine, 14);
            card.MouseEnter += (_, _) => card.BorderBrush = Chrome.BrandLineStrong;
            card.MouseLeave += (_, _) => card.BorderBrush = Chrome.BrandLine;
        }
        else
        {
            card = Chrome.Card(body, Chrome.SheetSoft, Chrome.Edge, 12);
            if (!assisted)
            {
                card.Opacity = 0.75;
                card.MouseEnter += (_, _) => card.Opacity = 1;
                card.MouseLeave += (_, _) => card.Opacity = 0.75;
            }
        }
        card.Margin = new Thickness(0, 0, 0, 10);
        card.Tag = summary.Id;
        return card;
    }

    /// <summary>A thin track under a connected provider, filled to its headline usage; the fill warms from green towards the band colour as it grows.</summary>
    private static Border ProgressBar(double fraction)
    {
        var shown = Math.Clamp(fraction, 0.02, 1);
        var track = new Grid { Height = 6 };
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(shown, GridUnitType.Star) });
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - shown, GridUnitType.Star) });
        Brush fill = fraction < 0.5 ? Chrome.Brand500 : new LinearGradientBrush(Theme.AmpleColor, ((SolidColorBrush)Theme.Of(fraction)).Color, 0);
        track.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(3) });
        return new Border { Background = Chrome.Surface800, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 12, 0, 0), Child = track };
    }

    /// <summary>Bring one provider's row into view, for a click on a dial whose tool still has to be installed.</summary>
    public void Focus(string providerId)
    {
        Show();
        foreach (var child in providersList.Children)
        {
            if (child is FrameworkElement row && row.Tag as string == providerId) { row.BringIntoView(); return; }
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
