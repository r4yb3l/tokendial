using Tmds.DBus.Protocol;
using Tokendial.Core.Alerts;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.Model;
using Tokendial.Core.Sessions;

namespace Tokendial.Linux.Alerts;

/// <summary>
/// The desktop's own notifications, over <c>org.freedesktop.Notifications</c>.
/// </summary>
/// <remarks>
/// The freedesktop equivalent of the Windows toast: the same words, delivered by whatever the session runs -
/// Cinnamon's own daemon, GNOME Shell, Dunst. Notifications therefore land in the notification centre, obey
/// Do not disturb and are styled by the desktop, which is the point of choosing them over Tokendial's banners.
/// <para>
/// The daemon assigns every notification an id, and passing that id back as <c>replaces_id</c> updates the
/// one already on screen instead of stacking another beside it. The ids are kept per provider and kind, the
/// same coalescing key the Windows sink uses as the toast's tag, so a window climbing through its thresholds
/// replaces its own notification rather than filling the screen with five.
/// </para>
/// </remarks>
public sealed class NotifySink : IAlertSink, IDisposable
{
    private const string Service = "org.freedesktop.Notifications";
    private const string Path = "/org/freedesktop/Notifications";

    private readonly Func<string, ProviderReading?> reading;
    private readonly Func<string, Activity?> activity;
    private readonly Func<DateTimeOffset> now;
    private readonly Func<string?> address;
    private readonly Dictionary<string, uint> shown = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    private DBusConnection? bus;

    public NotifySink(Func<string, ProviderReading?> reading, Func<string, Activity?> activity, Func<DateTimeOffset>? now = null,
        Func<string?>? address = null)
    {
        this.reading = reading;
        this.activity = activity;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.address = address ?? (() => SessionBusAddress());
    }

    /// <summary>
    /// Whether a daemon took the last notification. A session with no notification service is a real
    /// configuration - a bare window manager has none, and some have no session bus at all.
    /// </summary>
    public bool Available { get; private set; } = true;

    public void Deliver(Alert alert) => Deliver(alert, null);

    /// <summary>
    /// Sends the notification and calls <paramref name="failed"/> when no daemon took it, from whichever
    /// thread the send ends on. The send usually finishes after this returns, so asking
    /// <see cref="Available"/> straight afterwards would answer for the previous alert; the callback answers
    /// for this one, which is what lets the router fall back to a banner instead of delivering into nothing.
    /// </summary>
    public void Deliver(Alert alert, Action? failed)
    {
        var (title, body) = AlertCopy.Compose(alert, reading(alert.Provider), activity(alert.Provider), now());
        Log.Alerts.Info($"notify: {title} — {body}");
        _ = Send(alert, title, body, failed);
    }

    private async Task Send(Alert alert, string title, string body, Action? failed)
    {
        var key = $"{alert.Provider}|{alert.Kind}|{alert.Window ?? alert.SessionId ?? ""}";
        var sent = false;
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var connection = await Bus().ConfigureAwait(false);
            if (connection is null)
            {
                Log.Alerts.Error("notify: no session bus to send it on");
            }
            else
            {
                shown.TryGetValue(key, out var replaces);
                var id = await connection.CallMethodAsync(Compose(connection, alert, title, body, replaces),
                    static (Message message, object? _) => message.GetBodyReader().ReadUInt32()).ConfigureAwait(false);
                shown[key] = id;
                sent = true;
            }
        }
        catch (Exception error)
        {
            bus = null;
            Log.Alerts.Error($"notify: {error.Message}");
        }
        finally { gate.Release(); }

        Available = sent;
        if (!sent) failed?.Invoke();
    }

    /// <summary>
    /// The Notify call, built whole. Its own method because MessageWriter is a ref struct and cannot survive
    /// the await that sends it.
    /// </summary>
    /// <remarks>
    /// <c>desktop-entry</c> is what lets the daemon show this as Tokendial, with the icon the install wrote,
    /// rather than as an anonymous message. Urgency is raised only for a limit that has actually been hit.
    /// </remarks>
    private static MessageBuffer Compose(DBusConnection connection, Alert alert, string title, string body, uint replaces)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Service, Path, Service, "Notify", "susssasa{sv}i");
        writer.WriteString("Tokendial");
        writer.WriteUInt32(replaces);
        writer.WriteString("tokendial");
        writer.WriteString(title);
        writer.WriteString(body);
        writer.WriteArray(Array.Empty<string>());

        var hints = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString("desktop-entry");
        writer.WriteVariantString("tokendial");
        writer.WriteDictionaryEntryStart();
        writer.WriteString("urgency");
        writer.WriteVariantByte(alert.Kind == AlertKind.Limit ? (byte)2 : (byte)1);
        writer.WriteDictionaryEnd(hints);

        // -1 is the daemon's own timeout, which is the user's setting rather than ours.
        writer.WriteInt32(-1);
        return writer.CreateMessage();
    }

    private async ValueTask<DBusConnection?> Bus()
    {
        if (bus is not null) return bus;
        if (address() is not { } at) return null;

        var connection = new DBusConnection(at);
        await connection.ConnectAsync().ConfigureAwait(false);
        return bus = connection;
    }

    /// <summary>The session bus the environment names, or the one in the runtime directory; null when neither exists.</summary>
    private static string? SessionBusAddress()
    {
        var named = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        if (!string.IsNullOrWhiteSpace(named)) return named;
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return string.IsNullOrWhiteSpace(runtime) ? null : $"unix:path={runtime}/bus";
    }

    public void Dispose()
    {
        bus?.Dispose();
        bus = null;
        gate.Dispose();
    }
}
