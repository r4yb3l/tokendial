using Tokendial.Core.Install;
using Tokendial.Linux.Install;

namespace Tokendial.Linux.Tests;

/// <summary>
/// Which tool the Open button may start. Nothing here launches anything: starting a desktop app is what the
/// maintainer's hardware confirms.
/// </summary>
public sealed class AppLauncherTests
{
    private static PlatformRecipe Recipe(InstallKind kind, string path) =>
        new(kind, new Detect([], [path]), [], "", null, null);

    [Fact]
    public void InstalledAppResolvesToItsExecutable()
    {
        var file = Path.GetTempFileName();
        try
        {
            var launcher = new AppLauncher(recipe: key => key == "cursor" ? Recipe(InstallKind.App, file) : null);
            Assert.True(launcher.IsInstalled("cursor"));
            Assert.Equal(file, launcher.Resolve("cursor"));
            Assert.False(launcher.IsInstalled("antigravity"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void CommandLineToolIsNeverStartedWithoutATerminal()
    {
        var file = Path.GetTempFileName();
        try
        {
            var launcher = new AppLauncher(recipe: _ => Recipe(InstallKind.Cli, file));
            Assert.False(launcher.IsInstalled("codex"));
            Assert.False(launcher.Open("codex"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void MissingOrDirectoryTargetIsNotInstalled()
    {
        var directory = Directory.CreateTempSubdirectory("tokendial-app-");
        try
        {
            var missing = new AppLauncher(recipe: _ => Recipe(InstallKind.App, Path.Combine(directory.FullName, "cursor")));
            Assert.False(missing.IsInstalled("cursor"));
            Assert.False(missing.Open("cursor"));

            var folder = new AppLauncher(recipe: _ => Recipe(InstallKind.App, directory.FullName));
            Assert.False(folder.IsInstalled("cursor"));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void LinuxRecipesForTheAppProvidersAreApps()
    {
        foreach (var key in new[] { "cursor", "antigravity" })
            Assert.Equal(InstallKind.App, InstallCatalog.For(key)!.Linux!.Kind);
    }
}
