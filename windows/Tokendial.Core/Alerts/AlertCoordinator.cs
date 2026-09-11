using Tokendial.Core.Diagnostics;
using Tokendial.Core.Model;
using Tokendial.Core.Sessions;

namespace Tokendial.Core.Alerts;

/// <summary>
/// The host side of the alert engine: turns readings and sessions into events,
/// runs the reducer under one lock, persists its state, and hands alerts to the
/// sink after filtering the kinds the user switched off. Time comes from a clock
/// the tests can drive.
/// </summary>
public sealed class AlertCoordinator : IDisposable
{
    private readonly object gate = new();
    private readonly IAlertSink sink;
    private readonly Func<DateTimeOffset> now;
    private readonly string? stateFile;
    private AlertEngine engine;
    private Func<AlertKind, bool> wants;
    private Timer? ticker;

    public AlertCoordinator(IAlertSink sink, AlertConfig? config = null, string? stateFile = null, Func<DateTimeOffset>? now = null, Func<AlertKind, bool>? wants = null)
    {
        this.sink = sink;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.stateFile = stateFile;
        this.wants = wants ?? (_ => true);
        engine = LoadEngine(stateFile, config ?? AlertConfig.Default);
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
        lock (gate)
        {
            engine = AlertEngine.Load(engine.Save(), config);
            this.wants = wants;
        }
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

    public void OnActivities(IReadOnlyDictionary<string, Activity> activities)
    {
        var at = now();
        var seen = new HashSet<string>();
        foreach (var (provider, activity) in activities)
        {
            foreach (var session in activity.Sessions)
            {
                seen.Add(session.Id);
                Apply(new AlertEvent.Session(at, provider, session.Id, session.State switch
                {
                    SessionState.Waiting => SessionActivity.Waiting,
                    SessionState.Working => SessionActivity.Working,
                    _ => SessionActivity.Done
                }));
            }
        }
        IReadOnlyList<(string Provider, string SessionId)> waiting;
        lock (gate) waiting = engine.Waiting;
        foreach (var (provider, id) in waiting.Where(w => !seen.Contains(w.SessionId)))
            Apply(new AlertEvent.Session(at, provider, id, SessionActivity.Done));
    }

    public void OnHover(bool on) => Apply(new AlertEvent.Hover(now(), on));

    public void Tick() => Apply(new AlertEvent.Tick(now()));

    private void Apply(AlertEvent e)
    {
        List<Alert> alerts;
        lock (gate)
        {
            alerts = engine.Reduce(e).Where(a => wants(a.Kind)).ToList();
            if (alerts.Count > 0 || e is AlertEvent.Usage or AlertEvent.Restart) Persist();
            delivered.AddRange(alerts);
            if (delivered.Count > 200) delivered.RemoveRange(0, delivered.Count - 200);
        }
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

    private static AlertEngine LoadEngine(string? file, AlertConfig config)
    {
        try
        {
            if (file is not null && File.Exists(file)) return AlertEngine.Load(File.ReadAllText(file), config);
        }
        catch (Exception error) { Log.Alerts.Error($"load: {error.Message}"); }
        return new AlertEngine(config);
    }

    public void Dispose() => ticker?.Dispose();
}
