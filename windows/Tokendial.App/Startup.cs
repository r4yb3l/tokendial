using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.Providers;

namespace Tokendial.App;

/// <summary>Launch at login through the per-user Run key; no service, no task scheduler.</summary>
public static class LaunchAtLogin
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Tokendial";

    public static void Set(bool on)
    {
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(Key, writable: true);
            if (run is null) return;
            if (on) run.SetValue(Name, $"\"{Environment.ProcessPath}\"");
            else run.DeleteValue(Name, throwOnMissingValue: false);
        }
        catch (Exception error) { Log.Ui.Error($"launch at login: {error.Message}"); }
    }

    public static bool IsSet()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(Key);
            return run?.GetValue(Name) is string;
        }
        catch (Exception) { return false; }
    }
}

/// <summary>Finds and starts the coding tools a sign-in route points at.</summary>
public sealed class AppLauncher : IAppLauncher
{
    private static readonly Dictionary<string, string[]> Known = new()
    {
        ["cursor"] = [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "cursor", "Cursor.exe")],
        ["antigravity"] = [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Antigravity", "Antigravity.exe")],
        ["codex"] = []
    };

    public bool IsInstalled(string appKey) => Resolve(appKey) is not null;

    public bool Open(string appKey)
    {
        var target = Resolve(appKey);
        if (target is null) return false;
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception error)
        {
            Log.Ui.Error($"open {appKey}: {error.Message}");
            return false;
        }
    }

    private static string? Resolve(string appKey)
    {
        if (Known.TryGetValue(appKey, out var paths) && paths.FirstOrDefault(File.Exists) is string found) return found;
        var names = ProviderCatalog.AppCommands.GetValueOrDefault(appKey) ?? [];
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory.Trim(), name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}

/// <summary>Rolling text log under %LOCALAPPDATA%\Tokendial\logs. Debug lines only with TOKENDIAL_DEBUG set.</summary>
public sealed class FileLog : IDisposable
{
    private readonly object gate = new();
    private readonly string file;
    private readonly bool debug = Environment.GetEnvironmentVariable("TOKENDIAL_DEBUG") is not null;
    private StreamWriter? writer;

    public FileLog(string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tokendial", "logs");
        Directory.CreateDirectory(directory);
        file = Path.Combine(directory, "tokendial.log");
        Rotate();
        writer = new StreamWriter(new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        Log.Sink = Write;
    }

    private void Write(Level level, string area, string message)
    {
        if (level == Level.Debug && !debug) return;
        lock (gate)
        {
            writer?.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {area}: {message}");
        }
    }

    private void Rotate()
    {
        try
        {
            if (File.Exists(file) && new FileInfo(file).Length > 1_000_000) File.Move(file, file + ".1", overwrite: true);
        }
        catch (IOException) { }
    }

    public void Dispose()
    {
        lock (gate)
        {
            writer?.Dispose();
            writer = null;
        }
    }
}
