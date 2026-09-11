using System.Diagnostics;
using System.IO;
using System.Windows;
using Tokendial.Core;
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
/// A portable copy has no Update.exe beside it, and says so rather than pretending to uninstall itself.
/// </remarks>
public static class Uninstall
{
    private static string Updater =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tokendial", "Update.exe");

    public static bool Available => File.Exists(Updater);

    public static void Ask(Window owner)
    {
        if (!Available)
        {
            MessageBox.Show(owner, Strings.T("settings.uninstall.portable"), Strings.T("app.name"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(owner, Strings.T("settings.uninstall.ask"), Strings.T("settings.uninstall"),
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return;

        if (answer == MessageBoxResult.Yes)
        {
            try
            {
                if (Directory.Exists(Paths.Data)) Directory.Delete(Paths.Data, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        Process.Start(new ProcessStartInfo(Updater, "--uninstall") { UseShellExecute = true });
        Application.Current.Shutdown();
    }
}
