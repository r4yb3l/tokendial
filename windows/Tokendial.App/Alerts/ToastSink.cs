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
        var (title, body) = AlertCopy.Compose(alert, reading(alert.Provider), activity(alert.Provider), now());
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
}
