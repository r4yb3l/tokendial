using System.Diagnostics;
using System.IO;
using System.Windows;
using Tokendial.Core;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.I18n;

namespace Tokendial.App.Windows;

/// <summary>
/// Removing Tokendial from inside Tokendial.
/// </summary>
/// <remarks>
/// The actual removal is Velopack's <c>Update.exe --uninstall</c>, which is what Windows runs from
/// Installed apps: it kills the app, clears the shortcuts, the registry entry and the install directory.
/// What it cannot do is ask the one question only this app knows to ask - whether the user wants to keep
/// their settings and history, which now live outside the installer's directory and survive on purpose.
/// The answer is left as a marker in the data directory and carried out by the uninstall hook, which Update.exe
/// runs in a fresh process after it has stopped this one. Deleting from here could not work: this process
/// holds its own log open, and its store and alert timers could write their files back before the kill.
/// A portable copy has no Update.exe beside it, and says so rather than pretending to uninstall itself.
/// </remarks>
public static class Uninstall
{
    private static string Updater =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tokendial", "Update.exe");

    private static string RemovalMarker => Paths.In("remove-on-uninstall");

    public static bool Available => File.Exists(Updater);

    public static void Ask(Window owner)
    {
        var options = Strings.RightToLeft ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None;
        if (!Available)
        {
            MessageBox.Show(owner, Strings.T("settings.uninstall.portable"), Strings.T("app.name"),
                MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.None, options);
            return;
        }

        var answer = MessageBox.Show(owner, Strings.T("settings.uninstall.ask"), Strings.T("settings.uninstall"),
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.None, options);
        if (answer == MessageBoxResult.Cancel) return;

        Remember(removeData: answer == MessageBoxResult.Yes);
        try
        {
            Process.Start(new ProcessStartInfo(Updater, "--uninstall") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception error)
        {
            Log.Ui.Error($"uninstall: {error.Message}");
            Remember(removeData: false);
            return;
        }
        Application.Current.Shutdown();
    }

    /// <summary>The second half of <see cref="Ask"/>, from the uninstall hook: by then nothing holds a file in the data directory open or can write one back.</summary>
    public static void RemoveDataIfAsked()
    {
        if (!File.Exists(RemovalMarker)) return;
        try { Directory.Delete(Paths.Data, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Only the latest answer counts, so a No after a Yes whose uninstall never ran keeps the data.</summary>
    private static void Remember(bool removeData)
    {
        try
        {
            if (removeData)
            {
                Directory.CreateDirectory(Paths.Data);
                File.WriteAllText(RemovalMarker, "");
            }
            else File.Delete(RemovalMarker);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
