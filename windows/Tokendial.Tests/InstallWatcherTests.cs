using Tokendial.Core.Install;

namespace Tokendial.Tests;

public class InstallWatcherTests
{
    private sealed class World
    {
        public bool Installed;
        public bool SignedIn;
        public DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        public readonly List<InstallState> Advanced = [];
        public readonly List<InstallOutcome> Completed = [];

        public InstallWatcher Watcher(bool waitsAfterTerminal = false)
        {
            var watcher = new InstallWatcher("grok", () => Installed, () => SignedIn, waitsAfterTerminal, () => Now);
            watcher.Advanced += Advanced.Add;
            watcher.Completed += Completed.Add;
            return watcher;
        }
    }

    [Fact]
    public void AdvancesFromNotInstalledToSignedInAndCompletesOnce()
    {
        var world = new World();
        var watcher = world.Watcher();
        Assert.Equal(InstallState.NotInstalled, watcher.State);
        watcher.Tick();
        Assert.Empty(world.Advanced);
        world.Installed = true;
        watcher.Tick();
        Assert.Equal([InstallState.Installed], world.Advanced);
        world.SignedIn = true;
        watcher.Tick();
        watcher.Tick();
        Assert.Equal([InstallState.Installed, InstallState.SignedIn], world.Advanced);
        Assert.Equal([InstallOutcome.Success], world.Completed);
        Assert.False(watcher.Running);
    }

    [Fact]
    public void TimesOutAfterFifteenMinutes()
    {
        var world = new World();
        var watcher = world.Watcher();
        world.Now += TimeSpan.FromMinutes(14);
        watcher.Tick();
        Assert.Empty(world.Completed);
        world.Now += TimeSpan.FromMinutes(1);
        watcher.Tick();
        Assert.Equal([InstallOutcome.TimedOut], world.Completed);
        world.SignedIn = true;
        watcher.Tick();
        Assert.Single(world.Completed);
    }

    [Fact]
    public void AClosedTerminalEndsTheWaitWhenNothingSignedIn()
    {
        var world = new World { Installed = true };
        var watcher = world.Watcher();
        watcher.TerminalClosed();
        Assert.Equal([InstallOutcome.TerminalClosed], world.Completed);
    }

    [Fact]
    public void AClosedTerminalStillCountsASignInThatJustLanded()
    {
        var world = new World { Installed = true };
        var watcher = world.Watcher();
        world.SignedIn = true;
        watcher.TerminalClosed();
        Assert.Equal([InstallOutcome.Success], world.Completed);
    }

    [Fact]
    public void AnInstalledAppKeepsWaitingAfterItsTerminalCloses()
    {
        var world = new World();
        var watcher = world.Watcher(waitsAfterTerminal: true);
        world.Installed = true;
        watcher.TerminalClosed();
        Assert.Empty(world.Completed);
        Assert.True(watcher.Running);
        world.SignedIn = true;
        watcher.Tick();
        Assert.Equal([InstallOutcome.Success], world.Completed);
    }

    [Fact]
    public void AnAppWhoseInstallNeverHappenedEndsWithTheTerminal()
    {
        var world = new World();
        var watcher = world.Watcher(waitsAfterTerminal: true);
        watcher.TerminalClosed();
        Assert.Equal([InstallOutcome.TerminalClosed], world.Completed);
    }
}
