using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tokendial.Core.Alerts;

/// <summary>
/// The deterministic reducer specified in docs/alerts/spec.md. It owns no
/// clock and no thread: the host feeds it events and delivers what comes back.
/// Every (provider, window) lives in an epoch keyed by its reset time; a new
/// epoch re-arms everything.
/// </summary>
public sealed class AlertEngine
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AlertConfig config;
    private readonly AlertState state;

    public AlertEngine(AlertConfig? config = null, AlertState? state = null)
    {
        this.config = config ?? AlertConfig.Default;
        this.state = state ?? new AlertState();
    }

    public static AlertEngine Load(string json, AlertConfig? config = null)
    {
        AlertState? state = null;
        try
        {
            state = JsonSerializer.Deserialize<AlertState>(json, Json);
        }
        catch (JsonException)
        {
        }
        return new AlertEngine(config, state?.Schema == 1 ? state : null);
    }

    public string Save() => JsonSerializer.Serialize(state, Json);

    public IReadOnlyList<Alert> Reduce(AlertEvent e)
    {
        var candidates = new List<Alert>();
        switch (e)
        {
            case AlertEvent.Usage usage:
                candidates.AddRange(ApplyUsage(usage));
                break;
            case AlertEvent.Session session:
                ApplySession(session);
                break;
            case AlertEvent.Hover hover:
                state.HoverOn = hover.On;
                break;
            case AlertEvent.Restart restart:
                Prune(restart.At);
                break;
        }
        candidates.AddRange(TimeChecks(e.At));
        return Gate(candidates, e.At);
    }

    private IEnumerable<Alert> ApplyUsage(AlertEvent.Usage usage)
    {
        var key = $"{usage.Provider}|{usage.Window}";
        var epoch = state.Epochs.FirstOrDefault(x => x.Key == key);
        var fresh = epoch is null || epoch.Closed || epoch.ResetsAt != usage.ResetsAt || epoch.LastPct - usage.UsedPct > 10;
        if (fresh)
        {
            state.Epochs.RemoveAll(x => x.Key == key);
            epoch = new Epoch { Key = key, Provider = usage.Provider, Window = usage.Window, ResetsAt = usage.ResetsAt };
            state.Epochs.Add(epoch);
        }
        epoch!.LastPct = usage.UsedPct;
        epoch.ResetsAt = usage.ResetsAt;

        if (usage.Limited || usage.UsedPct >= 100)
        {
            foreach (var threshold in config.Thresholds) epoch.Fired.Add(threshold.ToString());
            if (epoch.Fired.Add("limit"))
            {
                yield return new Alert(AlertKind.Limit, usage.Provider, usage.Window, ResetsAt: usage.ResetsAt);
            }
            yield break;
        }

        var crossed = config.Thresholds.Where(t => t <= usage.UsedPct && !epoch.Fired.Contains(t.ToString())).ToList();
        if (crossed.Count == 0) yield break;
        foreach (var threshold in crossed) epoch.Fired.Add(threshold.ToString());
        yield return new Alert(AlertKind.Threshold, usage.Provider, usage.Window, Pct: crossed.Max());
    }

    private void ApplySession(AlertEvent.Session session)
    {
        var key = $"{session.Provider}|{session.SessionId}";
        if (session.State == SessionActivity.Waiting)
        {
            if (state.Waiting.All(w => w.Key != key))
            {
                state.Waiting.Add(new WaitingEntry { Key = key, Provider = session.Provider, SessionId = session.SessionId, Since = session.At });
            }
            return;
        }
        state.Waiting.RemoveAll(w => w.Key == key);
    }

    private IEnumerable<Alert> TimeChecks(DateTimeOffset now)
    {
        foreach (var epoch in state.Epochs.Where(x => !x.Closed && x.ResetsAt is not null).ToList())
        {
            var resetsAt = epoch.ResetsAt!.Value;
            if (!epoch.ResetSoonFired && !epoch.Fired.Contains("limit") && resetsAt > now
                && resetsAt - now <= config.ResetLead && epoch.LastPct >= config.ResetLeadMinPct)
            {
                epoch.ResetSoonFired = true;
                yield return new Alert(AlertKind.ResetSoon, epoch.Provider, epoch.Window, ResetsAt: resetsAt);
            }
            if (now >= resetsAt && epoch.Fired.Contains("limit") && !epoch.ResetDoneFired)
            {
                epoch.ResetDoneFired = true;
                epoch.Closed = true;
                yield return new Alert(AlertKind.ResetDone, epoch.Provider, epoch.Window);
            }
        }

        foreach (var waiting in state.Waiting)
        {
            var due = waiting.LastFired is DateTimeOffset last ? last + config.WaitingRepeat : waiting.Since + config.WaitingDebounce;
            if (now < due) continue;
            waiting.LastFired = now;
            yield return new Alert(AlertKind.Waiting, waiting.Provider, SessionId: waiting.SessionId);
        }
    }

    /// <summary>Hover and cooldown decide what goes out now, what waits, and what is dropped.</summary>
    private IReadOnlyList<Alert> Gate(List<Alert> candidates, DateTimeOffset now)
    {
        var emitted = new List<Alert>();

        if (!state.HoverOn)
        {
            foreach (var held in state.Held.ToList())
            {
                if (InCooldown(held.Provider, now)) continue;
                state.Held.Remove(held);
                emitted.Add(held);
            }
        }

        foreach (var alert in candidates)
        {
            if (alert.Kind == AlertKind.Threshold)
            {
                if (state.HoverOn || InCooldown(alert.Provider, now)) continue;
                emitted.Add(alert);
                state.CooldownUntil[alert.Provider] = now + config.PerProviderCooldown;
                continue;
            }
            if (state.HoverOn || InCooldown(alert.Provider, now))
            {
                state.Held.Add(alert);
                continue;
            }
            emitted.Add(alert);
            state.CooldownUntil[alert.Provider] = now + config.PerProviderCooldown;
        }

        return emitted.OrderBy(a => a.Kind).ThenBy(a => a.Provider, StringComparer.Ordinal).ToList();
    }

    private bool InCooldown(string provider, DateTimeOffset now) =>
        state.CooldownUntil.TryGetValue(provider, out var until) && until > now;

    private void Prune(DateTimeOffset now) =>
        state.Epochs.RemoveAll(x => x.ResetsAt is DateTimeOffset at && at < now - TimeSpan.FromHours(24));
}

/// <summary>Everything the engine remembers, shaped for JSON.</summary>
public sealed class AlertState
{
    public int Schema { get; set; } = 1;
    public List<Epoch> Epochs { get; set; } = new();
    public List<WaitingEntry> Waiting { get; set; } = new();
    public List<Alert> Held { get; set; } = new();
    public Dictionary<string, DateTimeOffset> CooldownUntil { get; set; } = new();
    public bool HoverOn { get; set; }
}

public sealed class Epoch
{
    public string Key { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Window { get; set; } = "";
    public DateTimeOffset? ResetsAt { get; set; }
    public double LastPct { get; set; }
    public HashSet<string> Fired { get; set; } = new();
    public bool ResetSoonFired { get; set; }
    public bool ResetDoneFired { get; set; }
    public bool Closed { get; set; }
}

public sealed class WaitingEntry
{
    public string Key { get; set; } = "";
    public string Provider { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTimeOffset Since { get; set; }
    public DateTimeOffset? LastFired { get; set; }
}
