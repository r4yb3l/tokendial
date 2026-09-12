using Tokendial.Core;
using Tokendial.Core.Providers;
using Tokendial.Core.Providers.Antigravity;

namespace Tokendial.Tests;

/// <summary>
/// A provider that cannot be read somewhere has to say so, rather than reporting "sign in" to somebody who
/// is signed in.
/// </summary>
public class PlatformsTests
{
    [Fact]
    public void AproviderThatDeclaresNothingIsSupportedEverywhere()
    {
        foreach (var desktop in new[] { Desktop.Windows, Desktop.MacOS, Desktop.Linux })
        {
            Assert.True(Platforms.Supported("claude", desktop));
            Assert.True(Platforms.Supported("codex", desktop));
            Assert.True(Platforms.Supported("cursor", desktop));
        }
    }

    /// <summary>The one declaration that exists today, asserted as the whole of it.</summary>
    [Fact]
    public void AntigravityIsDeclaredUnreadableOnLinuxAndNowhereElse()
    {
        Assert.True(Platforms.Supported("antigravity", Desktop.Windows));
        Assert.True(Platforms.Supported("antigravity", Desktop.MacOS));
        Assert.False(Platforms.Supported("antigravity", Desktop.Linux));

        var refused = ProviderCatalog.Order.Where(id => !Platforms.Supported(id, Desktop.Linux)).ToArray();
        Assert.Equal(["antigravity"], refused);
    }

    /// <summary>A Claude profile inherits Claude's declaration rather than being unknown.</summary>
    [Fact]
    public void AprofileFollowsItsFamily() =>
        Assert.True(Platforms.Supported("claude-work", Desktop.Linux));

    /// <summary>
    /// The behaviour, not just the table: on Linux the provider answers with a reason instead of asking for
    /// a sign-in. Only meaningful where it applies, so it says which platform it means.
    /// </summary>
    /// <remarks>
    /// The language is deliberately left alone. Strings.Use is global, xUnit runs test classes in parallel,
    /// and setting it here raced the catalogue tests into reading English where they expected Spanish - on a
    /// two-core runner, not on either machine it was written on. What this test is about is the kind of
    /// answer, not its wording, and the wording is the catalogue tests' business anyway.
    /// </remarks>
    [Fact]
    public async Task OnLinuxAntigravityGivesAreasonRatherThanAskingForASignIn()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) return;
        var provider = new AntigravityProvider();
        Assert.Null(provider.Account());
        var error = await Assert.ThrowsAsync<UsageError>(() => provider.ReadAsync());
        Assert.Equal(UsageErrorKind.NothingMetered, error.Kind);
        Assert.NotEqual(UsageErrorKind.NeedsSignIn, error.Kind);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }
}
