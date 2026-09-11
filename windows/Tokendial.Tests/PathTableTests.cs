using System.Text.Json;
using Tokendial.Core;
using Tokendial.Core.Providers.Copilot;
using Tokendial.Core.Providers.Cursor;

namespace Tokendial.Tests;

/// <summary>
/// Where every credential lives, on all three operating systems, asserted from this one.
/// </summary>
/// <remarks>
/// The category of bug this exists to catch does not throw: on Unix .NET maps ApplicationData to ~/.config,
/// so a Windows-shaped path resolves to a directory that exists and holds nothing. The file is not found, the
/// provider reports "sign in", and a signed-in user is told to sign in again. Copilot shipped exactly that
/// from birth. Nothing here touches the real filesystem or the real environment - the roots are fabricated -
/// so the table runs identically on a Windows machine and on a Linux runner.
/// </remarks>
public class PathTableTests
{
    private const string WindowsHome = @"C:\Users\ada";
    private const string UnixHome = "/home/ada";

    private static Roots RootsFor(Desktop desktop, params (string Key, string Value)[] environment) =>
        Roots.For(desktop, desktop == Desktop.Windows ? WindowsHome : UnixHome,
            key => environment.FirstOrDefault(e => e.Key == key).Value);

    private static string? None(string key) => null;

    // ---- the roots themselves ------------------------------------------------------------------------

    [Theory]
    [InlineData(Desktop.Windows, @"C:\Users\ada\AppData\Roaming", @"C:\Users\ada\AppData\Local")]
    [InlineData(Desktop.MacOS, "/home/ada/Library/Application Support", "/home/ada/Library/Application Support")]
    [InlineData(Desktop.Linux, "/home/ada/.config", "/home/ada/.local/share")]
    public void RootsAreTheOnesEachPlatformActuallyUses(Desktop desktop, string config, string data)
    {
        var roots = RootsFor(desktop);
        Assert.Equal(config, roots.Config);
        Assert.Equal(data, roots.Data);
    }

    [Fact]
    public void XdgVariablesMoveTheLinuxRoots()
    {
        var roots = RootsFor(Desktop.Linux, ("XDG_CONFIG_HOME", "/srv/conf"), ("XDG_DATA_HOME", "/srv/data"));
        Assert.Equal("/srv/conf", roots.Config);
        Assert.Equal("/srv/data", roots.Data);
    }

    /// <summary>The XDG specification says a relative value must be ignored, not resolved against something.</summary>
    [Fact]
    public void ArelativeXdgValueIsIgnored()
    {
        var roots = RootsFor(Desktop.Linux, ("XDG_CONFIG_HOME", "conf"));
        Assert.Equal("/home/ada/.config", roots.Config);
    }

    [Fact]
    public void WindowsRootsFollowTheEnvironmentWhenItIsSet()
    {
        var roots = RootsFor(Desktop.Windows, ("APPDATA", @"D:\Roaming"), ("LOCALAPPDATA", @"D:\Local"));
        Assert.Equal(@"D:\Roaming", roots.Config);
        Assert.Equal(@"D:\Local", roots.Data);
    }

    // ---- the providers that do not simply live under the home ----------------------------------------

    [Theory]
    [InlineData(Desktop.Windows, @"C:\Users\ada\AppData\Local\github-copilot\apps.json", @"C:\Users\ada\AppData\Roaming\GitHub CLI\hosts.yml")]
    [InlineData(Desktop.MacOS, "/home/ada/.config/github-copilot/apps.json", "/home/ada/.config/gh/hosts.yml")]
    [InlineData(Desktop.Linux, "/home/ada/.config/github-copilot/apps.json", "/home/ada/.config/gh/hosts.yml")]
    public void CopilotLooksWhereCopilotWrites(Desktop desktop, string apps, string gh)
    {
        var files = CopilotCredential.Files.For(desktop, RootsFor(desktop), None);
        Assert.Equal(apps, files.Apps);
        Assert.Equal(gh, files.GhHosts);
    }

    [Fact]
    public void GhConfigDirWinsForTheGitHubCliToken()
    {
        var files = CopilotCredential.Files.For(Desktop.Linux, RootsFor(Desktop.Linux), key => key == "GH_CONFIG_DIR" ? "/srv/gh" : null);
        Assert.Equal("/srv/gh/hosts.yml", files.GhHosts);
    }

