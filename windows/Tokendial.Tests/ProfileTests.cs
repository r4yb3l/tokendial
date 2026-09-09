using Tokendial.Core.Providers;
using Tokendial.Core.Providers.Claude;
using Tokendial.Core.Providers.Codex;
using Tokendial.Core.Sessions;

namespace Tokendial.Tests;

public class ProfileTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "tokendial-tests", Guid.NewGuid().ToString("N"));

    public ProfileTests() => Directory.CreateDirectory(home);

    public void Dispose() => Directory.Delete(home, recursive: true);

    private void Touch(string dir, string marker)
    {
        Directory.CreateDirectory(Path.Combine(home, dir));
        File.WriteAllText(Path.Combine(home, dir, marker), "");
    }

    [Fact]
    public void DiscoversTheDefaultThenUsedExtrasInSlugOrder()
    {
        Touch(".codex-work", "auth.json");
        Touch(".codex-beta", "config.toml");
        Directory.CreateDirectory(Path.Combine(home, ".codex-empty"));
        var found = Profiles.Discover(home, ".codex", ["auth.json", "config.toml"]);
        Assert.Equal([null, "beta", "work"], found.Select(p => p.Slug));
        Assert.Equal(Path.Combine(home, ".codex"), found[0].Directory);
    }

    [Fact]
    public void CodexProfilesCarryIdsPathsAndTheSignInCommand()
    {
        Touch(".codex-work", "auth.json");
        var profiles = CodexProfile.Discover(home);
        Assert.Equal(["codex", "codex-work"], profiles.Select(p => p.Id));
        Assert.Equal("Codex (work)", profiles[1].DisplayName);
        Assert.Equal(Path.Combine(home, ".codex-work", "auth.json"), profiles[1].AuthFile);
        Assert.NotNull(profiles[0].DesktopFile);
        Assert.Null(profiles[1].DesktopFile);
        Assert.Equal("$env:CODEX_HOME='~/.codex-work'; codex login", profiles[1].SignInCommand);
        Assert.Equal("codex login", profiles[0].SignInCommand);
    }

    [Fact]
    public void ClaudeProfilesStillDiscoverThroughTheSharedRule()
    {
        Touch(".claude-team", "settings.json");
        var profiles = ClaudeProfile.Discover(home);
        Assert.Equal(["claude", "claude-team"], profiles.Select(p => p.Id));
    }

    [Theory]
    [InlineData("claude", "claude")]
    [InlineData("claude-work", "claude")]
    [InlineData("codex-team", "codex")]
    [InlineData("opencode", "opencode")]
    [InlineData("gemini", "gemini")]
    [InlineData("claudex", "claudex")]
    [InlineData("nope-thing", "nope-thing")]
    public void FamilyCollapsesOnlyKnownProfiles(string id, string family) => Assert.Equal(family, ProviderFamily.Of(id));

    [Fact]
    public void ClaudeSessionIdsCarryTheProfile()
    {
        const string json = """{"pid":4242,"cwd":"D:/work/app","status":"waiting","entrypoint":"cli","startedAt":1788940800000}""";
        var parsed = ClaudeSessions.Parse(json, "claude-work");
        Assert.NotNull(parsed);
        Assert.Equal("claude-work.4242", parsed.Value.Session.Id);
        Assert.Equal("claude.4242", ClaudeSessions.Parse(json, "claude")!.Value.Session.Id);
    }
}
