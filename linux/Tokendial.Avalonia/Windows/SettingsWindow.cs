using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Tokendial.Core.I18n;
using Tokendial.Core.Model;
using Tokendial.Core.Providers;
using Tokendial.Core.Settings;
using Tokendial.Core.Store;
using Tokendial.Linux.Panel;

namespace Tokendial.Linux.Windows;

/// <summary>
/// Everything the user can decide, in two columns: the providers on the left, how the dock and the alerts
/// behave on the right. Written in the Chrome vocabulary, which is why it reads like the Windows one -
/// windows/Tokendial.App/Windows/SettingsWindow.cs makes seventy-seven calls into the same names.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly Settings settings;
    private readonly UsageStore store;
    private readonly IReadOnlyList<IUsageProvider> providers;
    private readonly Action save;
    private readonly Action quit;
    private readonly Action testAlert;
    private readonly StackPanel providerList = new() { Spacing = 1 };
    private readonly TextBlock menuHint = Chrome.HintText("");
    private Button? menuButton;

    public SettingsWindow(Settings settings, UsageStore store, IReadOnlyList<IUsageProvider> providers, Action save, Action quit, Action testAlert)
    {
        this.settings = settings;
        this.store = store;
        this.providers = providers;
        this.save = save;
        this.quit = quit;
        this.testAlert = testAlert;

        Title = Strings.T("settings.title");
        Width = 1120;
        Height = 860;
        // Two columns of cards stop being two columns below this; narrower than that the tiles overlap
        // rather than reflow, so the window refuses to be squeezed into a shape it cannot draw.
        MinWidth = 900;
        MinHeight = 520;
        Background = Chrome.WindowBackground;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FlowDirection = Strings.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        Content = Build();

    }

    public event Action? Changed;

    /// <summary>The alert settings moved, so the running engine needs the new shape of them.</summary>
    public event Action<Core.Alerts.AlertConfig>? AlertsChanged;

    private Control Build()
    {
        var columns = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Margin = new Thickness(20, 16, 20, 0)
        };

        var left = Chrome.Scroll(new StackPanel { Spacing = 14, Children = { Providers() } });
        left.Margin = new Thickness(0, 0, 10, 0);
        var right = Chrome.Scroll(new StackPanel { Spacing = 14, Children = { PanelBehaviour(), Alerts(), General() } });
        right.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(right, 1);
        columns.Children.Add(left);
        columns.Children.Add(right);

        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        root.Children.Add(columns);
        var footer = Footer();
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        return root;
    }

    // ---- providers ----------------------------------------------------------------------------------

    private Control Providers()
    {
        RefreshProviders();
        return Chrome.Section(Strings.T("settings.providers"), Strings.T("settings.providersSub"), providerList);
    }

    private void RefreshProviders()
    {
        providerList.Children.Clear();
        foreach (var provider in providers) providerList.Children.Add(ProviderRow(provider));
    }

    private Control ProviderRow(IUsageProvider provider)
    {
        var connected = !settings.Disconnected.Contains(provider.Id);
        var account = provider.Account();

        var mark = new MarkView(provider.Id, 18) { Fill = Chrome.Slate300, VerticalAlignment = VerticalAlignment.Center };
        var name = Chrome.Label(provider.DisplayName);
        var status = Chrome.Body(account?.Summary ?? Strings.T("status.signIn"), wrap: false);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, status } };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { mark, text } };

        var toggle = Chrome.Switch(connected, on => Connect(provider.Id, on));
        toggle.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(head);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);

        return new Border
        {
            Child = grid,
            Padding = new Thickness(12, 10, 12, 10),
            Background = connected ? Chrome.ActiveCard : Chrome.Well,
            BorderBrush = Chrome.EdgeSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10)
        };
    }

    /// <summary>
    /// Connecting is the user's decision and the only way a provider joins the dock; nothing here watches
    /// for a credential appearing and adds one on its own.
    /// </summary>
    private void Connect(string id, bool on)
    {
        if (on) { settings.Disconnected.Remove(id); store.Connect(id); }
        else { settings.Disconnected.Add(id); store.Disconnect(id); }
        save();
        Changed?.Invoke();
    }

    // ---- the dock -----------------------------------------------------------------------------------

    private Control PanelBehaviour()
    {
        // Equal widths: three choices of the same weight should not be three different sizes.
        const double modeWidth = 150;
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var (diagram, key, mode) in new (Control, string, PanelMode)[]
                 {
                     (Chrome.CompactDiagram(false), "settings.panel.hover", PanelMode.ExpandOnHover),
                     (Chrome.CompactDiagram(true), "settings.panel.always", PanelMode.AlwaysExpanded),
                     (Chrome.TrayDiagram(), "settings.panel.hidden", PanelMode.Hidden)
                 })
        {
            var tile = Chrome.Tile("panelMode", diagram, Strings.T(key), settings.Panel == mode, () => SetPanel(mode));
            tile.Width = modeWidth;
            // Stretch, so a label that wraps to three lines does not leave its neighbours short.
            tile.VerticalAlignment = VerticalAlignment.Stretch;
            modes.Children.Add(tile);
        }

        var edges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var (edge, key, vertical, far) in new[]
                 {
                     (DockEdge.Top, "settings.position.top", false, false),
                     (DockEdge.Bottom, "settings.position.bottom", false, true),
                     (DockEdge.Left, "settings.position.left", true, false),
                     (DockEdge.Right, "settings.position.right", true, true)
                 })
        {
            var chosen = settings.Edge == edge;
            var tile = Chrome.Tile("panelEdge", Chrome.EdgeDiagram(vertical, far, chosen), Strings.T(key), chosen,
                () => { settings.Edge = edge; save(); Changed?.Invoke(); });
            tile.Width = 96;
            edges.Children.Add(tile);
        }

        // The heading needs room above it and a smaller gap below, or it reads as belonging to the tiles it
        // sits on rather than the ones it names.
        var position = Chrome.SmallTitle(Strings.T("settings.position"));
        position.Margin = new Thickness(0, 18, 0, 8);

        var body = new StackPanel { Children = { modes, position, edges } };
        return Chrome.Section(Strings.T("settings.panel"), Strings.T("settings.panelSub"), body);
    }

    private void SetPanel(PanelMode mode)
    {
        settings.Panel = mode;
        save();
        Changed?.Invoke();
    }

    // ---- alerts -------------------------------------------------------------------------------------

    private Control Alerts()
    {
        var thresholds = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var value in new[] { 50, 60, 70, 80, 90, 95 })
        {
            var on = settings.Thresholds.Contains(value);
            var chip = Chrome.Check($"{value}%", on, chosen => ToggleThreshold(value, chosen));
            chip.Margin = new Thickness(0, 0, 10, 8);
            thresholds.Children.Add(chip);
        }

        var body = new StackPanel
        {
            Children =
            {
                Chrome.SwitchRow(Strings.T("settings.thresholds"), Strings.T("settings.thresholdsHint"),
                    settings.AlertThresholds, on => { settings.AlertThresholds = on; Applied(); }),
                thresholds,
                Chrome.Rule(new Thickness(0, 10, 0, 10)),
                Chrome.SwitchRow(Strings.T("settings.resetSoon"), null, settings.AlertResetSoon,
                    on => { settings.AlertResetSoon = on; Applied(); }),
                Chrome.SwitchRow(Strings.T("settings.afterWaiting"), null, settings.AlertWaiting,
                    on => { settings.AlertWaiting = on; Applied(); }),
                Chrome.SwitchRow(Strings.T("settings.limit"), null, settings.AlertLimit,
                    on => { settings.AlertLimit = on; Applied(); }),
                Chrome.Rule(new Thickness(0, 10, 0, 10)),
                Delivery()
            }
        };
        return Chrome.Section(Strings.T("settings.alerts"), Strings.T("settings.alertsHint"), body);
    }

    private void ToggleThreshold(int value, bool on)
    {
        if (on) { if (!settings.Thresholds.Contains(value)) settings.Thresholds.Add(value); }
        else settings.Thresholds.Remove(value);
        settings.Thresholds.Sort();
        Applied();
    }

    /// <summary>
    /// Where an alert is drawn. The engine never sees this choice - it decided what to say - so changing it
    /// only saves, and the router reads it again on the next delivery.
    /// </summary>
    private Control Delivery()
    {
        var title = Chrome.SmallTitle(Strings.T("settings.delivery"));
        title.Margin = new Thickness(0, 4, 0, 6);

        var choices = new StackPanel { Spacing = 6 };
        foreach (var (key, hint, mode) in new (string, string?, AlertDelivery)[]
                 {
                     ("settings.delivery.banners", "settings.delivery.bannersHintPlain", AlertDelivery.TokendialBanners),
                     ("settings.delivery.system", "settings.delivery.systemHint", AlertDelivery.WindowsToasts),
                     ("settings.delivery.both", null, AlertDelivery.WindowsThenBanners)
                 })
            choices.Children.Add(Chrome.RadioCard("delivery", Strings.T(key), settings.Delivery == mode,
                () => { settings.Delivery = mode; save(); }, hint is null ? null : Strings.T(hint)));

        var test = Chrome.Button(Strings.T("settings.testAlert"), testAlert);
        test.HorizontalAlignment = HorizontalAlignment.Left;
        test.Margin = new Thickness(0, 12, 0, 0);

        return new StackPanel { Children = { title, choices, test } };
    }

    /// <summary>Saves, and hands the running engine the settings it now has to work to.</summary>
    private void Applied()
    {
        save();
        AlertsChanged?.Invoke(settings.AlertConfig);
    }

    // ---- general ------------------------------------------------------------------------------------

    /// <summary>
    /// Adding Tokendial to the applications menu, which is what installing means on a desktop that has no
    /// installer. Painted by hand once it has happened rather than rebuilt, the same rule every other piece
    /// of state in this window follows.
    /// </summary>
    private Control MenuEntry()
    {
        menuButton = Chrome.Button(Strings.T("settings.addToMenu.confirm"), () => _ = Uninstall.Offer(this, PaintMenu));
        menuButton.VerticalAlignment = VerticalAlignment.Center;

        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
            Children = { Chrome.Label(Strings.T("settings.addToMenu")), menuHint }
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 6) };
        grid.Children.Add(text);
        Grid.SetColumn(menuButton, 1);
        grid.Children.Add(menuButton);

        PaintMenu();
        return grid;
    }

    private void PaintMenu()
    {
        var installed = Install.Desktop.InMenu;
        menuHint.Text = Strings.T(installed ? "settings.addToMenu.inMenu" : "settings.addToMenu.hint");
        if (menuButton is not null) menuButton.IsEnabled = !installed;
    }

    private Control General()
    {
        var languages = new WrapPanel();
        foreach (var (code, label) in Strings.Languages)
        {
            var chip = Chrome.Chip("language", label, settings.Language == code, () =>
            {
                settings.Language = code;
                Strings.Use(code);
                save();
                Changed?.Invoke();
            });
            chip.Margin = new Thickness(0, 0, 8, 8);
            languages.Children.Add(chip);
        }

        var body = new StackPanel
        {
            Children =
            {
                Chrome.SwitchRow(Strings.T("settings.launchAtLogin"), Strings.T("settings.launchAtLoginHint"), settings.LaunchAtLogin,
                    on => { settings.LaunchAtLogin = on; Install.Desktop.SetAutostart(on); save(); }),
                Chrome.SwitchRow(Strings.T("settings.checkForUpdates"), Strings.T("settings.checkForUpdatesHint"),
                    settings.CheckForUpdates, on => { settings.CheckForUpdates = on; save(); }),
                MenuEntry(),
                Chrome.Rule(new Thickness(0, 10, 0, 10)),
                Chrome.SmallTitle(Strings.T("settings.language")),
                languages
            }
        };
        return Chrome.Section(Strings.T("settings.startup"), null, body);
    }

    // ---- footer -------------------------------------------------------------------------------------

    private Control Footer()
    {
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        links.Children.Add(Chrome.Button(Strings.T("settings.website"), () => Open("https://tokendial.vercel.app")));
        links.Children.Add(Chrome.Button(Strings.T("settings.source"), () => Open("https://github.com/r4yb3l/tokendial")));
        links.Children.Add(Chrome.Button(Strings.T("settings.dataFolder"), () => Open(Core.Paths.Data)));
        links.Children.Add(Chrome.Button(Strings.T("settings.uninstall"), () => _ = Uninstall.Ask(this, quit)));

        var about = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Chrome.Label($"Tokendial {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)}"),
                Chrome.Body(Strings.T("settings.license"), wrap: false)
            }
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(20, 12, 20, 16) };
        grid.Children.Add(about);
        Grid.SetColumn(links, 1);
        grid.Children.Add(links);

        return new Border { Child = grid, Background = Chrome.TitleBarFill, BorderBrush = Chrome.Divider, BorderThickness = new Thickness(0, 1, 0, 0) };
    }

    /// <summary>xdg-open is the freedesktop way to ask the desktop what opens a URL or a folder.</summary>
    private static void Open(string target)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xdg-open", target) { UseShellExecute = false }); }
        catch (Exception) { }
    }
}
