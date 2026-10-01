using Tokendial.Core.Diagnostics;
using Tokendial.Core.Model;
using Tokendial.Core.Sessions;

namespace Tokendial.Core.Alerts;

/// <summary>
/// The host side of the alert engine: turns readings and sessions into events,
/// runs the reducer under one lock, persists its state, and hands alerts to the
/// sink. The kinds the user switched off are the engine's to drop, before they
/// can hold or start a cooldown. Time comes from a clock the tests can drive.
/// </summary>
public sealed class AlertCoordinator : IDisposable
{
    private readonly object gate = new();
    private readonly IAlertSink sink;
    private readonly Func<DateTimeOffset> now;
    private readonly string? stateFile;
    private AlertEngine engine;
    private Timer? ticker;

    public AlertCoordinator(IAlertSink sink, AlertConfig? config = null, string? stateFile = null, Func<DateTimeOffset>? now = null, Func<AlertKind, bool>? wants = null)
    {
        this.sink = sink;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.stateFile = stateFile;
        engine = LoadEngine(stateFile, config ?? AlertConfig.Default, wants);
        Log.Alerts.Info($"state: {engine.EpochCount} epoch(s) from {(stateFile is null ? "memory" : Path.GetFileName(stateFile))}");
        Apply(new AlertEvent.Restart(this.now()));
    }

    public static string DefaultStateFile => Paths.In("alerts.json");

    public IReadOnlyList<Alert> Delivered { get { lock (gate) return delivered.ToArray(); } }
    private readonly List<Alert> delivered = new();

    public void Start(TimeSpan? tick = null)
    {
        var period = tick ?? TimeSpan.FromSeconds(15);
        ticker = new Timer(_ => Apply(new AlertEvent.Tick(now())), null, period, period);
    }

    /// <summary>Thresholds or switches changed: the engine keeps its epochs and only the rules move.</summary>
    public void Reconfigure(AlertConfig config, Func<AlertKind, bool> wants)
    {
        lock (gate) engine = AlertEngine.Load(engine.Save(), config, wants);
    }

    public void OnReadings(IEnumerable<ProviderReading> readings)
    {
        var at = now();
        foreach (var reading in readings)
        {
            if (reading.Status is not ReadingStatus.Live) continue;
            if (reading.Headline is not UsageWindow window || window.UsedFraction is not double used) continue;
            Apply(new AlertEvent.Usage(at, reading.ProviderId, window.Id, Math.Round(used * 100, 2), window.ResetsAt, reading.Block is not null));
        }
    }

    /// <summary>
    /// One snapshot of every session, applied whole under the lock. Linux calls this from each monitor's own
    /// timer thread; applied piecemeal, one snapshot could close a session another had just reported waiting.
    /// </summary>
    public void OnActivities(IReadOnlyDictionary<string, Activity> activities)
    {
        var alerts = new List<Alert>();
        lock (gate)
        {
            var at = now();
            var seen = new HashSet<string>();
            foreach (var (provider, activity) in activities)
            {
                foreach (var session in activity.Sessions)
                {
                    seen.Add(session.Id);
                    alerts.AddRange(Reduce(new AlertEvent.Session(at, provider, session.Id, session.State switch
                    {
                        SessionState.Waiting => SessionActivity.Waiting,
                        SessionState.Working => SessionActivity.Working,
                        _ => SessionActivity.Done
                    })));
                }
            }
            foreach (var (provider, id) in engine.Waiting.Where(w => !seen.Contains(w.SessionId)))
                alerts.AddRange(Reduce(new AlertEvent.Session(at, provider, id, SessionActivity.Done)));
        }
        Deliver(alerts);
    }

    public void OnHover(bool on) => Apply(new AlertEvent.Hover(now(), on));

    public void Tick() => Apply(new AlertEvent.Tick(now()));

    private void Apply(AlertEvent e)
    {
        IReadOnlyList<Alert> alerts;
        lock (gate) alerts = Reduce(e);
        Deliver(alerts);
    }

    /// <summary>Runs one event through the engine and persists what it changed. The caller holds the lock.</summary>
    private IReadOnlyList<Alert> Reduce(AlertEvent e)
    {
        var alerts = engine.Reduce(e);
        if (alerts.Count > 0 || e is AlertEvent.Usage or AlertEvent.Restart) Persist();
        delivered.AddRange(alerts);
        if (delivered.Count > 200) delivered.RemoveRange(0, delivered.Count - 200);
        return alerts;
    }

    /// <summary>Outside the lock, so a slow notification centre never stalls the next event.</summary>
    private void Deliver(IEnumerable<Alert> alerts)
    {
        foreach (var alert in alerts)
        {
            Log.Alerts.Info($"{alert.Kind} {alert.Provider}{(alert.Window is null ? "" : "/" + alert.Window)}{(alert.Pct is null ? "" : $" {alert.Pct}%")}");
            try { sink.Deliver(alert); }
            catch (Exception error) { Log.Alerts.Error($"sink: {error.Message}"); }
        }
    }

    private void Persist()
    {
        if (stateFile is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
            var temp = stateFile + ".tmp";
            File.WriteAllText(temp, engine.Save());
            File.Move(temp, stateFile, overwrite: true);
        }
        catch (Exception error) { Log.Alerts.Error($"persist: {error.Message}"); }
    }

    private static AlertEngine LoadEngine(string? file, AlertConfig config, Func<AlertKind, bool>? wants)
    {
        try
        {
            if (file is not null && File.Exists(file)) return AlertEngine.Load(File.ReadAllText(file), config, wants);
        }
        catch (Exception error) { Log.Alerts.Error($"load: {error.Message}"); }
        return new AlertEngine(config, wants: wants);
    }

    public void Dispose() => ticker?.Dispose();
}
