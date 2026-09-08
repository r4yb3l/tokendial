namespace Tokendial.Core.Install;

public enum InstallOutcome
{
    /// <summary>The tool signed in; the provider can be connected.</summary>
    Success,
    /// <summary>Nothing signed in within the wait.</summary>
    TimedOut,
    /// <summary>The terminal closed before a sign-in appeared.</summary>
    TerminalClosed
}

/// <summary>
/// Follows one provider after its terminal opened: not installed → installed → signed in, on a clock,
/// and says how it ended. Thread-agnostic like the store, with no timer of its own in tests; it touches
/// neither settings nor the store, it only raises events.
/// </summary>
public sealed class InstallWatcher : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(3);

    private readonly object gate = new();
    private readonly Func<bool> installed;
    private readonly Func<bool> signedIn;
    private readonly Func<DateTimeOffset> now;
    private readonly TimeSpan timeout;
    private readonly bool waitsAfterTerminal;
    private readonly DateTimeOffset started;
    private Timer? timer;
    private bool done;

    /// <param name="waitsAfterTerminal">An app signs in on its own later, so closing the terminal does not end the wait once it is installed.</param>
    public InstallWatcher(string providerId, Func<bool> installed, Func<bool> signedIn, bool waitsAfterTerminal = false, Func<DateTimeOffset>? now = null, TimeSpan? timeout = null)
    {
        ProviderId = providerId;
        this.installed = installed;
        this.signedIn = signedIn;
        this.waitsAfterTerminal = waitsAfterTerminal;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.timeout = timeout ?? DefaultTimeout;
        started = this.now();
        State = installed() ? InstallState.Installed : InstallState.NotInstalled;
    }

    public string ProviderId { get; }
    public InstallState State { get; private set; }
    public InstallOutcome? Outcome { get; private set; }
    public bool Running => Outcome is null;

    public event Action<InstallState>? Advanced;
    public event Action<InstallOutcome>? Completed;

    public void Start(TimeSpan? interval = null)
    {
        var every = interval ?? DefaultInterval;
        timer = new Timer(_ => Tick(), null, every, every);
    }

    public void Tick()
    {
        InstallState? advanced = null;
        InstallOutcome? outcome = null;
        lock (gate)
        {
            if (done) return;
            if (signedIn())
            {
                State = InstallState.SignedIn;
                advanced = State;
                outcome = InstallOutcome.Success;
            }
            else
            {
                if (State == InstallState.NotInstalled && installed())
                {
                    State = InstallState.Installed;
                    advanced = State;
                }
                if (now() - started >= timeout) outcome = InstallOutcome.TimedOut;
            }
            if (outcome is not null) Finish(outcome.Value);
        }
        if (advanced is InstallState state) Advanced?.Invoke(state);
        if (outcome is InstallOutcome ended) Completed?.Invoke(ended);
    }

    /// <summary>The terminal window closed. A CLI signs in inside it, so closing it early ends the wait; an installed app signs in on its own later, so the wait goes on.</summary>
    public void TerminalClosed()
    {
        Tick();
        bool ended;
        lock (gate)
        {
            ended = !done && !(waitsAfterTerminal && State == InstallState.Installed);
            if (ended) Finish(InstallOutcome.TerminalClosed);
        }
        if (ended) Completed?.Invoke(InstallOutcome.TerminalClosed);
    }

    private void Finish(InstallOutcome outcome)
    {
        done = true;
        Outcome = outcome;
        timer?.Dispose();
        timer = null;
    }

    public void Dispose()
    {
        lock (gate)
        {
            done = true;
            timer?.Dispose();
            timer = null;
        }
    }
}
