using Tokendial.Core.Alerts;
using Tokendial.Core.Model;
using Tokendial.Core.Sessions;
using Tokendial.Core.Settings;

namespace Tokendial.Tests;

public class CoordinatorTests
{
    private sealed class RecordingSink : IAlertSink
    {
        public List<Alert> Alerts { get; } = new();
        public void Deliver(Alert alert) => Alerts.Add(alert);
    }

    private static ProviderReading Reading(double used, DateTimeOffset resetsAt, bool blocked = false) =>
        new("claude", "Claude Code", Fidelity.Official, ReadingStatus.LiveNow,
            [new UsageWindow("session", "Current session", used, ResetsAt: resetsAt), new UsageWindow("weekly_all", "All models", 0.1, ResetsAt: resetsAt.AddDays(6))],
            "session", blocked ? new Blocked("limit", resetsAt) : null);

    [Fact]
    public void ThresholdAlertsFlowFromReadingsAndSurviveRestart()
    {
        var clock = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var file = Path.Combine(Path.GetTempPath(), $"tokendial-{Guid.NewGuid():N}.json");
        var sink = new RecordingSink();
        var resetsAt = clock.AddHours(3);
        try
        {
            using (var coordinator = new AlertCoordinator(sink, stateFile: file, now: () => clock))
            {
                coordinator.OnReadings([Reading(0.42, resetsAt)]);
                clock = clock.AddMinutes(1);
                coordinator.OnReadings([Reading(0.55, resetsAt)]);
                Assert.Single(sink.Alerts);
                Assert.Equal(AlertKind.Threshold, sink.Alerts[0].Kind);
                Assert.Equal(50, sink.Alerts[0].Pct);
            }
            clock = clock.AddMinutes(2);
            using (var coordinator = new AlertCoordinator(sink, stateFile: file, now: () => clock))
            {
                coordinator.OnReadings([Reading(0.56, resetsAt)]);
                Assert.Single(sink.Alerts);
                clock = clock.AddMinutes(1);
                coordinator.OnReadings([Reading(0.83, resetsAt)]);
                Assert.Equal(2, sink.Alerts.Count);
                Assert.Equal(80, sink.Alerts[1].Pct);
            }
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void SwitchedOffKindsAreNeverDelivered()
    {
        var clock = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var sink = new RecordingSink();
        var settings = new Settings { AlertThresholds = false };
        using var coordinator = new AlertCoordinator(sink, settings.AlertConfig, now: () => clock, wants: settings.Wants);
        coordinator.OnReadings([Reading(0.10, clock.AddHours(1))]);
        clock = clock.AddMinutes(1);
        coordinator.OnReadings([Reading(0.96, clock.AddHours(1))]);
        Assert.Empty(sink.Alerts);
        clock = clock.AddMinutes(2);
        coordinator.OnReadings([Reading(1.0, clock.AddHours(1), blocked: true)]);
        Assert.Single(sink.Alerts);
        Assert.Equal(AlertKind.Limit, sink.Alerts[0].Kind);
    }

    [Fact]
    public void WaitingSessionAlertsAfterDebounceAndStopsWhenGone()
    {
        var clock = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var sink = new RecordingSink();
        using var coordinator = new AlertCoordinator(sink, now: () => clock);
        var waiting = new Dictionary<string, Activity>
        {
            ["claude"] = new(SessionState.Waiting, [new AgentSession("claude.1", "repo", "Terminal · repo", SessionState.Waiting, "needs you", clock)])
        };
        coordinator.OnActivities(waiting);
        clock = clock.AddSeconds(10);
        coordinator.Tick();
        Assert.Empty(sink.Alerts);
        clock = clock.AddSeconds(15);
        coordinator.Tick();
        Assert.Single(sink.Alerts);
        Assert.Equal(AlertKind.Waiting, sink.Alerts[0].Kind);
        Assert.Equal("claude.1", sink.Alerts[0].SessionId);

        coordinator.OnActivities(new Dictionary<string, Activity>());
        clock = clock.AddMinutes(10);
        coordinator.Tick();
        Assert.Single(sink.Alerts);
    }

    [Fact]
    public void AWaitingSessionThatVanishedAcrossARestartIsClosed()
    {
        var clock = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var file = Path.Combine(Path.GetTempPath(), $"tokendial-{Guid.NewGuid():N}.json");
        var sink = new RecordingSink();
        try
        {
            using (var coordinator = new AlertCoordinator(sink, stateFile: file, now: () => clock))
            {
                coordinator.OnActivities(new Dictionary<string, Activity>
                {
                    ["claude"] = new(SessionState.Waiting, [new AgentSession("claude.1", "repo", "Terminal · repo", SessionState.Waiting, "needs you", clock)])
                });
            }
            clock = clock.AddSeconds(5);
            using (var coordinator = new AlertCoordinator(sink, stateFile: file, now: () => clock))
            {
                coordinator.OnActivities(new Dictionary<string, Activity>());
                clock = clock.AddSeconds(30);
                coordinator.Tick();
                Assert.Empty(sink.Alerts);
            }
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        var file = Path.Combine(Path.GetTempPath(), $"tokendial-settings-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new Settings { Panel = PanelMode.AlwaysExpanded, Thresholds = [60, 90], LaunchAtLogin = true };
            settings.Disconnected.Add("grok");
            settings.Save(file);
            var loaded = Settings.Load(file);
            Assert.Equal(PanelMode.AlwaysExpanded, loaded.Panel);
            Assert.Equal([60, 90], loaded.AlertConfig.Thresholds);
            Assert.True(loaded.LaunchAtLogin);
            Assert.Contains("grok", loaded.Disconnected);
            Assert.Equal(new Settings().AlertConfig.ResetLead, loaded.AlertConfig.ResetLead);
        }
        finally { File.Delete(file); }
    }
}
