namespace Tokendial.Core.Sessions;

public enum SessionState
{
    Working,
    Waiting,
    Idle
}

/// <summary>One agent session, in display terms; each monitor parses its own tool's format.</summary>
public sealed record AgentSession(string Id, string Name, string Where, SessionState State, string? WaitingFor, DateTimeOffset Since);

/// <summary>What a provider's sessions add up to. Waiting outranks working: it is the one state that wants something from you.</summary>
public sealed record Activity(SessionState State, IReadOnlyList<AgentSession> Sessions)
{
    public static Activity? Of(IReadOnlyList<AgentSession> sessions)
    {
        if (sessions.Count == 0) return null;
        var state = sessions.Any(s => s.State == SessionState.Waiting) ? SessionState.Waiting
            : sessions.Any(s => s.State == SessionState.Working) ? SessionState.Working
            : SessionState.Idle;
        return new Activity(state, sessions);
    }

    /// <summary>Waiting, then working, then idle; newest first within each.</summary>
    public IReadOnlyList<AgentSession> Ordered =>
        Sessions.OrderBy(s => s.State switch { SessionState.Waiting => 0, SessionState.Working => 1, _ => 2 }).ThenByDescending(s => s.Since).ToList();
}

/// <summary>Publishes one tool's live sessions from a background thread.</summary>
public interface IActivityMonitor : IDisposable
{
    string ProviderId { get; }
    IReadOnlyList<AgentSession> Sessions { get; }
    event Action? Changed;
    void Start();
}

/// <summary>Re-reads a source on a timer and publishes only real changes.</summary>
public abstract class PolledMonitor : IActivityMonitor
{
    private readonly object gate = new();
    private readonly TimeSpan interval;
    private Timer? timer;
    private bool reading;
    private IReadOnlyList<AgentSession> sessions = [];

    protected PolledMonitor(string providerId, TimeSpan interval)
    {
        ProviderId = providerId;
        this.interval = interval;
    }

    public string ProviderId { get; }
    public IReadOnlyList<AgentSession> Sessions { get { lock (gate) return sessions; } }
    public event Action? Changed;

    public void Start()
    {
        Poll();
        timer = new Timer(_ => Poll(), null, interval, interval);
    }

    public void Dispose() => timer?.Dispose();

    protected abstract IReadOnlyList<AgentSession> Read();

    protected void Poll()
    {
        lock (gate)
        {
            if (reading) return;
            reading = true;
        }
        try
        {
            var found = Read();
            bool changed;
            lock (gate)
            {
                changed = !found.SequenceEqual(sessions);
                if (changed) sessions = found;
            }
            if (changed) Changed?.Invoke();
        }
        catch (Exception error)
        {
            Diagnostics.Log.Sessions.Error($"{GetType().Name}: {error.Message}");
        }
        finally
        {
            lock (gate) reading = false;
        }
    }
}

/// <summary>Is a pid alive, and still the same process? Pids get reused; start times settle it.</summary>
public static class Liveness
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static bool IsAlive(int pid, DateTimeOffset? startedAt)
    {
        System.Diagnostics.Process process;
        try { process = System.Diagnostics.Process.GetProcessById(pid); }
        catch (Exception) { return false; }
        using (process)
        {
            if (startedAt is null) return true;
            try
            {
                var actual = new DateTimeOffset(process.StartTime.ToUniversalTime());
                return (actual - startedAt.Value).Duration() < Tolerance;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
