using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Tokendial.Core.Model;
using Tokendial.Core.Sessions;
using Tokendial.Linux.Interop;
using Tokendial.Linux.Panel;

namespace Tokendial.Linux.Tests;

/// <summary>
/// The floating face as far as it can be checked without an X server: how "take me there" reads a parent from
/// /proc, and what the speech bubble decides to say. Whether the window manager keeps the face above, refuses
/// it focus and honours the activation request is only visible on a real desktop.
/// </summary>
public class FaceWindowTests
{
    [Theory]
    [InlineData("4242 (claude) S 4100 4242 4100 34816 4242 4194560", 4100)]
    [InlineData("812 (tmux: server) S 1 812 812 0 -1 4194624", 1)]
    [InlineData("77 (a) b) (c) R 66 77 66 0 -1", 66)]
    [InlineData("9 (gnome-terminal-) S 1500 9 9 0 -1", 1500)]
    public void TheParentIsCountedFromTheLastParenthesis(string stat, int parent) =>
        Assert.Equal(parent, ProcStat.Parent(stat));

    [Theory]
    [InlineData("")]
    [InlineData("4242 claude S 4100")]
    [InlineData("4242 (claude)")]
    [InlineData("4242 (claude) S x")]
    public void AMalformedLineHasNoParent(string stat) =>
        Assert.Null(ProcStat.Parent(stat));

    [Fact]
    public void TheWalkStopsAtInit()
    {
        Assert.Null(ProcStat.ParentOf(1));
        Assert.Null(ProcStat.ParentOf(0));
    }

    private static (AgentSession, string) Waiting(string id) =>
        (new AgentSession(id, id, "Terminal · utils", SessionState.Waiting, null, DateTimeOffset.UnixEpoch, Pid: 4242), "Claude Code");

    [AvaloniaFact]
    public void TheBubbleSpeaksOnlyWhileSomebodyWaits()
    {
        var bubble = new SpeechBubble();
        var changes = 0;
        bubble.Changed += () => changes++;
        try
        {
            bubble.Say([Waiting("claude.1")]);
            Assert.True(bubble.HasSomethingToSay);
            bubble.Say([Waiting("claude.1")]);
            Assert.Equal(1, changes);
            bubble.Say([]);
            Assert.False(bubble.HasSomethingToSay);
            Assert.Equal(2, changes);
        }
        finally { bubble.Close(); }
    }

    [AvaloniaFact]
    public void TheFaceTakesAModelWithNoXServerToAsk()
    {
        var window = new MascotWindow();
        try
        {
            window.Show();
            window.PlaceAt(null, null);
            var session = new AgentSession("claude.1", "utils", "Terminal · utils", SessionState.Waiting, null, DateTimeOffset.UnixEpoch, Pid: 4242);
            window.Apply(new PanelModel([], [session], 0));
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsVisible);
        }
        finally { window.Close(); }
    }
}
