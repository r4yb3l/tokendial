using Tokendial.Core;
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
    private const string GlobeIcon = "M12 3a9 9 0 100 18 9 9 0 000-18zm0 0c2.5 2.2 3.8 5.2 3.8 9s-1.3 6.8-3.8 9c-2.5-2.2-3.8-5.2-3.8-9s1.3-6.8 3.8-9zM3.5 9h17M3.5 15h17";
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
    private TextBlock? panelHint;
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
        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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
        BuildPanel(right);
        BuildAlerts(right);

        shell.Children.Add(root);
        var footer = BuildFooter();
        Grid.SetRow(footer, 1);
        shell.Children.Add(footer);

        building = false;
        return shell;
    }

    /// <summary>The bar across the foot of the window: what this is, and where to go from here.</summary>
    private Border BuildFooter()
    {
        var bar = new Grid();
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Text.Make($"{Strings.T("app.name")} {version}", 12, Chrome.Slate200, FontWeights.Medium));
        var license = Chrome.Body(Strings.T("settings.license"));
        license.FontSize = 11;
        words.Children.Add(license);
        bar.Children.Add(words);
        var links = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        links.Children.Add(Chrome.Button(Strings.T("settings.website"), () => Open("https://tokendial.app")));
        links.Children.Add(Chrome.Button(Strings.T("settings.source"), () => Open("https://github.com/r4yb3l/tokendial")));
        var folder = Chrome.Button(Strings.T("settings.dataFolder"), () => Open(Paths.Data));
        links.Children.Add(folder);
        var remove = Chrome.Button(Strings.T("settings.uninstall"), () => Uninstall.Ask(window));
        remove.Margin = new Thickness(0);
        links.Children.Add(remove);
        Grid.SetColumn(links, 1);
        bar.Children.Add(links);
        return new Border
        {
            Background = Chrome.TitleBarFill,
            BorderBrush = Chrome.LineSoft,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 12, 20, 12),
            Child = bar
        };
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

        var readColumn = Text.Make(Strings.T("settings.readToggle"), 10, Chrome.Slate500, FontWeights.Medium);
        readColumn.HorizontalAlignment = HorizontalAlignment.Right;
        readColumn.Margin = new Thickness(0, 12, 15, -4);
        column.Children.Add(readColumn);

        Detach(providersList);
        var listCard = Chrome.Card(providersList, Chrome.Sheet, Chrome.Line, 0);
        listCard.Margin = new Thickness(0, 6, 0, 0);
        column.Children.Add(listCard);
        var listFooter = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var refreshAll = Chrome.Button(Strings.T("settings.refresh"), () => store.PollNow());
        refreshAll.Margin = new Thickness(0);
        listFooter.Children.Add(refreshAll);
        column.Children.Add(listFooter);
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
            chips.Children.Add(Chrome.Chip("language", LanguageContent(code, name), settings.Language == code, () =>
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

    /// <summary>A language chip's content: its flag, then the language's own name for itself. The system
    /// entry belongs to no place, so it keeps a globe instead.</summary>
    private static UIElement LanguageContent(string? code, string name)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var badge = Flags.For(code) ?? Chrome.Icon(GlobeIcon, 13, Chrome.Slate400);
        badge.VerticalAlignment = VerticalAlignment.Center;
        badge.Margin = new Thickness(0, 0, 7, 0);
        row.Children.Add(badge);
        var label = Text.Make(name, 11, Chrome.Slate400, FontWeights.Medium);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        return row;
    }

    private void BuildPanel(StackPanel column)
    {
        column.Children.Add(Chrome.SectionTitle(Strings.T("settings.panel"), null, Text.Make(Strings.T("settings.panelSub"), 12, Chrome.Slate400)));
        var modes = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 12, -8, 0) };
        void AddMode(PanelMode mode, string key, UIElement diagram)
        {
            modes.Children.Add(Chrome.Tile("panel", diagram, Strings.T(key), settings.Panel == mode, () => SetPanel(mode)));
        }
        AddMode(PanelMode.ExpandOnHover, "settings.panel.hover", Chrome.CompactDiagram(wide: false));
        AddMode(PanelMode.AlwaysExpanded, "settings.panel.always", Chrome.CompactDiagram(wide: true));
        AddMode(PanelMode.Hidden, "settings.panel.hidden", Chrome.TrayDiagram());
        column.Children.Add(modes);
        panelHint = Chrome.Body(PanelHint(settings.Panel));
        panelHint.FontSize = 11;
        panelHint.Margin = new Thickness(0, 2, 0, 0);
        column.Children.Add(panelHint);

        var positionTitle = Text.Make(Strings.T("settings.position"), 12, Chrome.Slate300, FontWeights.Medium);
        positionTitle.Margin = new Thickness(0, 14, 0, 0);
        column.Children.Add(positionTitle);
        var edges = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 8, -8, 0) };
        void AddEdge(DockEdge dockEdge, string key, bool vertical, bool far)
        {
            var selected = settings.Edge == dockEdge;
            edges.Children.Add(Chrome.Tile("edge", Chrome.EdgeDiagram(vertical, far, selected), Strings.T(key), selected, () =>
            {
                if (building || settings.Edge == dockEdge) return;
                settings.Edge = dockEdge;
                save();
                EdgeChanged?.Invoke(dockEdge);
                Relocalize();
            }));
        }
        AddEdge(DockEdge.Top, "settings.position.top", vertical: true, far: false);
        AddEdge(DockEdge.Bottom, "settings.position.bottom", vertical: true, far: true);
        AddEdge(DockEdge.Left, "settings.position.left", vertical: false, far: false);
        AddEdge(DockEdge.Right, "settings.position.right", vertical: false, far: true);
        column.Children.Add(edges);
    }

    private static string PanelHint(PanelMode mode) => Strings.T(mode switch
    {
        PanelMode.AlwaysExpanded => "settings.panel.alwaysHint",
        PanelMode.Hidden => "settings.panel.hiddenHint",
        _ => "settings.panel.hoverHint"
    });

    private void BuildAlerts(StackPanel column)
    {
        column.Children.Add(Chrome.Rule(new Thickness(0, 6, 0, 16)));
        column.Children.Add(Chrome.SectionTitle(Strings.T("settings.alerts")));
        var hint = Chrome.Body(Strings.T("settings.alertsHint"));
        hint.FontSize = 11;
        hint.Margin = new Thickness(0, 4, 0, 0);
        column.Children.Add(hint);

        column.Children.Add(AlertCard(
            Chrome.SwitchRow(Strings.T("settings.thresholds"), Strings.T("settings.thresholdsHint"), settings.AlertThresholds, v => { settings.AlertThresholds = v; Save(); }),
            Picker("thresholds", Strings.T("settings.thresholdsField"), Strings.T("settings.thresholdsFieldHint"), Thresholds, settings.Thresholds,
                pct => $"{pct}%", pct => settings.Thresholds.Contains(pct), ToggleThreshold, multiple: true)));
        column.Children.Add(AlertCard(
            Chrome.SwitchRow(Strings.T("settings.resetSoon"), Strings.T("settings.resetSoonHint"), settings.AlertResetSoon, v => { settings.AlertResetSoon = v; Save(); }),
            Picker("leadTime", Strings.T("settings.leadTime"), null, LeadMinutes, [settings.ResetLeadMinutes],
                n => n.ToString(), n => settings.ResetLeadMinutes == n, n => { settings.ResetLeadMinutes = n; Save(); }, multiple: false)));
        column.Children.Add(AlertCard(
            Chrome.SwitchRow(Strings.T("settings.waiting"), Strings.T("settings.waitingHint"), settings.AlertWaiting, v => { settings.AlertWaiting = v; Save(); }),
            Picker("afterWaiting", Strings.T("settings.afterWaiting"), null, WaitingSeconds, [settings.WaitingDebounceSeconds],
                n => n.ToString(), n => settings.WaitingDebounceSeconds == n, n => { settings.WaitingDebounceSeconds = n; Save(); }, multiple: false)));
        column.Children.Add(AlertCard(
            Chrome.SwitchRow(Strings.T("settings.limit"), Strings.T("settings.limitHint"), settings.AlertLimit, v => { settings.AlertLimit = v; Save(); }), null));

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
    private static Border AlertCard(UIElement toggle, UIElement? input)
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
        if (panelHint is not null) panelHint.Text = PanelHint(mode);
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
            if (providersList.Children.Count > 0) providersList.Children.Add(new Border { Height = 1, Background = Chrome.LineFaint });
            providersList.Children.Add(ProviderRow(summary, recipe, state, assisted, live, reading));
        }
        Round(providersList);
        statusText.Text = Strings.Plural("settings.connectedCount", active);
    }

    /// <summary>One line of the provider list: the tool's dial, what is known about it, and whether
    /// Tokendial reads it at all. The dial is the same 240-degree arc the dock draws, so a glance means
    /// the same thing in both places.</summary>
    private Border ProviderRow(ProviderSummary summary, InstallRecipe? recipe, InstallState state, bool assisted, bool live, Core.Model.ProviderReading? reading)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tint = Marks.Tint(summary.Id);
        var read = live && reading is { HasReading: true };
        var fraction = read ? reading!.HeadlineFraction ?? 0 : 0;
        var face = new Grid { Width = 34, Height = 34, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        face.Children.Add(new Dial(34, 3) { Fraction = fraction, Hollow = !read, Track = Chrome.Surface750, Fill = Theme.Of(fraction) });
        var mark = new MarkView(summary.Id, 15) { Fill = live ? new SolidColorBrush(tint) : Chrome.Slate500, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (assisted && assistant.Waiting(summary.Id)) mark.Pulse(true, Theme.AmpleColor, Theme.TextDisabledColor, Chrome.Slate400);
        face.Children.Add(mark);
        row.Children.Add(face);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(Text.Make(summary.Name, 12, live ? Chrome.Strong : Chrome.Slate300, live ? FontWeights.SemiBold : FontWeights.Medium));
        if (read)
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
        Grid.SetColumn(words, 1);
        row.Children.Add(words);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        if (assisted && recipe is not null)
        {
            if (InstallSteps.Action(window, summary, recipe, state, assistant, () => store.OpenSource(summary.Id)) is Button next) { next.Margin = new Thickness(0, 0, 10, 0); actions.Children.Add(next); }
        }
        else if (live)
        {
            if (summary.Account is null && summary.SignIn is SignInRoute.OpenApp app)
                actions.Children.Add(Chrome.Button(Strings.T("settings.open", ("name", app.Name)), () => store.OpenSource(summary.Id)));
            if (summary.Account?.ManageUrl is Uri manage)
                actions.Children.Add(Chrome.Button(Strings.T("settings.manage"), () => Open(manage.ToString())));
            if (actions.Children.Count > 0) ((FrameworkElement)actions.Children[^1]).Margin = new Thickness(0, 0, 10, 0);
        }
        else
        {
            var inactive = Text.Make(Strings.T("settings.inactive"), 11, Chrome.Slate500);
            inactive.FontFamily = Chrome.Mono;
            inactive.VerticalAlignment = VerticalAlignment.Center;
            inactive.Margin = new Thickness(0, 0, 10, 0);
            actions.Children.Add(inactive);
        }
        var toggle = Chrome.Switch(summary.Connected, on => Connect(summary.Id, on));
        toggle.VerticalAlignment = VerticalAlignment.Center;
        toggle.ToolTip = Strings.T("settings.readToggleHint");
        actions.Children.Add(toggle);
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);

        var host = new Border { Padding = new Thickness(12, 10, 12, 10), Child = row, Tag = summary.Id };
        if (live) host.Background = Chrome.BrandFaint;
        if (!live && !assisted)
        {
            host.Opacity = 0.75;
            host.MouseEnter += (_, _) => host.Opacity = 1;
            host.MouseLeave += (_, _) => host.Opacity = 0.75;
        }
        return host;
    }

    /// <summary>The list is one card, so the rows at its ends carry its rounded corners; a tinted row
    /// would otherwise square them off.</summary>
    private static void Round(StackPanel list)
    {
        foreach (var child in list.Children) if (child is Border row) row.CornerRadius = new CornerRadius(0);
        if (list.Children.Count == 0) return;
        if (list.Children[0] is Border first) first.CornerRadius = new CornerRadius(11, 11, 0, 0);
        if (list.Children[list.Children.Count - 1] is Border last) last.CornerRadius = new CornerRadius(0, 0, 11, 11);
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
