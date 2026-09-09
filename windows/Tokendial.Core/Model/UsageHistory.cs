namespace Tokendial.Core.Model;

/// <summary>One reading of one window, kept only long enough to see which way it is heading.</summary>
public sealed record UsageSample(DateTimeOffset TakenAt, double UsedFraction, DateTimeOffset? ResetsAt);

/// <summary>
/// A short ring of samples per provider window, in memory only. A window starts a new ring when its reset moves
/// or its fraction falls, which is how a rollover looks from the outside; the ring never outlives the epoch.
/// </summary>
public sealed class UsageHistory
{
    public const int MaxSamples = 180;
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(3);
    public static readonly TimeSpan ResetTolerance = TimeSpan.FromSeconds(60);
    public const double DropThatEndsAnEpoch = 0.10;

    private readonly Dictionary<(string Provider, string Window), List<UsageSample>> series = new();

    /// <summary>One sample per window that carries a fraction; count-only windows are not forecastable.</summary>
    public void Record(ProviderReading reading, DateTimeOffset now)
    {
        foreach (var window in reading.Windows)
        {
            if (window.UsedFraction is double fraction) Add(reading.ProviderId, window.Id, new UsageSample(now, fraction, window.ResetsAt));
        }
    }

    public void Add(string providerId, string windowId, UsageSample sample)
    {
        if (!series.TryGetValue((providerId, windowId), out var list)) series[(providerId, windowId)] = list = new List<UsageSample>();
        if (list.Count > 0 && !SameEpoch(list[^1], sample)) list.Clear();
        list.Add(sample);
        list.RemoveAll(s => sample.TakenAt - s.TakenAt > MaxAge);
        if (list.Count > MaxSamples) list.RemoveRange(0, list.Count - MaxSamples);
    }

    public IReadOnlyList<UsageSample> Samples(string providerId, string windowId) =>
        series.TryGetValue((providerId, windowId), out var list) ? list.ToArray() : [];

    public void Forget(string providerId)
    {
        foreach (var key in series.Keys.Where(k => k.Provider == providerId).ToList()) series.Remove(key);
    }

    /// <summary>The same epoch as long as the reset stays put (within the same tolerance the alert engine uses) and usage did not fall.</summary>
    public static bool SameEpoch(UsageSample previous, UsageSample next) =>
        SameReset(previous.ResetsAt, next.ResetsAt) && previous.UsedFraction - next.UsedFraction <= DropThatEndsAnEpoch;

    private static bool SameReset(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b is null : b is not null && (a.Value - b.Value).Duration() <= ResetTolerance;
}
