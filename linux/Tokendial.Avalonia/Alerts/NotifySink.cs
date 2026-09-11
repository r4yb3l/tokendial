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
    private readonly Dictionary<string, uint> shown = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    private DBusConnection? bus;

    public NotifySink(Func<string, ProviderReading?> reading, Func<string, Activity?> activity, Func<DateTimeOffset>? now = null)
    {
        this.reading = reading;
        this.activity = activity;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Whether a daemon answered the last time one was asked. A session with no notification service is a
    /// real configuration - a bare window manager has none - and the router falls back to banners rather
    /// than delivering an alert into nothing.
    /// </summary>
    public bool Available { get; private set; } = true;

    public void Deliver(Alert alert)
    {
        var (title, body) = AlertCopy.Compose(alert, reading(alert.Provider), activity(alert.Provider), now());
        Log.Alerts.Info($"notify: {title} — {body}");
        _ = Send(alert, title, body);
    }

    private async Task Send(Alert alert, string title, string body)
    {
        var key = $"{alert.Provider}|{alert.Kind}|{alert.Window ?? alert.SessionId ?? ""}";
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var connection = await Bus().ConfigureAwait(false);
            if (connection is null) return;

            shown.TryGetValue(key, out var replaces);
            var id = await connection.CallMethodAsync(Compose(connection, alert, title, body, replaces),
                static (Message message, object? _) => message.GetBodyReader().ReadUInt32()).ConfigureAwait(false);
            shown[key] = id;
            Available = true;
        }
        catch (Exception error)
        {
            Available = false;
            bus = null;
            Log.Alerts.Error($"notify: {error.Message}");
        }
        finally { gate.Release(); }
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
        var address = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        if (string.IsNullOrWhiteSpace(address))
        {
            var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (string.IsNullOrWhiteSpace(runtime)) return null;
            address = $"unix:path={runtime}/bus";
        }

        var connection = new DBusConnection(address);
        await connection.ConnectAsync().ConfigureAwait(false);
        return bus = connection;
    }

    public void Dispose()
    {
        bus?.Dispose();
        bus = null;
        gate.Dispose();
    }
}
