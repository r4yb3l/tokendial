using System.Text.Json;
using Tokendial.Core.Alerts;

namespace Tokendial.Tests;

/// <summary>
/// Runs every conformance vector in docs/alerts/vectors. The Swift core runs the
/// same files; the two engines must emit identical sequences.
/// </summary>
public class AlertVectorTests
{
    private static readonly DateTimeOffset Base = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> Vectors() =>
        Directory.EnumerateFiles(Docs.Path("alerts", "vectors"), "*.json").OrderBy(p => p).Select(p => new object[] { Path.GetFileName(p) });

    [Theory]
    [MemberData(nameof(Vectors))]
    public void VectorPasses(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Docs.Path("alerts", "vectors", file)));
        var root = document.RootElement;
        var config = Config(root.GetProperty("config"));
        var engine = new AlertEngine(config);
        var emitted = new List<string>();

        foreach (var step in root.GetProperty("steps").EnumerateArray())
        {
            var t = step.GetProperty("t").GetDouble();
            var at = Base.AddSeconds(t);
            var eventElement = step.GetProperty("event");
            AlertEvent alertEvent = eventElement.GetProperty("kind").GetString() switch
            {
                "usage" => new AlertEvent.Usage(at, eventElement.GetProperty("provider").GetString()!, eventElement.GetProperty("window").GetString()!,
                    eventElement.GetProperty("usedPct").GetDouble(),
                    eventElement.TryGetProperty("resetsAt", out var r) && r.ValueKind == JsonValueKind.Number ? Base.AddSeconds(r.GetDouble()) : null,
                    eventElement.TryGetProperty("limited", out var l) && l.ValueKind == JsonValueKind.True),
                "session" => new AlertEvent.Session(at, eventElement.GetProperty("provider").GetString()!, eventElement.GetProperty("sessionId").GetString()!,
                    Enum.Parse<SessionActivity>(eventElement.GetProperty("state").GetString()!, ignoreCase: true)),
                "hover" => new AlertEvent.Hover(at, eventElement.GetProperty("on").GetBoolean()),
                "tick" => new AlertEvent.Tick(at),
                "restart" => new AlertEvent.Restart(at),
                var other => throw new InvalidOperationException($"unknown event {other}")
            };

            if (step.TryGetProperty("persistAndRestart", out var persist) && persist.ValueKind == JsonValueKind.True)
            {
                engine = AlertEngine.Load(engine.Save(), config);
            }

            foreach (var alert in engine.Reduce(alertEvent))
            {
                emitted.Add(Describe(t, alert));
            }
        }

        var expected = root.GetProperty("expected").EnumerateArray().Select(e =>
        {
            var kind = e.GetProperty("kind").GetString()!;
            var provider = e.GetProperty("provider").GetString()!;
            var window = e.TryGetProperty("window", out var w) ? w.GetString() : null;
            var pct = e.TryGetProperty("pct", out var p) ? p.GetInt32().ToString() : "";
            var session = e.TryGetProperty("sessionId", out var s) ? s.GetString() : null;
            return $"{e.GetProperty("t").GetDouble()} {kind} {provider} {window ?? session} {pct}".Trim();
        }).ToList();

        Assert.Equal(expected, emitted);
    }

    private static string Describe(double t, Alert alert)
    {
        var kind = alert.Kind switch
        {
            AlertKind.Threshold => "threshold",
            AlertKind.Limit => "limit",
            AlertKind.ResetSoon => "resetSoon",
            AlertKind.ResetDone => "resetDone",
            _ => "waiting"
        };
        return $"{t} {kind} {alert.Provider} {alert.Window ?? alert.SessionId} {alert.Pct?.ToString() ?? ""}".Trim();
    }

    private static AlertConfig Config(JsonElement element)
    {
        var d = AlertConfig.Default;
        int Int(string name, int fallback) => element.TryGetProperty(name, out var v) ? v.GetInt32() : fallback;
        var thresholds = element.TryGetProperty("thresholds", out var t)
            ? t.EnumerateArray().Select(x => x.GetInt32()).ToList()
            : d.Thresholds;
        return new AlertConfig(thresholds,
            TimeSpan.FromSeconds(Int("resetLeadSeconds", (int)d.ResetLead.TotalSeconds)),
            Int("resetLeadMinPct", d.ResetLeadMinPct),
            TimeSpan.FromSeconds(Int("waitingDebounceSeconds", (int)d.WaitingDebounce.TotalSeconds)),
            TimeSpan.FromSeconds(Int("waitingRepeatSeconds", (int)d.WaitingRepeat.TotalSeconds)),
            TimeSpan.FromSeconds(Int("perProviderCooldownSeconds", (int)d.PerProviderCooldown.TotalSeconds)));
    }
}

/// <summary>Finds the shared docs/ directory next to the test output or up the tree.</summary>
public static class Docs
{
    public static string Path(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(System.IO.Path.Combine(dir.FullName, "docs", "providers")))
        {
            dir = dir.Parent;
        }
        if (dir is null) throw new DirectoryNotFoundException("docs/ not found above " + AppContext.BaseDirectory);
        return System.IO.Path.Combine([dir.FullName, "docs", .. parts]);
    }
}
