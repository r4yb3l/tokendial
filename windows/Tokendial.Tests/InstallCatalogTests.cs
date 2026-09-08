using Tokendial.Core.I18n;
using Tokendial.Core.Install;
using Tokendial.Core.Providers;

namespace Tokendial.Tests;

public class InstallCatalogTests
{
    private static readonly string[] Assisted = ProviderCatalog.Order.Where(id => id != "glm").ToArray();

    [Fact]
    public void EveryProviderButGlmHasARecipe()
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
        Assert.All(platform.Requires, r => Assert.Contains(r, new[] { "winget", "brew" }));
        if (platform.Kind == InstallKind.Cli) Assert.NotNull(platform.SignIn);
        if (platform.DownloadUrl is string download) Assert.StartsWith("https://", download);
        if (platform.Install.StartsWith("winget install"))
        {
            Assert.Contains("--accept-source-agreements", platform.Install);
            Assert.Contains("--accept-package-agreements", platform.Install);
            Assert.Contains("winget", platform.Requires);
        }
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
