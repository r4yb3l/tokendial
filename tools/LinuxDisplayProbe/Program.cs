using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.Model;
using Tokendial.Core.Settings;
using Tokendial.Linux.Panel;
using Tokendial.Linux.Interop;

namespace Tokendial.DisplayProbe;

public static class Program
{
    public static DockEdge Edge { get; private set; }
    public static string? Display { get; private set; }
    public static bool Expanded { get; private set; }
    public static int Seconds { get; private set; }

    public static int Main(string[] args)
    {
        if (args.Length is < 1 or > 4 || !Enum.TryParse<DockEdge>(args[0], true, out var edge)
            || !Enum.IsDefined(edge)
            || (args.Length > 2 && args[2] is not ("compact" or "expanded"))
            || (args.Length > 3 && (!int.TryParse(args[3], out _) || int.Parse(args[3]) is < 1 or > 3600)))
        {
            Console.Error.WriteLine("Usage: LinuxDisplayProbe <Top|Bottom|Left|Right> [display-name|auto] [compact|expanded] [seconds:1-3600]");
            return 2;
        }
        var rejection = Tokendial.Linux.SessionPolicy.Rejection(
            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"), Environment.GetEnvironmentVariable("DISPLAY"));
        if (rejection is not null)
        {
            Console.Error.WriteLine("The display probe requires an X11 session.");
            return 1;
        }
        Edge = edge;
        Display = args.Length > 1 && args[1] != "auto" ? args[1] : null;
        Expanded = args.Length > 2 && args[2] == "expanded";
        Seconds = args.Length > 3 ? int.Parse(args[3]) : 20;
        Log.Sink = (level, area, message) => Console.WriteLine($"{DateTimeOffset.UtcNow:O} [{level}] {area}: {message}");
        return Tokendial.Linux.Program.Builder<ProbeApp>().StartWithClassicDesktopLifetime([]);
    }
}

/// <summary>Exercises the real dock with fixture readings, without loading user settings or provider credentials.</summary>
public sealed class ProbeApp : Application
{
    public override void Initialize() => Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string[] ids = ["claude", "codex", "copilot", "cursor", "antigravity", "gemini", "glm", "grok", "opencode"];
            var panel = new PanelWindow(ids, Program.Display, Program.Edge) { Title = "Tokendial display probe" };
            var tiles = ids.Select((id, index) => new Tile(id, id, Tile.MarkFor(id),
                new ProviderReading(id, id, Fidelity.Official, ReadingStatus.LiveNow,
                    [new UsageWindow("fixture", "5h limit", (index + 1) / 10.0, ResetsAt: DateTimeOffset.UtcNow.AddHours(2))]),
                null, null, false)).ToArray();
            string? previous = null;
            void Report()
            {
                var screens = ScreenChoice.All(panel.Screens);
                var handle = panel.TryGetPlatformHandle();
                var origin = handle is null ? (X: 0, Y: 0, Ok: false) : X11.RootOrigin(handle.Handle);
                var value = JsonSerializer.Serialize(new
                {
                    saved = Program.Display, edge = Program.Edge.ToString(),
                    target = Displays.Choose(screens, Program.Display),
                    renderScaling = panel.RenderScaling, dipWidth = panel.Width, dipHeight = panel.Height,
                    actualOrigin = new { origin.X, origin.Y, origin.Ok },
                    screens
                });
                if (value == previous) return;
                previous = value;
                Console.WriteLine($"{DateTimeOffset.UtcNow:O} {value}");
            }
            var monitor = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            monitor.Tick += (_, _) => Report();
            var stop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Program.Seconds) };
            stop.Tick += (_, _) => desktop.Shutdown();
            panel.Opened += (_, _) =>
            {
                panel.Update(new PanelModel(tiles, [], 0));
                if (Program.Expanded) panel.Flash(TimeSpan.FromSeconds(Program.Seconds));
                Console.WriteLine($"window=0x{panel.TryGetPlatformHandle()?.Handle.ToInt64():x}");
                Report();
                monitor.Start();
                stop.Start();
            };
            panel.Closed += (_, _) => { monitor.Stop(); stop.Stop(); };
            desktop.MainWindow = panel;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
