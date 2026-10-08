using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Tmds.DBus.Protocol;

namespace Tokendial.DisplayProbe;

/// <summary>Shows the real tray icon with a fixture reading and nothing else, then exits after the given time.</summary>
public sealed class TrayProbeApp : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var tray = new Tokendial.Linux.Tray.TrayIcon(this);
            tray.Update(0.42, "Tokendial tray probe");
            Console.WriteLine("tray: icon shown");
            DispatcherTimer.RunOnce(() =>
            {
                tray.Dispose();
                desktop.Shutdown();
            }, TimeSpan.FromSeconds(Program.Seconds));
        }
        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>
/// Stands in for Cinnamon's xapp-sn-watcher crashing mid-call: it owns the StatusNotifierWatcher name, and when
/// an item asks to register it dies without replying, so the caller gets <c>org.freedesktop.DBus.Error.NoReply</c>.
/// Avalonia 12.1.2 raised that from an <c>async void</c> and the runtime aborted Tokendial.
/// </summary>
public static class DyingWatcher
{
    public static async Task<int> Run()
    {
        if (DBusAddress.Session is not string address)
        {
            Console.Error.WriteLine("No session bus. Run this under dbus-run-session, never on the desktop's own bus.");
            return 1;
        }
        var connection = new DBusConnection(address);
        await connection.ConnectAsync();
        connection.AddMethodHandler(new Handler());
        await connection.RequestNameAsync("org.kde.StatusNotifierWatcher");
        Console.WriteLine("watcher: owns org.kde.StatusNotifierWatcher");
        await Task.Delay(Timeout.Infinite);
        return 0;
    }

    private sealed class Handler : IPathMethodHandler
    {
        public string Path => "/StatusNotifierWatcher";
        public bool HandlesChildPaths => false;

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            Console.WriteLine($"watcher: got {context.Request.MemberAsString}, dying without a reply");
            Process.GetCurrentProcess().Kill();
            return ValueTask.CompletedTask;
        }
    }
}
