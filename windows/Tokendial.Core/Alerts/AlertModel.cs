namespace Tokendial.Core.Alerts;

/// <summary>Tunable rules of the alert engine. Defaults match docs/alerts/spec.md.</summary>
public sealed record AlertConfig(
    IReadOnlyList<int> Thresholds,
    TimeSpan ResetLead,
    int ResetLeadMinPct,
    TimeSpan WaitingDebounce,
    TimeSpan WaitingRepeat,
    TimeSpan PerProviderCooldown)
{
    public static readonly AlertConfig Default = new(
        [50, 80, 95],
        TimeSpan.FromSeconds(600),
        50,
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(300),
        TimeSpan.FromSeconds(60));
}

public enum SessionActivity
{
    Working,
    Waiting,
    Done
}

/// <summary>What the host tells the engine. Time is always explicit.</summary>
public abstract record AlertEvent(DateTimeOffset At)
{
    public sealed record Usage(DateTimeOffset At, string Provider, string Window, double UsedPct, DateTimeOffset? ResetsAt, bool Limited = false) : AlertEvent(At);
    public sealed record Session(DateTimeOffset At, string Provider, string SessionId, SessionActivity State) : AlertEvent(At);
    public sealed record Hover(DateTimeOffset At, bool On) : AlertEvent(At);
    public sealed record Tick(DateTimeOffset At) : AlertEvent(At);
    public sealed record Restart(DateTimeOffset At) : AlertEvent(At);
}

/// <summary>Declared in emission order: within one reduction, limits go out before thresholds, and so on.</summary>
public enum AlertKind
{
    Limit,
    Threshold,
    ResetSoon,
    ResetDone,
    Waiting
}

/// <summary>One notification the host should show.</summary>
public sealed record Alert(
    AlertKind Kind,
    string Provider,
    string? Window = null,
    int? Pct = null,
    DateTimeOffset? ResetsAt = null,
    string? SessionId = null);

/// <summary>The platform's notification centre. Contains no logic.</summary>
public interface IAlertSink
{
    void Deliver(Alert alert);
}
