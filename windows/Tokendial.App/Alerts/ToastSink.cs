using Microsoft.Toolkit.Uwp.Notifications;
using Tokendial.App.Interop;
using Tokendial.Core.Alerts;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.Model;
using Tokendial.Core.Sessions;

namespace Tokendial.App.Alerts;

/// <summary>Windows toasts for the engine's alerts. Copy only; the decision to alert was made upstream.</summary>
public sealed class ToastSink : IAlertSink
{
    private readonly Func<string, ProviderReading?> reading;
    private readonly Func<string, Activity?> activity;
    private readonly Func<DateTimeOffset> now;
    private int muted;

    public ToastSink(Func<string, ProviderReading?> reading, Func<string, Activity?> activity, Func<DateTimeOffset>? now = null)
    {
        this.reading = reading;
        this.activity = activity;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Alerts Windows queued silently under Focus Assist since the panel was last expanded.</summary>
    public int Muted => muted;
    public event Action? MutedChanged;
    public void ClearMuted() { if (Interlocked.Exchange(ref muted, 0) != 0) MutedChanged?.Invoke(); }

    public void Deliver(Alert alert)
    {
        var (title, body) = Compose(alert, reading(alert.Provider), activity(alert.Provider), now());
        Log.Alerts.Info($"toast: {title} — {body}");
        try
        {
            new ToastContentBuilder()
                .AddArgument("action", "open")
                .AddArgument("provider", alert.Provider)
                .AddText(title)
                .AddText(body)
                .Show(toast =>
                {
                    toast.ExpirationTime = DateTimeOffset.Now.AddMinutes(15);
                    toast.Group = alert.Provider;
                    toast.Tag = $"{alert.Kind}-{alert.Window ?? alert.SessionId ?? ""}".Replace('.', '-');
                });
            if (Native.NotificationsMuted()) { Interlocked.Increment(ref muted); MutedChanged?.Invoke(); }
        }
        catch (Exception error) { Log.Alerts.Error($"toast: {error.Message}"); }
    }

    public static (string Title, string Body) Compose(Alert alert, ProviderReading? reading, Activity? activity, DateTimeOffset now)
    {
        var name = reading?.DisplayName ?? Humanize(alert.Provider);
        var window = alert.Window is null ? null : reading?.Windows.FirstOrDefault(w => w.Id == alert.Window);
        var windowLabel = window?.Label ?? "Usage";
        var reset = (window?.ResetsAt ?? alert.ResetsAt) is DateTimeOffset at ? Copy.Reset(at, now) : null;
        switch (alert.Kind)
        {
            case AlertKind.Threshold:
                return ($"{name} at {alert.Pct}%", Join(windowLabel, reset));
            case AlertKind.Limit:
                return ($"{name} limit reached", Join(windowLabel, reset ?? "Waiting for the window to reset"));
            case AlertKind.ResetSoon:
                return ($"{name} resets soon", Join(windowLabel, reset, alert.Pct is int p ? $"{p}% used" : null));
            case AlertKind.ResetDone:
                return ($"{name} is available again", $"{windowLabel} has reset");
            case AlertKind.Waiting:
                var session = activity?.Sessions.FirstOrDefault(s => s.Id == alert.SessionId);
                var who = session is null ? name : $"{session.Name}";
                var where = session is null ? null : session.Where;
                var why = session?.WaitingFor;
                return ($"{who} is waiting for you", Join(name, where, why));
            default:
                return (name, windowLabel);
        }
    }

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));

    private static string Humanize(string id) => id switch
    {
        _ when id.StartsWith("claude", StringComparison.Ordinal) => "Claude Code",
        "codex" => "Codex", "cursor" => "Cursor", "antigravity" => "Antigravity", "glm" => "GLM", "grok" => "Grok", "opencode" => "OpenCode",
        _ => id
    };
}
