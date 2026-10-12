using Tokendial.Core.I18n;
using Tokendial.Core.Model;
using Tokendial.Core.Providers;
using Tokendial.Core.Sessions;

namespace Tokendial.App.Panel;

/// <summary>One provider as the panel shows it.</summary>
public sealed record Tile(
    string Id,
    string Name,
    string Mark,
    ProviderReading Reading,
    ProviderAccount? Account,
    Activity? Activity,
    bool InFlight,
    Forecast? Forecast = null)
{
    public UsageWindow? Headline => Reading.Headline;
    public double? Fraction => Reading.HeadlineFraction;
    public Band? Band => Reading.Band;
    public bool HasReading => Reading.HasReading && Reading.Status is not ReadingStatus.NeedsSignIn and not ReadingStatus.Unsupported;
    public IEnumerable<UsageWindow> Secondary => Reading.Windows.Where(w => w.Id != Headline?.Id);
    public string HeadlineLabel => Headline is UsageWindow w ? Strings.Label(w.Label) : StatusLabel;

    public string StatusLabel => Reading.Status switch
    {
        ReadingStatus.NeedsSignIn => Strings.T("status.signIn"),
        ReadingStatus.Unsupported => Strings.T("status.nothingMetered"),
        ReadingStatus.Failed => Strings.T("status.unavailable"),
        ReadingStatus.Stale when !Reading.HasReading => InFlight ? Strings.T("status.reading") : Strings.T("status.noReadingYet"),
        _ => ""
    };

    public static string MarkFor(string id) => ProviderFamily.Of(id) switch
    {
        "claude" => "CL",
        "codex" => "CX",
        "copilot" => "CP",
        "cursor" => "CU",
        "antigravity" => "AG",
        "gemini" => "GM",
        "glm" => "GL",
        "grok" => "GK",
        "opencode" => "OC",
        _ => id.Length >= 2 ? id[..2].ToUpperInvariant() : id.ToUpperInvariant()
    };
}

/// <summary>A snapshot the window renders. Built off the UI thread from the store and the hub, applied on it.</summary>
public sealed record PanelModel(IReadOnlyList<Tile> Tiles, IReadOnlyList<AgentSession> Sessions, int Muted)
{
    public static readonly PanelModel Empty = new([], [], 0);

    public static PanelModel Build(IReadOnlyList<ProviderReading> readings, IReadOnlyList<ProviderSummary> summaries,
        IReadOnlyDictionary<string, Activity> activities, IReadOnlySet<string> inFlight, int muted, IReadOnlyDictionary<string, Forecast>? forecasts = null)
    {
        var tiles = readings.Select(r =>
        {
            var summary = summaries.FirstOrDefault(s => s.Id == r.ProviderId);
            return new Tile(r.ProviderId, r.DisplayName, Tile.MarkFor(r.ProviderId), r, summary?.Account, activities.GetValueOrDefault(r.ProviderId), inFlight.Contains(r.ProviderId),
                forecasts?.GetValueOrDefault(r.ProviderId));
        }).ToList();
        var sessions = activities.Values.SelectMany(a => a.Ordered)
            .OrderBy(s => s.State switch { SessionState.Waiting => 0, SessionState.Working => 1, _ => 2 })
            .ThenByDescending(s => s.Since).ToList();
        return new PanelModel(tiles, sessions, muted);
    }

    public bool AnyWaiting => Sessions.Any(s => s.State == SessionState.Waiting);

    /// <summary>The worst band across every provider that has a reading: what the mascot's arc carries.</summary>
    public Band? WorstBand => Tiles.Where(t => t.HasReading && t.Band is not null).Select(t => t.Band!.Value).DefaultIfEmpty().Max() is var worst
        && Tiles.Any(t => t.HasReading && t.Band is not null) ? worst : null;

    /// <summary>How full the worst provider is, so the arc shows the reading the band came from rather than an average.</summary>
    public double WorstFraction => Tiles.Where(t => t.HasReading).Select(t => Math.Clamp(t.Fraction ?? 0, 0, 1)).DefaultIfEmpty(0).Max();

    /// <summary>Waiting outranks working, as everywhere else: the one state that wants something from you.</summary>
    public SessionState? Mood => Sessions.Count == 0 ? null
        : Sessions.Any(s => s.State == SessionState.Waiting) ? SessionState.Waiting
        : Sessions.Any(s => s.State == SessionState.Working) ? SessionState.Working
        : SessionState.Idle;
}
