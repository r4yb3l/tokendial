using System.Windows;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;
using Tokendial.App.Alerts;
using Tokendial.App.Install;
using Tokendial.App.Panel;
using Tokendial.App.Tray;
using Tokendial.App.Windows;
using Tokendial.Core.Alerts;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.I18n;
using Tokendial.Core.Providers;
using Tokendial.Core.Sessions;
using Tokendial.Core.Settings;
using Tokendial.Core.Store;

namespace Tokendial.App;

/// <summary>The composition root: settings, store, monitors, alert engine, panel, tray, toasts, wired on the UI thread.</summary>
public sealed class App : Application
{
    private readonly SingleInstance instance;
    private readonly FileLog log = new();
    private readonly Settings settings = Settings.Load();
    private readonly ReadingArchive archive = new();
    private UsageStore store = null!;
    private InstallAssistant installer = null!;
    private readonly HashSet<string> adopted = new(StringComparer.Ordinal);
    private ActivityHub hub = null!;
    private AlertCoordinator alerts = null!;
    private ToastSink toasts = null!;
    private BannerSink banners = null!;
    private AlertRouter router = null!;
    private PanelWindow panel = null!;
    private TrayIcon tray = null!;
    private SettingsWindow? settingsWindow;
    private DispatcherTimer? clockTimer;
    private DispatcherTimer? modelTimer;

