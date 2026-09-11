using Tokendial.Core.I18n;
using Tokendial.Core.Install;
using Tokendial.Core.Providers;

namespace Tokendial.Tests;

public class InstallCatalogTests
{
    /// <summary>GLM is a key rather than a tool, so there is nothing to install.</summary>
    private static readonly string[] Assisted = ProviderCatalog.Order.Where(id => id is not "glm").ToArray();

    [Fact]
    public void EveryProviderWithATerminalInstallerHasARecipe()
    {
        Assert.Equal(Assisted.OrderBy(x => x), InstallCatalog.All.Keys.OrderBy(x => x));
        Assert.Null(InstallCatalog.For("glm"));
    }

    /// <summary>
    /// Every assisted provider has a Linux recipe. Written as a list rather than a count so that adding one
    /// without a Linux line fails here rather than reaching a Mint user as silence.
    /// </summary>
    [Fact]
    public void EveryRecipeCoversLinux()
    {
        var missing = InstallCatalog.All.Values.Where(r => r.Linux is null).Select(r => r.ProviderId).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Empty(missing);
    }

    /// <summary>The bug this replaced: everything that was not Windows was treated as macOS.</summary>
    [Fact]
    public void NoPlatformIsOfferedAnotherPlatformsPackageManager()
    {
        foreach (var recipe in InstallCatalog.All.Values)
        {
            Assert.DoesNotContain("brew", recipe.Windows.Requires);
            Assert.DoesNotContain("winget", recipe.MacOS.Requires);
            if (recipe.Linux is not PlatformRecipe linux) continue;
            Assert.DoesNotContain("brew", linux.Requires);
            Assert.DoesNotContain("winget", linux.Requires);
            Assert.DoesNotContain("brew ", linux.Install);
            Assert.DoesNotContain("winget ", linux.Install);
        }
    }

    [Fact]
    public void ClaudeProfilesShareClaudesRecipe()
    {
        Assert.Same(InstallCatalog.For("claude"), InstallCatalog.For("claude-work"));
        Assert.Equal("claude", InstallCatalog.Family("claude-work"));
    }

    /// <summary>Every platform of every recipe, from the map, so a fourth one would be covered by writing it.</summary>
    public static IEnumerable<object[]> Platforms() =>
        InstallCatalog.All.Values.SelectMany(r => r.Platforms.Select(p => new object[] { r.ProviderId, p.Key.ToString().ToLowerInvariant(), p.Value }));

    [Theory]
    [MemberData(nameof(Platforms))]
    public void RecipesAreComplete(string providerId, string os, PlatformRecipe platform)
    {
        var recipe = InstallCatalog.All[providerId];
        Assert.False(string.IsNullOrWhiteSpace(recipe.Vendor));
        Assert.StartsWith("https://", recipe.DocsUrl);
        // An app with no package-manager line is a download, and says where from.
        Assert.True(!string.IsNullOrWhiteSpace(platform.Install) || platform.DownloadUrl is not null, $"{providerId}/{os} install");
        Assert.True(platform.Detect.Commands.Count + platform.Detect.Paths.Count > 0, $"{providerId}/{os} detect");
        Assert.All(platform.Requires, r => Assert.Contains(r, new[] { "winget", "brew", "npm", "apt", "snap", "flatpak" }));
        if (platform.Kind == InstallKind.Cli) Assert.NotNull(platform.SignIn);
        if (platform.DownloadUrl is string download) Assert.StartsWith("https://", download);
        if (platform.Install.StartsWith("winget install"))
        {
            Assert.Contains("--accept-source-agreements", platform.Install);
            Assert.Contains("--accept-package-agreements", platform.Install);
            Assert.Contains("winget", platform.Requires);
        }
        // A line that needs a package manager has to say so, or the assistant cannot offer to install it first.
        if (platform.Install.StartsWith("brew install")) Assert.Contains("brew", platform.Requires);
        if (platform.Install.StartsWith("npm install -g")) Assert.Contains("npm", platform.Requires);
    }

    [Fact]
    public void EveryHintExistsInTheEnglishCatalogue()
    {
        var english = Strings.Keys("en");
        foreach (var recipe in InstallCatalog.All.Values)
        {
            foreach (var platform in recipe.Platforms.Values)
            {
                if (platform.SignIn is SignInStep signIn) Assert.True(english.ContainsKey(signIn.Hint), $"{recipe.ProviderId}: {signIn.Hint}");
            }
        }
        Assert.True(english.ContainsKey("install.hint.app"));
    }
}
