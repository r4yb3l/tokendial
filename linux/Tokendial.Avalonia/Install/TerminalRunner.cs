using System.Diagnostics;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.Install;

namespace Tokendial.Linux.Install;

/// <summary>
/// Opens a terminal on a script and reports when that window closes.
/// </summary>
/// <remarks>
/// The Linux counterpart of windows/Tokendial.App/Install/TerminalRunner.cs, with one difference that decides
/// the shape: several terminals are client-server. The command hands the request to a process that is already
/// running and exits within milliseconds, so its exit says nothing about the window. Where that is the case
/// nothing is wired to it, and the watcher falls back to polling the provider - which it does anyway, every
/// three seconds with a fifteen-minute timeout.
/// </remarks>
public static class TerminalRunner
{
    public static Terminal? Available =>
        Terminals.Resolve(Environment.GetEnvironmentVariable, name => new ToolLocator().IsInstalled(new Detect([name], [])));

    public static bool Start(string scriptPath, Action exited)
    {
        if (Available is not { } terminal)
        {
            Log.Ui.Error("terminal: no terminal emulator found");
            return false;
        }

        var info = new ProcessStartInfo(terminal.Command)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        foreach (var argument in terminal.Arguments(scriptPath)) info.ArgumentList.Add(argument);

        var process = new Process { StartInfo = info, EnableRaisingEvents = !terminal.ForksAway };
        if (!terminal.ForksAway)
        {
            process.Exited += (_, _) =>
            {
                exited();
                process.Dispose();
            };
        }

        try
        {
            process.Start();
            Log.Ui.Info($"terminal: {terminal.Command} {string.Join(' ', terminal.Arguments(scriptPath))}");
            if (terminal.ForksAway) process.Dispose();
            return true;
        }
        catch (Exception error)
        {
            Log.Ui.Error($"terminal {terminal.Command}: {error.Message}");
            process.Dispose();
            return false;
        }
    }
}