    public App(SingleInstance instance)
    {
        this.instance = instance;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, e) => { Log.Ui.Error($"unhandled: {e.Exception}"); e.Handled = true; };
    }

    public static string Version => typeof(App).Assembly.GetName().Version is Version v ? $"{v.Major}.{v.Minor}.{v.Build}" : "dev";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Ui.Info($"Tokendial {Version} starting");
        Strings.Use(settings.Language);
        ApplyAppearance(rebuild: false);
        Sqlite.SweepCache();

        var providers = ProviderCatalog.Providers(archive);
        store = new UsageStore(providers, archive, settings.Disconnected, launcher: new AppLauncher());
        installer = new InstallAssistant(providers, Dispatcher);
        installer.SignedIn += OnToolSignedIn;
        hub = new ActivityHub(ProviderCatalog.Monitors());
        store.IsBusy = () => hub.AnyWorking;
        toasts = new ToastSink(id => store.Readings.FirstOrDefault(r => r.ProviderId == id), hub.For);
        banners = new BannerSink(Dispatcher, id => store.Readings.FirstOrDefault(r => r.ProviderId == id), hub.For);
        banners.Opened += _ => panel.Flash(TimeSpan.FromSeconds(8));
        router = new AlertRouter(toasts, banners, () => settings.Delivery);
        alerts = new AlertCoordinator(router, settings.AlertConfig, AlertCoordinator.DefaultStateFile, wants: settings.Wants);

        panel = new PanelWindow();
        panel.SetEdge(settings.Edge);
        panel.HoverChanged += on => { alerts.OnHover(on); if (on) toasts.ClearMuted(); };
        panel.ExpandedShown += () => toasts.ClearMuted();
        panel.ProviderClicked += OpenProvider;
        panel.SettingsRequested += ShowSettings;

        tray = new TrayIcon();
        tray.ShowRequested += () => panel.Flash(TimeSpan.FromSeconds(6));
        tray.RefreshRequested += () => store.PollNow();
        tray.SettingsRequested += ShowSettings;
        tray.TestAlertRequested += TestAlert;
        tray.QuitRequested += Quit;

        store.Changed += () => Dispatcher.BeginInvoke(DispatcherPriority.Background, OnStoreChanged);
        hub.Changed += () => Dispatcher.BeginInvoke(DispatcherPriority.Background, OnHubChanged);
        toasts.MutedChanged += () => Dispatcher.BeginInvoke(DispatcherPriority.Background, RefreshModel);

        ToastNotificationManagerCompat.OnActivated += args => Dispatcher.BeginInvoke(() => OnToast(ToastArguments.Parse(args.Argument)));
        instance.Listen(signal => Dispatcher.BeginInvoke(() =>
        {
            switch (signal)
            {
                case SingleInstance.Signal.Settings: ShowSettings(); break;
                case SingleInstance.Signal.TestAlert: TestAlert(); break;
                default: panel.Flash(); break;
            }
        }));
        SystemEvents.PowerModeChanged += (_, args) => { if (args.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(() => store.OnWake()); };
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(panel.Reposition);
        SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General) Dispatcher.BeginInvoke(panel.Reposition);
            if (args.Category == UserPreferenceCategory.General && settings.Appearance == Appearance.System) Dispatcher.BeginInvoke(() => ApplyAppearance(rebuild: true));
        };

        panel.Show();
        panel.FlowDirection = Strings.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        panel.SetMode(settings.Panel);
        RefreshModel();

        clockTimer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => RefreshModel(), Dispatcher);
        clockTimer.Start();

        if (settings.FirstRunDone) { AdoptNewProviders(providers); Begin(); }
        else FirstRun();
    }

    /// <summary>A provider this install has never seen (an update added it) is connected only when its tool is already signed in.</summary>
    private void AdoptNewProviders(IReadOnlyList<IUsageProvider> providers)
    {
        var fresh = providers.Where(p => !settings.Known.Contains(p.Id)).ToList();
        if (fresh.Count == 0) return;
        foreach (var provider in fresh)
        {
            settings.Known.Add(provider.Id);
            if (provider.Account() is null) settings.Disconnected.Add(provider.Id);
        }
        Save();
        store.Disconnected = settings.Disconnected;
    }

    private void Begin()
    {
        store.Start();
        hub.Start();
        alerts.Start();
        if (settings.LaunchAtLogin != LaunchAtLogin.IsSet()) LaunchAtLogin.Set(settings.LaunchAtLogin);
    }

    private void FirstRun()
    {
        var all = store.Summaries;
        var detected = all.Where(s => s.Account is not null).ToList();
        var absent = all.Where(s => s.Account is null).ToList();
        WelcomeWindow.Show(detected, absent, installer, chosen =>
        {
            var keep = chosen.Concat(adopted).ToHashSet(StringComparer.Ordinal);
            settings.Disconnected = all.Select(s => s.Id).Where(id => !keep.Contains(id)).ToHashSet(StringComparer.Ordinal);
            settings.Known = all.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            settings.FirstRunDone = true;
            settings.LastSeenVersion = Version;
            Save();
            store.Disconnected = settings.Disconnected;
            Begin();
        }, ShowSettings);
    }

    /// <summary>The assistant saw the tool sign in: the provider joins the dial without another click. During the welcome it is kept for the choice being made there.</summary>
    private void OnToolSignedIn(string id)
    {
        adopted.Add(id);
        settings.Disconnected.Remove(id);
        settings.Known.Add(id);
        if (!settings.FirstRunDone) return;
        Save();
        store.Connect(id);
    }

    private void OnStoreChanged()
    {
        alerts.OnReadings(store.Readings);
        RefreshModel();
    }

    private void OnHubChanged()
    {
        alerts.OnActivities(hub.Activities);
        RefreshModel();
    }

    private void RefreshModel()
    {
        modelTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Background, (_, _) =>
        {
            modelTimer!.Stop();
            var model = PanelModel.Build(store.Readings, store.Summaries, hub.Activities, store.InFlight, toasts.Muted, store.Forecasts);
            panel.Update(model);
            var worst = model.Tiles.Where(t => t.HasReading).Select(t => t.Fraction).Max();
            tray.Update(worst, Tooltip(model));
        }, Dispatcher);
        modelTimer.Stop();
        modelTimer.Start();
    }

    private static string Tooltip(PanelModel model)
    {
        if (model.Tiles.Count == 0) return Strings.T("tray.noProviders");
        var lines = model.Tiles.Where(t => t.HasReading).Select(t => $"{t.Name} {Math.Round((t.Fraction ?? 0) * 100):0}%");
        return "Tokendial\n" + string.Join("\n", lines);
    }

    private void OnToast(ToastArguments args)
    {
        panel.Flash(TimeSpan.FromSeconds(8));
        if (args.TryGetValue("provider", out var id) && !string.IsNullOrEmpty(id)) Log.Ui.Info($"toast opened for {id}");
    }

    private void OpenProvider(string id)
    {
        var summary = store.Summaries.FirstOrDefault(s => s.Id == id);
        var reading = store.Readings.FirstOrDefault(r => r.ProviderId == id);
        if (reading?.Status is Core.Model.ReadingStatus.NeedsSignIn)
        {
            if (store.OpenSource(id)) return;
            if (installer.Recipe(id) is not null) { ShowSettings(id); return; }
        }
        if (summary?.Account?.ManageUrl is Uri url) SettingsWindow.Open(url.ToString());
        else ShowSettings();
    }

    private void ShowSettings(string focusProviderId)
    {
        ShowSettings();
        settingsWindow?.Focus(focusProviderId);
    }

    private void ShowSettings()
    {
        if (settingsWindow is null)
        {
            settingsWindow = new SettingsWindow(settings, store, installer, Save, LaunchAtLogin.Set, TestAlert, Version);
            settingsWindow.PanelModeChanged += mode => panel.SetMode(mode);
            settingsWindow.AlertsChanged += config => alerts.Reconfigure(config, settings.Wants);
            settingsWindow.LanguageChanged += ApplyLanguage;
            settingsWindow.AppearanceChanged += _ => ApplyAppearance(rebuild: true);
            settingsWindow.EdgeChanged += panel.SetEdge;
            settingsWindow.Closed += () => settingsWindow = null;
        }
        settingsWindow.Show();
    }

    /// <summary>Dark or light, from the setting or from Windows. Live windows repaint in place.</summary>
    private void ApplyAppearance(bool rebuild)
    {
        var dark = settings.Appearance switch { Appearance.Dark => true, Appearance.Light => false, _ => SystemLook.DarkApps() };
        if (rebuild && dark == Theme.Dark) return;
        Theme.Use(dark);
        Chrome.Use(dark);
        if (!rebuild) return;
        panel.Retheme();
        settingsWindow?.Retheme();
        RefreshModel();
    }

    /// <summary>The user picked a language: every visible label is rebuilt in place.</summary>
    private void ApplyLanguage(string? code)
    {
        Strings.Use(code);
        panel.FlowDirection = Strings.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        panel.Relocalize();
        tray.Relocalize();
        settingsWindow?.Relocalize();
        RefreshModel();
    }

    /// <summary>A sample threshold alert straight to the sink, so the user sees what one looks like without waiting to cross 50%.</summary>
    private void TestAlert()
    {
        var reading = store.Readings.FirstOrDefault(r => r.HasReading) ?? store.Readings.FirstOrDefault();
        var window = reading?.Headline;
        router.Deliver(new Alert(AlertKind.Threshold, reading?.ProviderId ?? "claude", window?.Id, 80, window?.ResetsAt ?? DateTimeOffset.UtcNow.AddMinutes(51)));
    }

    private void Save()
    {
        try { settings.Save(); }
        catch (Exception error) { Log.Ui.Error($"save settings: {error.Message}"); }
    }

    private void Quit()
    {
        Log.Ui.Info("quit");
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        clockTimer?.Stop();
        banners.Close();
        tray.Dispose();
        alerts.Dispose();
        hub.Dispose();
        installer.Dispose();
        store.Dispose();
        foreach (var provider in ProviderDisposables()) provider.Dispose();
        ToastNotificationManagerCompat.History.Clear();
        log.Dispose();
        base.OnExit(e);
    }

    private static IEnumerable<IDisposable> ProviderDisposables() => [];
}
