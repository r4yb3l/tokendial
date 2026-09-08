using System.Windows;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;
using Tokendial.App.Alerts;
using Tokendial.App.Panel;
using Tokendial.App.Tray;
using Tokendial.App.Windows;
using Tokendial.Core.Alerts;
using Tokendial.Core.Diagnostics;
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
        Sqlite.SweepCache();

        var providers = ProviderCatalog.Providers(archive);
        store = new UsageStore(providers, archive, settings.Disconnected, launcher: new AppLauncher());
        hub = new ActivityHub(ProviderCatalog.Monitors());
        store.IsBusy = () => hub.AnyWorking;
        toasts = new ToastSink(id => store.Readings.FirstOrDefault(r => r.ProviderId == id), hub.For);
        banners = new BannerSink(Dispatcher, id => store.Readings.FirstOrDefault(r => r.ProviderId == id), hub.For);
        banners.Opened += _ => panel.Flash(TimeSpan.FromSeconds(8));
        router = new AlertRouter(toasts, banners, () => settings.Delivery);
        alerts = new AlertCoordinator(router, settings.AlertConfig, AlertCoordinator.DefaultStateFile, wants: settings.Wants);

        panel = new PanelWindow();
        panel.HoverChanged += on => { alerts.OnHover(on); if (on) toasts.ClearMuted(); };
        panel.ExpandedShown += () => toasts.ClearMuted();
        panel.ProviderClicked += OpenProvider;
        panel.SettingsRequested += ShowSettings;

        tray = new TrayIcon();
        tray.ShowRequested += () => panel.Flash(TimeSpan.FromSeconds(6));
        tray.RefreshRequested += () => store.PollNow();
        tray.SettingsRequested += ShowSettings;
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
        SystemEvents.UserPreferenceChanged += (_, args) => { if (args.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General) Dispatcher.BeginInvoke(panel.Reposition); };

        panel.Show();
        panel.SetMode(settings.Panel);
        RefreshModel();

        clockTimer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => RefreshModel(), Dispatcher);
        clockTimer.Start();

        if (settings.FirstRunDone) Begin();
        else FirstRun();
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
        WelcomeWindow.Show(detected, absent, chosen =>
        {
            settings.Disconnected = all.Select(s => s.Id).Where(id => !chosen.Contains(id)).ToHashSet(StringComparer.Ordinal);
            settings.FirstRunDone = true;
            settings.LastSeenVersion = Version;
            Save();
            store.Disconnected = settings.Disconnected;
            Begin();
        }, ShowSettings);
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
            var model = PanelModel.Build(store.Readings, store.Summaries, hub.Activities, store.InFlight, toasts.Muted);
            panel.Update(model);
            var worst = model.Tiles.Where(t => t.HasReading).Select(t => t.Fraction).Max();
            tray.Update(worst, Tooltip(model));
        }, Dispatcher);
        modelTimer.Stop();
        modelTimer.Start();
    }

    private static string Tooltip(PanelModel model)
    {
        if (model.Tiles.Count == 0) return "Tokendial · no providers connected";
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
        if (reading?.Status is Core.Model.ReadingStatus.NeedsSignIn && store.OpenSource(id)) return;
        if (summary?.Account?.ManageUrl is Uri url) SettingsWindow.Open(url.ToString());
        else ShowSettings();
    }

    private void ShowSettings()
    {
        if (settingsWindow is null)
        {
            settingsWindow = new SettingsWindow(settings, store, Save, LaunchAtLogin.Set, TestAlert, Version);
            settingsWindow.PanelModeChanged += mode => panel.SetMode(mode);
            settingsWindow.AlertsChanged += config => alerts.Reconfigure(config, settings.Wants);
            settingsWindow.Closed += () => settingsWindow = null;
        }
        settingsWindow.Show();
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
        store.Dispose();
        foreach (var provider in ProviderDisposables()) provider.Dispose();
        ToastNotificationManagerCompat.History.Clear();
        log.Dispose();
        base.OnExit(e);
    }

    private static IEnumerable<IDisposable> ProviderDisposables() => [];
}
