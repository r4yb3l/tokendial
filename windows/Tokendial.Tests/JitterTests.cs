using Tokendial.Core.Alerts;

namespace Tokendial.Tests;

/// <summary>The exact sequence seen live on 2026-09-08: a 50 % threshold fired, the app restarted, and Claude's resets_at drifted by half a second.</summary>
public class JitterTests
{
    private static readonly DateTimeOffset First = DateTimeOffset.Parse("2026-09-08T18:09:59.571904+00:00");
    private static readonly DateTimeOffset Second = DateTimeOffset.Parse("2026-09-08T18:10:00.156089+00:00");

    [Fact]
    public void DriftAcrossARestartDoesNotRefire()
    {
        var t0 = DateTimeOffset.Parse("2026-09-08T15:02:00Z");
        var engine = new AlertEngine();
        var first = engine.Reduce(new AlertEvent.Usage(t0, "claude", "session", 52, First));
        Assert.Single(first);

        var reloaded = AlertEngine.Load(engine.Save());
        Assert.Empty(reloaded.Reduce(new AlertEvent.Restart(t0.AddMinutes(4))));
        Assert.Empty(reloaded.Reduce(new AlertEvent.Usage(t0.AddMinutes(4), "claude", "session", 52, First)));
        Assert.Empty(reloaded.Reduce(new AlertEvent.Usage(t0.AddMinutes(5), "claude", "session", 54, Second)));
    }

    [Fact]
    public void DriftWithinARunDoesNotRefire()
    {
        var t0 = DateTimeOffset.Parse("2026-09-08T15:02:00Z");
        var engine = new AlertEngine();
        Assert.Single(engine.Reduce(new AlertEvent.Usage(t0, "claude", "session", 52, First)));
        Assert.Empty(engine.Reduce(new AlertEvent.Usage(t0.AddMinutes(2), "claude", "session", 53, Second)));
        Assert.Empty(engine.Reduce(new AlertEvent.Usage(t0.AddMinutes(4), "claude", "session", 54, First.AddSeconds(30))));
    }
}
