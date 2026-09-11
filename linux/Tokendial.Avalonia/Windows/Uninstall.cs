using Avalonia.Controls;
using Tokendial.Core.I18n;
using Tokendial.Linux.Install;

namespace Tokendial.Linux.Windows;

/// <summary>
/// Adding Tokendial to the menu, and taking it back out, from inside Tokendial.
/// </summary>
/// <remarks>
/// The Linux sibling of windows/Tokendial.App/Windows/Uninstall.cs, asking the same Yes/No/Cancel question
/// about the settings and the history, which live outside whatever was installed and survive on purpose.
/// What differs is who does the removing: on Windows that is Velopack's <c>Update.exe --uninstall</c>, and
/// here there is no installer to hand back to - <see cref="Desktop"/> wrote every file, so it removes them.
/// A copy that was merely run from a download folder says so rather than pretending to uninstall itself.
/// </remarks>
public static class Uninstall
{
    public static async Task Ask(Window owner, Action quit)
    {
        if (!Desktop.InMenu && !Desktop.CanInstall)
        {
            await Tell(owner, Strings.T("settings.uninstall.loose"));
            return;
        }

        var answer = await Windows.Ask.YesNoCancel(owner, Strings.T("settings.uninstall"),
            Strings.T("settings.uninstall.appimage"));
        if (answer == Answer.Cancel) return;

        Desktop.Remove(alsoData: answer == Answer.Yes);
        quit();
    }

    /// <summary>
    /// Putting Tokendial in the menu is offered and never silent: it copies a file into the user's home and
    /// writes two entries, which is their decision to make, the same rule the install assistant follows.
    /// </summary>
    public static async Task Offer(Window owner, Action changed)
    {
        if (!Desktop.CanInstall)
        {
            await Tell(owner, Strings.T("settings.addToMenu.loose"));
            return;
        }

        var answer = await Windows.Ask.Confirm(owner, Strings.T("settings.addToMenu"),
            Strings.T("settings.addToMenu.ask"), Strings.T("settings.addToMenu.confirm"));
        if (answer != Answer.Yes) return;

        var done = Desktop.Install();
        changed();
        await Tell(owner, Strings.T(done ? "settings.addToMenu.done" : "settings.addToMenu.failed"));
    }

    private static Task Tell(Window owner, string message) =>
        Windows.Ask.Tell(owner, Strings.T("app.name"), message);
}
