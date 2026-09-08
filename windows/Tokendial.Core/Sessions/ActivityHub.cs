namespace Tokendial.Core.Sessions;

/// <summary>Every monitor's sessions, keyed by provider, with one change event for the whole set.</summary>
public sealed class ActivityHub : IDisposable
{
    private readonly IReadOnlyList<IActivityMonitor> monitors;
    private readonly object gate = new();
    private Dictionary<string, Activity> activities = new();

    public ActivityHub(IReadOnlyList<IActivityMonitor> monitors)
    {
        this.monitors = monitors;
        foreach (var monitor in monitors) monitor.Changed += () => Refresh(monitor);
    }

    public event Action? Changed;

    public IReadOnlyDictionary<string, Activity> Activities { get { lock (gate) return new Dictionary<string, Activity>(activities); } }

    public Activity? For(string providerId) { lock (gate) return activities.GetValueOrDefault(providerId); }

    public bool AnyWorking { get { lock (gate) return activities.Values.Any(a => a.State == SessionState.Working); } }

    public IReadOnlyList<AgentSession> AllSessions { get { lock (gate) return activities.Values.SelectMany(a => a.Ordered).ToList(); } }

    public void Start()
    {
        foreach (var monitor in monitors) monitor.Start();
    }

    private void Refresh(IActivityMonitor monitor)
    {
        lock (gate)
        {
            var activity = Activity.Of(monitor.Sessions);
            if (activity is null) activities.Remove(monitor.ProviderId);
            else activities[monitor.ProviderId] = activity;
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        foreach (var monitor in monitors) monitor.Dispose();
    }
}
