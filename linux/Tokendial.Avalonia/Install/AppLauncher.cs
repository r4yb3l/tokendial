using System.Diagnostics;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.Install;
using Tokendial.Core.Providers;

namespace Tokendial.Linux.Install;

/// <summary>
/// Opens the desktop app that owns a credential - Cursor, Antigravity - for the Open button in settings and
/// for connecting a provider that has not signed in yet. The Linux twin of the AppLauncher in
/// windows/Tokendial.App/Startup.cs, finding the app where the install assistant looks for it: the
/// detection paths and commands in the provider's own spec.
/// </summary>
/// <remarks>
/// Only recipes of kind app are opened. A command-line tool started with no terminal has nothing to draw on,
/// and it signs in through the assistant's terminal instead, so here it counts as not installed.
/// </remarks>
public sealed class AppLauncher : IAppLauncher
{
    // What an AppImage runtime sets for the program inside it. Inherited, they would tell the app started
    // from here that it is Tokendial's AppImage; electron-updater, for one, reads APPIMAGE to find the file
    // it replaces when it updates.
    private static readonly string[] AppImageVariables = ["APPIMAGE", "APPDIR", "ARGV0", "OWD"];

    private readonly ToolLocator locator;
    private readonly Func<string, PlatformRecipe?> recipe;

    public AppLauncher(ToolLocator? locator = null, Func<string, PlatformRecipe?>? recipe = null)
    {
        this.locator = locator ?? new ToolLocator();
        this.recipe = recipe ?? (appKey => InstallCatalog.For(appKey)?.Here);
    }

    public bool IsInstalled(string appKey) => Resolve(appKey) is not null;

    /// <summary>Starts the app and lets it go: Tokendial neither waits for it nor ends it.</summary>
    public bool Open(string appKey)
    {
        if (Resolve(appKey) is not { } executable) return false;
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        foreach (var name in AppImageVariables) start.Environment.Remove(name);

        try
        {
            using var process = Process.Start(start);
            Log.Ui.Info($"open {appKey}: {executable}");
            return process is not null;
        }
        catch (Exception error)
        {
            Log.Ui.Error($"open {appKey}: {error.Message}");
            return false;
        }
    }

    /// <summary>The app's executable file, or null when it is not installed or is not an app.</summary>
    internal string? Resolve(string appKey) =>
        recipe(appKey) is { Kind: InstallKind.App } platform
        && locator.Resolve(platform.Detect) is { } found
        && File.Exists(found)
            ? found
            : null;
}
