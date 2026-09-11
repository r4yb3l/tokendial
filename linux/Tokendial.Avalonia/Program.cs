using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Tokendial.Core.I18n;
using Tokendial.Core.Providers;
using Tokendial.Core.Sessions;
using Tokendial.Core.Settings;
using Tokendial.Core.Store;
using Tokendial.Linux.Panel;
using Tokendial.Linux.Tray;

namespace Tokendial.Linux;

public static class Program
{
    public static int Main(string[] args) =>
        AppBuilder.Configure<TokendialApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
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
    private DispatcherTimer? coalesce;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        Strings.Use(settings.Language);
        Sqlite.SweepCache();

        var providers = ProviderCatalog.Providers(archive);
        Adopt(providers);

        store = new UsageStore(providers, archive, settings.Disconnected);
        hub = new ActivityHub(ProviderCatalog.Monitors());
        store.IsBusy = () => hub.AnyWorking;

        panel = new PanelWindow(providers.Where(p => !settings.Disconnected.Contains(p.Id)).Select(p => p.Id).ToList());
        desktop.MainWindow = panel;

        tray = new Tokendial.Linux.Tray.TrayIcon(this);
        tray.RefreshRequested += () => store.PollNow();
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
