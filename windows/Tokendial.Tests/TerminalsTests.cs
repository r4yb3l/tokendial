using Tokendial.Core.Install;

namespace Tokendial.Tests;

public class TerminalsTests
{
    private static Func<string, bool> Present(params string[] names) => name => names.Contains(name);

    private static string? Nothing(string key) => null;

    [Fact]
    public void TheDistributionsOwnChoiceWinsOverTheDesktopsOwn()
    {
        var terminal = Terminals.Resolve(Nothing, Present("x-terminal-emulator", "gnome-terminal", "xterm"));
        Assert.Equal("x-terminal-emulator", terminal!.Command);
    }

    [Fact]
    public void FallsThroughToWhateverIsInstalled()
    {
        Assert.Equal("kitty", Terminals.Resolve(Nothing, Present("kitty", "xterm"))!.Command);
        Assert.Equal("xterm", Terminals.Resolve(Nothing, Present("xterm"))!.Command);
    }

    /// <summary>A system with no terminal is a real configuration, and says so rather than guessing one.</summary>
    [Fact]
    public void AsystemWithNoTerminalAnswersNothing() =>
        Assert.Null(Terminals.Resolve(Nothing, Present()));

    [Fact]
    public void TheUsersOwnChoiceWinsOverEverything()
    {
        var terminal = Terminals.Resolve(
            key => key == "TOKENDIAL_TERMINAL" ? "alacritty" : null,
            Present("alacritty", "x-terminal-emulator"));
        Assert.Equal("alacritty", terminal!.Command);

        var generic = Terminals.Resolve(
            key => key == "TERMINAL" ? "gnome-terminal" : null,
            Present("gnome-terminal", "x-terminal-emulator"));
        Assert.Equal("gnome-terminal", generic!.Command);
    }

    /// <summary>A named terminal that is not installed is not used; the search carries on without it.</summary>
    [Fact]
    public void AchoiceThatIsNotThereIsIgnored()
    {
        var terminal = Terminals.Resolve(key => key == "TERMINAL" ? "wezterm" : null, Present("xterm"));
        Assert.Equal("xterm", terminal!.Command);
    }

    /// <summary>
    /// The distinction the caller depends on: a client-server terminal exits at once, so its exit must not be
    /// read as the user closing the window.
    /// </summary>
    [Theory]
    [InlineData("gnome-terminal", true)]
    [InlineData("mate-terminal", true)]
    [InlineData("tilix", true)]
    [InlineData("x-terminal-emulator", true)]
    [InlineData("xterm", false)]
    [InlineData("kitty", false)]
    [InlineData("konsole", false)]
    public void TheClientServerTerminalsAreMarkedAsSuch(string name, bool forks) =>
        Assert.Equal(forks, Terminals.Resolve(Nothing, Present(name))!.ForksAway);

    /// <summary>An unknown terminal is assumed to fork, because the cautious answer costs only a missed close.</summary>
    [Fact]
    public void AnUnknownTerminalIsAssumedToFork()
    {
        var terminal = Terminals.Resolve(key => key == "TERMINAL" ? "st" : null, Present("st"));
        Assert.Equal("st", terminal!.Command);
        Assert.True(terminal.ForksAway);
    }

    [Fact]
    public void TheScriptPathReplacesItsPlaceholderWhereverItAppears()
    {
        var gnome = Terminals.Resolve(Nothing, Present("gnome-terminal"))!;
        Assert.Equal(["--", "sh", "/tmp/grok.sh"], gnome.Arguments("/tmp/grok.sh"));

        var tilix = Terminals.Resolve(Nothing, Present("tilix"))!;
        Assert.Equal(["-e", "sh /tmp/grok.sh"], tilix.Arguments("/tmp/grok.sh"));
    }
}
