using Tokendial.Core;
using Tokendial.Core.Diagnostics;

namespace Tokendial.Linux;

/// <summary>
/// A rolling text log under the data folder, the twin of windows/Tokendial.App/Startup.cs's FileLog.
/// </summary>
/// <remarks>
/// Without this, every <c>Log.Ui.Error</c> in the application goes nowhere: <see cref="Log.Sink"/> defaults to
/// discarding, and an AppImage started from a menu entry has no terminal to print to either. So a failure
/// inside a try/catch - an install that wrote nothing, a copy that never started - leaves no trace at all and
/// the only way to find out what happened is to guess. Debug lines still need TOKENDIAL_DEBUG.
/// </remarks>
public sealed class FileLog : IDisposable
{
    private readonly object gate = new();
    private readonly string file;
    private readonly bool debug = Environment.GetEnvironmentVariable("TOKENDIAL_DEBUG") is not null;
    private StreamWriter? writer;

    public FileLog(string? directory = null)
    {
        directory ??= Paths.In("logs");
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
