using Microsoft.Toolkit.Uwp.Notifications;
using Tokendial.App.Interop;
using Tokendial.Core.Alerts;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.I18n;
using Tokendial.Core.Model;
using Tokendial.Core.Providers;
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
        var windowLabel = window is null ? Strings.T("alert.usage") : Strings.Label(window.Label);
        var reset = (window?.ResetsAt ?? alert.ResetsAt) is DateTimeOffset at ? Copy.Reset(at, now) : null;
        switch (alert.Kind)
        {
            case AlertKind.Threshold:
                return (Strings.T("alert.threshold.title", ("name", name), ("pct", alert.Pct ?? 0)), Join(windowLabel, reset));
            case AlertKind.Limit:
                return (Strings.T("alert.limit.title", ("name", name)), Join(windowLabel, reset ?? Strings.T("alert.limit.waiting")));
            case AlertKind.ResetSoon:
                return (Strings.T("alert.resetSoon.title", ("name", name)), Join(windowLabel, reset, alert.Pct is int p ? Strings.T("alert.resetSoon.used", ("pct", p)) : null));
            case AlertKind.ResetDone:
                return (Strings.T("alert.resetDone.title", ("name", name)), Strings.T("alert.resetDone.body", ("window", windowLabel)));
            case AlertKind.Waiting:
                var session = activity?.Sessions.FirstOrDefault(s => s.Id == alert.SessionId);
                return (Strings.T("alert.waiting.title", ("name", name)), session is null ? Strings.T("alert.waiting.body") : Join(session.Where, session.WaitingFor));
            default:
                return (name, windowLabel);
        }
    }

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));

    private static string Humanize(string id) => ProviderFamily.Of(id) switch
    {
        "claude" => "Claude Code",
        "codex" => "Codex", "copilot" => "GitHub Copilot", "cursor" => "Cursor", "antigravity" => "Antigravity", "gemini" => "Gemini CLI", "glm" => "GLM", "grok" => "Grok", "opencode" => "OpenCode",
        _ => id
    };
}
