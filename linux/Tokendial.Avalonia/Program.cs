using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Tokendial.Core.I18n;
using Tokendial.Core.Providers;
using Tokendial.Core.Sessions;
using Tokendial.Core.Settings;
using Tokendial.Core.Store;
using Tokendial.Linux.Install;
using Tokendial.Linux.Panel;
using Tokendial.Linux.Tray;
using Tokendial.Linux.Windows;

namespace Tokendial.Linux;

public static class Program
{
    public static int Main(string[] args)
    {
        if (!SingleInstance.Claim()) return 0;
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
        hub = new ActivityHub(ProviderCatalog.Monitors());
        store.IsBusy = () => hub.AnyWorking;

        panel = new PanelWindow(providers.Where(p => !settings.Disconnected.Contains(p.Id)).Select(p => p.Id).ToList());
        panel.SettingsRequested += ShowSettings;

        // A downloaded AppImage asks before it becomes an installed application, and the dock waits for the
        // answer: an app that simply appears, having written nothing the user saw, is the shape of something
        // that let itself in.
        if (Desktop.ShouldOffer)
        {
            var setup = new SetupWindow();
            setup.Decided += install => Decide(desktop, install);
            desktop.MainWindow = setup;
        }
        else
        {
            desktop.MainWindow = panel;
            // Just installed: open the dock for long enough to be seen, so pressing Install visibly produces
            // the application rather than a capsule the user has to go looking for.
            if (desktop.Args?.Contains(Desktop.JustInstalled) == true) panel.Flash(TimeSpan.FromSeconds(8));
        }

        tray = new Tokendial.Linux.Tray.TrayIcon(this);
        tray.ShowRequested += () => panel.Flash(TimeSpan.FromSeconds(6));
        tray.RefreshRequested += () => store.PollNow();
        tray.SettingsRequested += ShowSettings;
        tray.QuitRequested += () => desktop.Shutdown();

        // The store and the hub both change often and independently; rebuilding the model on a short timer
        // rather than on every event is what the Windows app does, for the same reason.
        coalesce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        coalesce.Tick += (_, _) => { coalesce!.Stop(); Refresh(); };
        store.Changed += () => Dispatcher.UIThread.Post(() => coalesce!.Start(), DispatcherPriority.Background);
        hub.Changed += () => Dispatcher.UIThread.Post(() => coalesce!.Start(), DispatcherPriority.Background);

        desktop.Startup += (_, _) => { store.Start(); hub.Start(); };
        desktop.ShutdownRequested += (_, _) => { store.Dispose(); hub.Dispose(); tray.Dispose(); };

        base.OnFrameworkInitializationCompleted();
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
            window = new SettingsWindow(settings, store, catalogue, Save, Quit);
            window.Closed += (_, _) => window = null;
            window.Changed += Refresh;
        }
        window.Show();
        window.Activate();
    }

    /// <summary>
    /// What the first run decides. Installing hands over to the copy it just made and ends this process, so
    /// the user is left with the installed application rather than the file they downloaded.
    /// </summary>
    private void Decide(IClassicDesktopStyleApplicationLifetime desktop, bool install)
    {
        if (install && Desktop.Install())
        {
            SingleInstance.Release();
            Desktop.Launch();
            desktop.Shutdown();
            return;
        }

        if (!install) Desktop.Decline();
        if (panel is null) return;
        desktop.MainWindow = panel;
        panel.Show();
    }

    /// <summary>Uninstalling removes the app from under itself, so it ends the session rather than lingering.</summary>
    private void Quit()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
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

    private static string Tooltip(PanelModel model)
    {
        if (model.Tiles.Count == 0) return Strings.T("tray.noProviders");
        var lines = model.Tiles.Where(t => t.HasReading).Select(t => $"{t.Name} {Math.Round((t.Fraction ?? 0) * 100):0}%");
        return string.Join('\n', ["Tokendial", .. lines]);
    }
}
