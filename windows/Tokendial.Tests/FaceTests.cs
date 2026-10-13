using Tokendial.Core.Model;
using Tokendial.Core.Sessions;

namespace Tokendial.Tests;

/// <summary>What the floating face shows, and how "take me there" finds the window a session lives in.</summary>
public class FaceTests
{
    private static ProviderReading Reading(string id, double used, Blocked? block = null, ReadingStatus? status = null) =>
        new(id, id, Fidelity.Official, status ?? ReadingStatus.LiveNow, [new UsageWindow("w", "W", used)], Block: block);

    private static AgentSession Session(string id, SessionState state) =>
        new(id, id, "Terminal · utils", state, null, DateTimeOffset.UnixEpoch);

    [Fact]
    public void ColourAndFillComeFromTheSameProvider()
    {
        var blocked = Reading("a", 0.10, new Blocked("rate limited", null));
        var face = Face.Of([blocked, Reading("b", 0.70)], []);
        Assert.Equal(Band.Critical, face.Band);
        Assert.Equal(1, face.Fraction);
        Assert.True(face.Spent);
    }

    [Fact]
    public void WithinOneBandTheFullerProviderWins()
    {
        var face = Face.Of([Reading("a", 0.55), Reading("b", 0.75), Reading("c", 0.20)], []);
        Assert.Equal(Band.Watch, face.Band);
        Assert.Equal(0.75, face.Fraction);
    }

    [Fact]
    public void ProvidersThatCannotBeReadAreLeftOut()
    {
        var face = Face.Of([Reading("a", 0.95, status: ReadingStatus.SignIn), Reading("b", 0.30)], []);
        Assert.Equal(Band.Ample, face.Band);
        Assert.Equal(0.30, face.Fraction);
    }

    [Fact]
    public void NothingToReadIsAnEmptyArcAndAContentFace()
    {
        var face = Face.Of([], []);
        Assert.Equal(Face.Empty, face);
        Assert.Equal("^ ^", face.Eyes);
        Assert.Equal("◡", face.Mouth);
    }

    [Fact]
    public void WaitingOutranksWorking()
    {
        var face = Face.Of([Reading("a", 0.2)], [Session("x", SessionState.Working), Session("y", SessionState.Waiting)]);
        Assert.Equal(SessionState.Waiting, face.Mood);
        Assert.Equal("O O", face.Eyes);
        Assert.Equal("o", face.Mouth);
    }

    [Theory]
    [InlineData(0.20, null, "^ ^", "◡")]
    [InlineData(0.60, null, "- -", "_")]
    [InlineData(0.90, null, "> <", "~")]
    [InlineData(1.00, SessionState.Waiting, "x x", "_")]
    [InlineData(0.20, SessionState.Working, "o o", "◡")]
    public void TheGlyphTable(double used, SessionState? mood, string eyes, string mouth)
    {
        var face = new Face(Bands.Of(used), used, mood);
        Assert.Equal(eyes, face.Eyes);
        Assert.Equal(mouth, face.Mouth);
    }

    private static readonly Dictionary<int, int> Parents = new() { [30] = 20, [20] = 10, [10] = 1 };

    [Fact]
    public void TheWalkStopsAtTheFirstProcessWithAWindow() =>
        Assert.Equal(200, Ancestry.FirstAnswer(30, id => Parents.TryGetValue(id, out var p) ? p : null, id => id is 20 or 10 ? id * 10 : (int?)null));

    [Fact]
    public void ARecycledParentIdCannotLoopForever()
    {
        var loop = new Dictionary<int, int> { [30] = 20, [20] = 30 };
        Assert.Null(Ancestry.FirstAnswer(30, id => loop.TryGetValue(id, out var p) ? p : null, _ => (int?)null));
    }
}
