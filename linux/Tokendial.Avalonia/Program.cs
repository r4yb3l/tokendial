using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Tokendial.Core.Alerts;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.I18n;
using Tokendial.Core.Model;
using Tokendial.Core.Providers;
using Tokendial.Core.Sessions;
using Tokendial.Core.Settings;
using Tokendial.Core.Store;
using Tokendial.Linux.Alerts;
using Tokendial.Linux.Install;
using Tokendial.Linux.Panel;
using Tokendial.Linux.Tray;
using Tokendial.Linux.Windows;

namespace Tokendial.Linux;

public static class Program
{
    public static int Main(string[] args)
    {
        // The claim is taken once this copy is going to be the running Tokendial, not here: a downloaded
        // AppImage may legitimately be an installer while another copy is already running.
        using var log = new FileLog();
        Log.Ui.Info($"starting {typeof(Program).Assembly.GetName().Version?.ToString(3)}, " +
                    $"appimage={Install.Desktop.Image ?? "none"}");
        return AppBuilder.Configure<TokendialApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}

/// <summary>
/// Composition root, laid out like windows/Tokendial.App/App.cs: it owns the store and the hub, and the
/// windows only render what it hands them. Doing the reading inside the dock worked for one provider and
/// would have had to be unpicked the moment anything else needed a number.
/// </summary>
public sealed class TokendialApp : Application
{
    private readonly Settings settings = Settings.Load();
    private readonly ReadingArchive archive = new();

    private UsageStore? store;
    private ActivityHub? hub;
    private AlertCoordinator? alerts;
    private NotifySink? notifications;
    private BannerSink? banners;
    private AlertRouter? router;
    private InstallAssistant? assistant;
    private PanelWindow? panel;
    private Tokendial.Linux.Tray.TrayIcon? tray;
    private SettingsWindow? window;
    private IReadOnlyList<IUsageProvider> catalogue = [];
    private DispatcherTimer? coalesce;

    public override void Initialize()
    {
        // Built-in controls have no template without a theme: a ScrollViewer with no template reports an
        // extent of zero, shows no bar and ignores the wheel, which is why settings would not scroll.
        // Tokendial's own controls template themselves, so this decides nothing about how they look.
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        base.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        Strings.Use(settings.Language);
        Sqlite.SweepCache();

        // An update changes the path the autostart entry holds, so it is written again on every launch.
        Desktop.SyncAutostart(settings.LaunchAtLogin);

        var providers = ProviderCatalog.Providers(archive);
        catalogue = providers;
        Adopt(providers);

        store = new UsageStore(providers, archive, settings.Disconnected);
        assistant = new InstallAssistant(providers);
        assistant.SignedIn += id => { settings.Disconnected.Remove(id); store!.Connect(id); Save(); Refresh(); };
        hub = new ActivityHub(ProviderCatalog.Monitors());
        store.IsBusy = () => hub.AnyWorking;

        panel = new PanelWindow(providers.Where(p => !settings.Disconnected.Contains(p.Id)).Select(p => p.Id).ToList(),
            settings.Display);
        panel.SettingsRequested += ShowSettings;

        // The engine decided what to say and when; these two only deliver it. The preference is read at
        // delivery rather than wired once, so changing it in settings takes effect with no rewiring.
        notifications = new NotifySink(Reading, hub.For);
        banners = new BannerSink(Reading, hub.For) { Display = settings.Display };
        banners.Opened += _ => panel.Flash(TimeSpan.FromSeconds(8));
        router = new AlertRouter(notifications, banners, () => settings.Delivery);
        alerts = new AlertCoordinator(router, settings.AlertConfig, AlertCoordinator.DefaultStateFile, wants: settings.Wants);

        store.Changed += () => alerts.OnReadings(store.Readings);
        hub.Changed += () => alerts.OnActivities(hub.Activities);
        panel.HoverChanged += on => alerts.OnHover(on);

        // The store and the hub both change often and independently; rebuilding the model on a short timer
        // rather than on every event is what the Windows app does, for the same reason.
        coalesce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        coalesce.Tick += (_, _) => { coalesce!.Stop(); Refresh(); };
        store.Changed += () => Dispatcher.UIThread.Post(() => coalesce!.Start(), DispatcherPriority.Background);
        hub.Changed += () => Dispatcher.UIThread.Post(() => coalesce!.Start(), DispatcherPriority.Background);

        // A downloaded AppImage asks before it becomes an installed application, and nothing else starts -
        // no dock, no tray, no polling - until it has an answer: an application that simply appears, having
        // written nothing the user saw, is the shape of something that let itself in.
        if (Desktop.ShouldOffer)
        {
            var setup = new SetupWindow();
            setup.Decided += install => Decide(desktop, install);
            desktop.MainWindow = setup;
        }
        else Run(desktop, justInstalled: false);

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// What the first run decides. Installing writes the files and this same process carries on as the
    /// installed application: starting a second copy and handing over to it only adds a few seconds in which
    /// nothing at all happens, and a handful of ways to end up with nothing running.
    /// </summary>
    private void Decide(IClassicDesktopStyleApplicationLifetime desktop, bool install)
    {
        var installed = install && Desktop.Install();
        if (!install) Desktop.Decline();
        Log.Ui.Info(install ? $"install requested, installed={installed}" : "kept portable");
        Run(desktop, justInstalled: installed);
    }

    /// <summary>
    /// Becomes the running Tokendial: claims the session, shows the dock and starts reading. A copy that
    /// cannot claim quits, because the two of them would draw two docks and poll the same accounts twice.
    /// </summary>
    private void Run(IClassicDesktopStyleApplicationLifetime desktop, bool justInstalled)
    {
        if (store is null || hub is null || panel is null) return;
        if (!SingleInstance.Claim())
        {
            desktop.Shutdown();
            return;
        }

        desktop.MainWindow = panel;
        panel.Show();

        tray = new Tokendial.Linux.Tray.TrayIcon(this);
        tray.ShowRequested += () => panel.Flash(TimeSpan.FromSeconds(6));
        tray.RefreshRequested += () => store.PollNow();
        tray.SettingsRequested += ShowSettings;
        tray.TestAlertRequested += TestAlert;
        tray.QuitRequested += () => desktop.Shutdown();

        desktop.ShutdownRequested += (_, _) =>
        {
            store.Dispose();
            hub.Dispose();
            tray?.Dispose();
            alerts?.Dispose();
            assistant?.Dispose();
            notifications?.Dispose();
            banners?.Close();
        };
        store.Start();
        hub.Start();
        alerts?.Start();

        // Just installed: open the dock for long enough to be seen, so pressing Install visibly produces the
        // application rather than a capsule at the top of a screen nobody was looking at.
        if (justInstalled) panel.Flash(TimeSpan.FromSeconds(8));
    }

    /// <summary>
    /// The dock shows the tools the user actually uses, not the nine that exist. The rule is the one
    /// windows/Tokendial.App/App.cs:124 already applies: a provider met for the first time whose
    /// <c>Account()</c> is null - no credential, so not signed in or not installed - is recorded as
    /// disconnected, and a disconnected provider is never read. Connecting one later is a settings decision
    /// the user makes, never something discovered behind their back.
    /// </summary>
    private void Adopt(IReadOnlyList<IUsageProvider> providers)
    {
        var fresh = providers.Where(p => !settings.Known.Contains(p.Id)).ToList();
        if (fresh.Count == 0) return;
        foreach (var provider in fresh)
        {
            settings.Known.Add(provider.Id);
            if (provider.Account() is null) settings.Disconnected.Add(provider.Id);
        }
        try { settings.Save(); } catch (IOException) { }
    }

    /// <summary>One settings window, raised again rather than opened twice.</summary>
    private void ShowSettings()
    {
        if (store is null) return;
        if (window is null)
        {
            window = new SettingsWindow(settings, store, catalogue, Save, Quit, TestAlert, assistant!);
            window.Closed += (_, _) => window = null;
            window.Changed += Refresh;
            window.Changed += ApplyDisplay;
            window.AlertsChanged += config => alerts?.Reconfigure(config, settings.Wants);
        }
        window.Show();
        window.Activate();
    }

    /// <summary>Uninstalling removes the app from under itself, so it ends the session rather than lingering.</summary>
    private void Quit()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }

