using Tokendial.Core.Model;

namespace Tokendial.Tests;

public class ForecastTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 8, 14, 0, 0, TimeSpan.Zero);

    private static List<UsageSample> Ramp(double startFraction, double perMinute, int minutes, int step = 1, DateTimeOffset? resetsAt = null)
    {
        var samples = new List<UsageSample>();
        for (var m = 0; m <= minutes; m += step) samples.Add(new UsageSample(T0.AddMinutes(m), startFraction + perMinute * m, resetsAt));
        return samples;
    }

    [Fact]
    public void ASteadyClimbRunsOutAtTheExpectedTime()
    {
        var reset = T0.AddHours(3);
        var samples = Ramp(0.40, 0.01, 20, resetsAt: reset);
        var forecast = Forecast.For(samples, T0.AddMinutes(20), reset);
        Assert.NotNull(forecast);
        Assert.Equal(ForecastKind.RunsOut, forecast.Kind);
        Assert.Equal(T0.AddMinutes(60), forecast.RunsOutAt!.Value, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ASlowClimbLastsUntilTheReset()
    {
        var reset = T0.AddMinutes(40);
        var samples = Ramp(0.40, 0.01, 20, resetsAt: reset);
        var forecast = Forecast.For(samples, T0.AddMinutes(20), reset);
        Assert.Equal(ForecastKind.LastsUntilReset, forecast?.Kind);
        Assert.Null(forecast?.RunsOutAt);
    }

    [Fact]
    public void AFlatWindowHasNoForecast()
    {
        var samples = Ramp(0.40, 0, 30, step: 5);
        Assert.Null(Forecast.For(samples, T0.AddMinutes(30), T0.AddHours(2)));
    }

    [Fact]
    public void TooFewOrTooCloseSamplesHaveNoForecast()
    {
        Assert.Null(Forecast.For(Ramp(0.40, 0.01, 2), T0.AddMinutes(2), T0.AddHours(2)));
        Assert.Null(Forecast.For(Ramp(0.40, 0.01, 8), T0.AddMinutes(8), T0.AddHours(2)));
    }

    [Fact]
    public void OldSamplesAreIgnored()
    {
        var samples = Ramp(0.10, 0.01, 30);
        Assert.Null(Forecast.For(samples, T0.AddMinutes(30 + 61), T0.AddHours(3)));
    }

    [Fact]
    public void AFullWindowHasNoForecast()
    {
        var samples = Ramp(0.80, 0.01, 20);
        Assert.Null(Forecast.For(samples, T0.AddMinutes(20), T0.AddHours(3)));
    }

    [Fact]
    public void WithoutAResetOnlyTheNextDayCounts()
    {
        Assert.Equal(ForecastKind.RunsOut, Forecast.For(Ramp(0.40, 0.01, 20), T0.AddMinutes(20), null)?.Kind);
        Assert.Null(Forecast.For(Ramp(0.40, 0.0001, 20), T0.AddMinutes(20), null));
    }

    [Fact]
    public void IntegerPercentagesStillGiveASlope()
    {
        var samples = new List<UsageSample>();
        for (var m = 0; m <= 20; m++) samples.Add(new UsageSample(T0.AddMinutes(m), Math.Floor((0.40 + 0.01 * m) * 100) / 100, null));
        var slope = Forecast.SlopePerHour(samples);
        Assert.InRange(slope, 0.55, 0.65);
    }
}

public class UsageHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 8, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void KeepsSamplesOfOneEpochAndTrimsByAgeAndCount()
    {
        var history = new UsageHistory();
        for (var m = 0; m < 400; m++) history.Add("claude", "session", new UsageSample(T0.AddMinutes(m), Math.Min(0.99, m / 500.0), T0.AddHours(8)));
        var samples = history.Samples("claude", "session");
        Assert.True(samples.Count <= UsageHistory.MaxSamples);
        Assert.True(samples.All(s => T0.AddMinutes(399) - s.TakenAt <= UsageHistory.MaxAge));
    }

    [Fact]
    public void AMovedResetStartsANewEpoch()
    {
        var history = new UsageHistory();
        history.Add("claude", "session", new UsageSample(T0, 0.9, T0.AddHours(1)));
        history.Add("claude", "session", new UsageSample(T0.AddMinutes(1), 0.95, T0.AddHours(1).AddSeconds(30)));
        Assert.Equal(2, history.Samples("claude", "session").Count);
        history.Add("claude", "session", new UsageSample(T0.AddMinutes(2), 0.02, T0.AddHours(6)));
        Assert.Single(history.Samples("claude", "session"));
    }

    [Fact]
    public void ADropWithoutAResetStartsANewEpoch()
    {
        var history = new UsageHistory();
        history.Add("antigravity", "requests", new UsageSample(T0, 0.6, null));
        history.Add("antigravity", "requests", new UsageSample(T0.AddMinutes(1), 0.55, null));
        Assert.Equal(2, history.Samples("antigravity", "requests").Count);
        history.Add("antigravity", "requests", new UsageSample(T0.AddMinutes(2), 0.1, null));
        Assert.Single(history.Samples("antigravity", "requests"));
    }

    [Fact]
    public void RecordsOnlyWindowsWithAFraction()
    {
        var history = new UsageHistory();
        var reading = new ProviderReading("codex", "Codex", Fidelity.Official, ReadingStatus.LiveNow,
            [new UsageWindow("primary", "5h limit", 0.3, ResetsAt: T0.AddHours(2)), new UsageWindow("count", "Requests", Count: 7)]);
        history.Record(reading, T0);
        Assert.Single(history.Samples("codex", "primary"));
        Assert.Empty(history.Samples("codex", "count"));
        history.Forget("codex");
        Assert.Empty(history.Samples("codex", "primary"));
    }
}
