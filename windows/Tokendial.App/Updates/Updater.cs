using System.Windows.Threading;
using Tokendial.Core.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace Tokendial.App.Updates;

/// <summary>
/// Asks GitHub Releases once a day whether a newer Tokendial exists, downloads it in the background and then waits
/// for the user to restart. Only an installed copy checks; a build run from bin/ or a loose exe stays quiet.
/// The request carries nothing about the user: no account, no id, no reading.
/// </summary>
public sealed class Updater : IDisposable
{
    public const string Repository = "https://github.com/r4yb3l/tokendial";
    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly UpdateManager manager = new(new GithubSource(Repository, null, prerelease: false));
    private readonly Func<bool> enabled;
    private readonly DispatcherTimer timer;
    private VelopackAsset? ready;
    private bool checking;

    public Updater(Func<bool> enabled, Dispatcher dispatcher)
    {
        this.enabled = enabled;
        timer = new DispatcherTimer(FirstCheck, DispatcherPriority.Background, async (_, _) => await CheckAsync(), dispatcher);
        timer.Stop();
    }

    /// <summary>The version downloaded and waiting for a restart, or null.</summary>
    public string? ReadyVersion => ready?.Version.ToString();

    public event Action? Changed;

    public void Start()
    {
        if (!manager.IsInstalled)
        {
            Log.Ui.Info("updates: not an installed copy, no check");
            return;
        }
        timer.Start();
    }

    private async Task CheckAsync()
    {
        timer.Interval = Interval;
        if (checking || ready is not null || !enabled()) return;
        checking = true;
        try
        {
            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                Log.Ui.Debug("updates: up to date");
                return;
            }
            await manager.DownloadUpdatesAsync(update);
            ready = update.TargetFullRelease;
            Log.Ui.Info($"updates: {ready.Version} downloaded, waiting for a restart");
            Changed?.Invoke();
        }
        catch (Exception error)
        {
            Log.Ui.Warn($"updates: check failed ({error.GetType().Name})");
        }
        finally { checking = false; }
    }

    /// <summary>Quits, applies the downloaded release and starts the new version. Only the user calls this, from the tray.</summary>
    public void RestartToUpdate()
    {
        if (ready is null) return;
        Log.Ui.Info($"updates: restarting into {ready.Version}");
        manager.ApplyUpdatesAndRestart(ready);
    }

    public void Dispose() => timer.Stop();
}