    /// <summary>
    /// One alert, delivered the way a real one would be, bypassing the engine entirely - the point is to
    /// show the user what an alert looks like and where it appears, not to decide whether to send one.
    /// </summary>
    private void TestAlert()
    {
        if (store is null || router is null) return;
        var reading = store.Readings.FirstOrDefault(r => r.HeadlineFraction is not null) ?? store.Readings.FirstOrDefault();
        router.Deliver(new Alert(AlertKind.Threshold, reading?.ProviderId ?? "claude", reading?.Windows.FirstOrDefault()?.Id,
            80, reading?.Windows.FirstOrDefault()?.ResetsAt ?? DateTimeOffset.Now.AddMinutes(51)));
    }

    private ProviderReading? Reading(string id) => store?.Readings.FirstOrDefault(r => r.ProviderId == id);

    /// <summary>The chosen monitor reaches the dock and the banners together, so they never disagree.</summary>
    private void ApplyDisplay()
    {
        panel?.SetDisplay(settings.Display);
        if (banners is not null) banners.Display = settings.Display;
    }

    private void Save()
    {
        try { settings.Save(); } catch (IOException) { }
    }

    private void Refresh()
    {
        if (store is null || hub is null || panel is null) return;
        var model = PanelModel.Build(store.Readings, store.Summaries, hub.Activities, store.InFlight, 0, store.Forecasts);
        panel.Update(model);

        // The tray shows the worst reading, because one number in a panel can only answer one question and
        // "how close am I to a limit" is the one worth answering.
        var read = model.Tiles.Where(t => t.HasReading).Select(t => t.Fraction).ToList();
        tray?.Update(read.Count > 0 ? read.Max() : null, Tooltip(model));
    }

    /// <summary>
    /// One line, joined with the dot the rest of Tokendial uses. The Windows tray stacks these on separate
    /// lines, but Avalonia's StatusNotifierItem backend publishes the tooltip as the item's Id as well, and
    /// an Id carrying a newline is not an identifier: Cinnamon's watcher accepts the registration and then
    /// draws nothing at all.
    /// </summary>
    private static string Tooltip(PanelModel model)
    {
        if (model.Tiles.Count == 0) return Strings.T("tray.noProviders");
        var parts = model.Tiles.Where(t => t.HasReading).Select(t => $"{t.Name} {Math.Round((t.Fraction ?? 0) * 100):0}%");
        return string.Join(" · ", ["Tokendial", .. parts]);
    }
}
