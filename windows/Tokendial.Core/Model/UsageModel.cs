using Tokendial.Core.I18n;

namespace Tokendial.Core.Model;

/// <summary>Whether a number came from the vendor or was worked out locally. Derived readings are shown with a tilde and no dial arc.</summary>
public enum Fidelity
{
    Official,
    Derived
}

/// <summary>Colour band of a reading. Critical starts at 0.80 so the colour change and the 80 % alert coincide.</summary>
public enum Band
{
    Ample,
    Watch,
    Critical
}

public static class Bands
{
    public static Band Of(double usedFraction) => usedFraction switch
    {
        < 0.50 => Band.Ample,
        < 0.80 => Band.Watch,
        _ => Band.Critical
    };
}

/// <summary>How much to trust the reading on screen right now.</summary>
public abstract record ReadingStatus
{
    public sealed record Live : ReadingStatus;
    public sealed record Stale(DateTimeOffset Since) : ReadingStatus;
    public sealed record NeedsSignIn : ReadingStatus;
    public sealed record Unsupported(string Why) : ReadingStatus;
    public sealed record Failed(string Why) : ReadingStatus;

    public static readonly ReadingStatus LiveNow = new Live();
    public static readonly ReadingStatus SignIn = new NeedsSignIn();

    public bool IsStale => this is Stale;
    public DateTimeOffset? StaleSince => (this as Stale)?.Since;
}

/// <summary>One metered window: a fraction used, or a bare count when the vendor publishes no ceiling.</summary>
public sealed record UsageWindow(string Id, string Label, double? UsedFraction = null, int? Count = null, DateTimeOffset? ResetsAt = null)
{
    /// <summary>"63% used · 37% left", or "~7 requests today" for a count.</summary>
    public string Summary(Fidelity fidelity)
    {
        if (UsedFraction is double f)
        {
            var used = (int)Math.Round(f * 100, MidpointRounding.AwayFromZero);
            var tilde = fidelity == Fidelity.Derived ? "~" : "";
            return tilde + Strings.T("copy.usedLeft", ("used", used), ("left", Math.Max(0, 100 - used)));
        }
        if (Count is int n) return Strings.Plural("copy.requests", n);
        return Strings.T("copy.noReading");
    }
}

/// <summary>Set when a limit has been reached even where the headline still shows room.</summary>
public sealed record Blocked(string Reason, DateTimeOffset? Until);

/// <summary>Everything the panel shows for one provider.</summary>
public sealed record ProviderReading(
    string ProviderId,
    string DisplayName,
    Fidelity Fidelity,
    ReadingStatus Status,
    IReadOnlyList<UsageWindow> Windows,
    string? HeadlineId = null,
    Blocked? Block = null)
{
    /// <summary>The window the dial means. Declared by the provider; when it is missing the dial shows no reading rather than another window.</summary>
    public UsageWindow? Headline => HeadlineId is null ? Windows.FirstOrDefault() : Windows.FirstOrDefault(w => w.Id == HeadlineId);

    public double? HeadlineFraction => Headline?.UsedFraction;

    public bool HasReading => Windows.Count > 0;

    /// <summary>"63%", a bare count, or a dash.</summary>
    public string HeadlineText =>
        HeadlineFraction is double f ? $"{(int)Math.Round(f * 100, MidpointRounding.AwayFromZero)}%"
        : Headline?.Count is int n ? n.ToString()
        : "—";

    public Band? Band => Block is not null ? Model.Band.Critical : HeadlineFraction is double f ? Bands.Of(f) : null;
}
