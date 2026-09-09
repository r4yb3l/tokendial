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

    [Fact]
    public void ClaudeProfilesShareClaudesRecipe()
    {
        Assert.Same(InstallCatalog.For("claude"), InstallCatalog.For("claude-work"));
        Assert.Equal("claude", InstallCatalog.Family("claude-work"));
    }

    public static IEnumerable<object[]> Platforms() =>
        InstallCatalog.All.Values.SelectMany(r => new[] { new object[] { r.ProviderId, "windows", r.Windows }, new object[] { r.ProviderId, "macos", r.MacOS } });

    [Theory]
    [MemberData(nameof(Platforms))]
    public void RecipesAreComplete(string providerId, string os, PlatformRecipe platform)
    {
        var recipe = InstallCatalog.All[providerId];
        Assert.False(string.IsNullOrWhiteSpace(recipe.Vendor));
        Assert.StartsWith("https://", recipe.DocsUrl);
        Assert.False(string.IsNullOrWhiteSpace(platform.Install), $"{providerId}/{os} install");
        Assert.True(platform.Detect.Commands.Count + platform.Detect.Paths.Count > 0, $"{providerId}/{os} detect");
        Assert.All(platform.Requires, r => Assert.Contains(r, new[] { "winget", "brew", "npm" }));
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
            foreach (var platform in new[] { recipe.Windows, recipe.MacOS })
            {
                if (platform.SignIn is SignInStep signIn) Assert.True(english.ContainsKey(signIn.Hint), $"{recipe.ProviderId}: {signIn.Hint}");
            }
        }
        Assert.True(english.ContainsKey("install.hint.app"));
    }
}
