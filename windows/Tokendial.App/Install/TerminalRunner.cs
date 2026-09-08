using System.Diagnostics;
using Tokendial.Core.Diagnostics;

namespace Tokendial.App.Install;

/// <summary>
/// Opens Windows PowerShell on a script in its own console window and reports when that window closes.
/// The host is launched directly, never through the wt.exe alias, so the process handle stays ours;
/// on Windows 11 with Terminal as the default host the window still opens in Windows Terminal.
/// </summary>
public static class TerminalRunner
{
    public static bool Start(string scriptPath, Action exited)
    {
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        info.ArgumentList.Add("-NoExit");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(scriptPath);
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.Exited += (_, _) =>
        {
            exited();
            process.Dispose();
        };
        try
        {
            process.Start();
            return true;
        }
        catch (Exception error)
        {
            Log.Ui.Error($"terminal: {error.Message}");
            process.Dispose();
            return false;
        }
    }
}
