using System.Diagnostics;

namespace Tokendial.Core.Diagnostics;

public enum Level
{
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>One sink; the host decides where lines go. Tests leave the debugger default.</summary>
public static class Log
{
    public static Action<Level, string, string> Sink { get; set; } = (level, area, message) => Trace.WriteLine($"[{level}] {area}: {message}");

    public static readonly Area Usage = new("usage");
    public static readonly Area Sessions = new("sessions");
    public static readonly Area Alerts = new("alerts");
    public static readonly Area Ui = new("ui");
}

public sealed class Area(string name)
{
    public void Debug(string message) => Log.Sink(Level.Debug, name, message);
    public void Info(string message) => Log.Sink(Level.Info, name, message);
    public void Warn(string message) => Log.Sink(Level.Warn, name, message);
    public void Error(string message) => Log.Sink(Level.Error, name, message);
}
