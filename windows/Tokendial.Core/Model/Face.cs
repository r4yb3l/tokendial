using Tokendial.Core.Sessions;

namespace Tokendial.Core.Model;

/// <summary>
/// What the floating face shows: the arc takes the one provider closest to running out, colour and fill
/// both from it, and the glyphs take that band together with the strongest session state.
/// The table is written down in docs/design/tokens.md, which every platform draws from.
/// </summary>
public sealed record Face(Band? Band, double Fraction, SessionState? Mood)
{
    public static readonly Face Empty = new(null, 0, null);

    public static Face Of(IEnumerable<ProviderReading> readings, IReadOnlyList<AgentSession> sessions)
    {
        var worst = readings
            .Where(r => r.HasReading && r.Band is not null && r.Status is not ReadingStatus.NeedsSignIn and not ReadingStatus.Unsupported)
            .OrderByDescending(r => r.Band).ThenByDescending(Fill)
            .FirstOrDefault();
        return new Face(worst?.Band, worst is null ? 0 : Fill(worst), Activity.Of(sessions)?.State);
    }

    /// <summary>A blocked provider cannot be used however much its headline says is left, so it reads as spent.</summary>
    private static double Fill(ProviderReading reading) => reading.Block is not null ? 1 : Math.Clamp(reading.HeadlineFraction ?? 0, 0, 1);

    public bool Spent => Fraction >= 1;

    public string Eyes => Spent ? "x x"
        : Mood == SessionState.Waiting ? "O O"
        : Mood == SessionState.Working ? "o o"
        : Band == Model.Band.Critical ? "> <"
        : Band == Model.Band.Watch ? "- -"
        : "^ ^";

    public string Mouth => Spent ? "_"
        : Mood == SessionState.Waiting ? "o"
        : Band == Model.Band.Critical ? "~"
        : Band == Model.Band.Watch ? "_"
        : "◡";

    /// <summary>Eyes shut for the length of a blink.</summary>
    public const string Blink = "- -";
}