    [Theory]
    [InlineData(Desktop.Windows, @"C:\Users\ada\AppData\Roaming\Cursor\User\globalStorage\state.vscdb")]
    [InlineData(Desktop.MacOS, "/home/ada/Library/Application Support/Cursor/User/globalStorage/state.vscdb")]
    [InlineData(Desktop.Linux, "/home/ada/.config/Cursor/User/globalStorage/state.vscdb")]
    public void CursorsStoreFollowsElectron(Desktop desktop, string expected) =>
        Assert.Equal(expected, CursorCredential.StorePath(RootsFor(desktop)));

    // ---- the specs and the code have to agree --------------------------------------------------------

    /// <summary>
    /// The assertion that would have caught the Copilot bug: the spec has said the right path all along, so
    /// the code is checked against it rather than against another copy of itself. Providers whose credential
    /// is not a single file are listed here, and the list is asserted to be exactly that set, so a new
    /// provider cannot quietly opt out of the comparison.
    /// </summary>
    [Theory]
    [InlineData(Desktop.Windows)]
    [InlineData(Desktop.MacOS)]
    [InlineData(Desktop.Linux)]
    public void EverySpecDeclaresACredentialForEveryPlatform(Desktop desktop)
    {
        var key = desktop switch { Desktop.Windows => "windows", Desktop.MacOS => "macos", _ => "linux" };
        var missing = new List<string>();
        foreach (var (id, spec) in Specs())
        {
            if (!spec.GetProperty("credential").TryGetProperty(key, out _)) missing.Add(id);
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void OnlyTheseProvidersKeepTheirCredentialSomewhereOtherThanAPlainFileOnLinux()
    {
        var unusual = Specs()
            .Where(s => s.Spec.GetProperty("credential").GetProperty("linux").GetProperty("kind").GetString() is not "file")
            .Select(s => s.Id)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["antigravity", "cursor"], unusual);
    }

    /// <summary>
    /// Antigravity's Linux credential is Secret Service, which Tokendial does not read. The spec has to say
    /// so out loud, because the alternative is a signed-in user being told to sign in.
    /// </summary>
    [Fact]
    public void AproviderThatCannotBeReadOnLinuxSaysSo()
    {
        var antigravity = Specs().Single(s => s.Id == "antigravity").Spec;
        Assert.Equal("secret-service", antigravity.GetProperty("credential").GetProperty("linux").GetProperty("kind").GetString());
        Assert.False(antigravity.GetProperty("status").GetProperty("platforms").GetProperty("linux").GetBoolean());

        var declared = Specs()
            .Where(s => s.Spec.GetProperty("status").TryGetProperty("platforms", out var p) && !p.GetProperty("linux").GetBoolean())
            .Select(s => s.Id)
            .ToArray();
        Assert.Equal(["antigravity"], declared);
    }

    /// <summary>No Linux recipe may reach for a distribution's package manager: the names collide.</summary>
    /// <remarks>
    /// brew's grok is a regex tool and its glm a C++ maths library; apt has the same trap with a log-parsing
    /// library and libglm-dev. Only the vendor's own script, a vendor-scoped npm package, or a download page.
    /// </remarks>
    [Fact]
    public void LinuxRecipesComeFromTheVendorAndNotFromADistribution()
    {
        foreach (var (id, spec) in Specs())
        {
            if (!spec.TryGetProperty("install", out var install) || !install.TryGetProperty("linux", out var linux)) continue;
            var line = linux.GetProperty("install").GetString() ?? "";
            Assert.DoesNotContain("apt ", line);
            Assert.DoesNotContain("apt-get", line);
            Assert.DoesNotContain("snap install", line);
            Assert.DoesNotContain("flatpak install", line);
            Assert.DoesNotContain("pacman", line);
            Assert.DoesNotContain("brew ", line);
            if (line.StartsWith("npm install -g "))
                Assert.StartsWith("@", line["npm install -g ".Length..]);
            if (line.Length == 0)
                Assert.True(linux.TryGetProperty("downloadUrl", out _), $"{id}: an empty install line needs a download page");
        }
    }

    private static IEnumerable<(string Id, JsonElement Spec)> Specs()
    {
        foreach (var file in Directory.EnumerateFiles(Docs.Path("providers"), "*.json"))
        {
            if (Path.GetFileName(file) == "schema.json") continue;
            var document = JsonDocument.Parse(File.ReadAllText(file));
            yield return (document.RootElement.GetProperty("id").GetString()!, document.RootElement.Clone());
        }
    }
}
