namespace Tokendial.Core.Model;

public enum ForecastKind
{
    /// <summary>At this pace the window empties before it resets.</summary>
    RunsOut,
    /// <summary>At this pace the window lasts until its reset.</summary>
    LastsUntilReset
}

/// <summary>
/// Where the current pace takes a window. A least-squares slope over the recent samples of one epoch, because
/// vendors publish whole percentages and two neighbouring samples read as a flat line or a cliff.
/// </summary>
public sealed record Forecast(ForecastKind Kind, DateTimeOffset? RunsOutAt)
{
    public static readonly TimeSpan MinSpan = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Lookback = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan Horizon = TimeSpan.FromHours(24);
    /// <summary>Half a percentage point an hour: anything slower is a resting window, not a pace.</summary>
    public const double MinSlopePerHour = 0.005;

    public static Forecast? For(IReadOnlyList<UsageSample> samples, DateTimeOffset now, DateTimeOffset? resetsAt)
    {
        var recent = samples.Where(s => now - s.TakenAt <= Lookback && s.TakenAt <= now).OrderBy(s => s.TakenAt).ToList();
        if (recent.Count < 3) return null;
        var first = recent[0];
        var last = recent[^1];
        if (last.TakenAt - first.TakenAt < MinSpan) return null;
        if (last.UsedFraction >= 1) return null;

        var slope = SlopePerHour(recent);
        if (!double.IsFinite(slope) || slope <= MinSlopePerHour) return null;
        var hoursLeft = (1 - last.UsedFraction) / slope;
        if (!double.IsFinite(hoursLeft) || hoursLeft < 0) return null;
        var runsOutAt = last.TakenAt + TimeSpan.FromHours(hoursLeft);

        var reset = resetsAt is DateTimeOffset r && r > now ? r : (DateTimeOffset?)null;
        if (reset is DateTimeOffset at) return runsOutAt < at ? new Forecast(ForecastKind.RunsOut, runsOutAt) : new Forecast(ForecastKind.LastsUntilReset, null);
        return runsOutAt - now <= Horizon ? new Forecast(ForecastKind.RunsOut, runsOutAt) : null;
    }

    /// <summary>Ordinary least squares of fraction against hours since the first sample.</summary>
    public static double SlopePerHour(IReadOnlyList<UsageSample> samples)
    {
        var origin = samples[0].TakenAt;
        var xs = samples.Select(s => (s.TakenAt - origin).TotalHours).ToArray();
        var ys = samples.Select(s => s.UsedFraction).ToArray();
        var meanX = xs.Average();
        var meanY = ys.Average();
        var covariance = xs.Zip(ys, (x, y) => (x - meanX) * (y - meanY)).Sum();
        var variance = xs.Sum(x => (x - meanX) * (x - meanX));
        return variance == 0 ? double.NaN : covariance / variance;
    }
}
