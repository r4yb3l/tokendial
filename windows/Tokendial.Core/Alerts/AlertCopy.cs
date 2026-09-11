using Tokendial.Core.I18n;
using Tokendial.Core.Model;
using Tokendial.Core.Providers;
using Tokendial.Core.Sessions;

namespace Tokendial.Core.Alerts;

/// <summary>
/// What an alert says. The decision to alert was made upstream; this only writes it down.
/// </summary>
/// <remarks>
/// In Core rather than beside a sink because all three platforms need the same words: Windows toasts and
/// banners, macOS notifications and banners, and the freedesktop notification daemon. It touches nothing but
/// the catalogue and the readings, so the same alert reads identically wherever it is delivered.
/// </remarks>
public static class AlertCopy
{
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
