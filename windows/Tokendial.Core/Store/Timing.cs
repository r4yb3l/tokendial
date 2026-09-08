using System.Globalization;

namespace Tokendial.Core.Store;

/// <summary>Retry-After is seconds or an HTTP date; a past date is zero.</summary>
public static class RetryAfterHeader
{
    public static TimeSpan? Parse(string? value, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds))
        {
            return TimeSpan.FromSeconds(Math.Max(0, seconds));
        }
        if (DateTimeOffset.TryParseExact(text, "R", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            return date > now ? date - now : TimeSpan.Zero;
        }
        return null;
    }

    public static TimeSpan? From(HttpResponseMessage response, DateTimeOffset now) =>
        response.Headers.TryGetValues("Retry-After", out var values) ? Parse(values.FirstOrDefault(), now) : null;
}

/// <summary>
/// How long to wait after a 429. The vendor's hint only raises the floor: some
/// answer Retry-After: 0. Starts at a minute, doubles per consecutive 429, caps
/// at fifteen minutes so it always recovers on its own.
/// </summary>
public static class Backoff
{
    public static readonly TimeSpan Floor = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan Ceiling = TimeSpan.FromMinutes(15);

    public static TimeSpan Exponential(int consecutive, TimeSpan? hint) =>
        TimeSpan.FromSeconds(Math.Min(Ceiling.TotalSeconds,
            Math.Max(Floor.TotalSeconds * Math.Pow(2, Math.Min(consecutive, 4)), hint?.TotalSeconds ?? 0)));

    public static TimeSpan Flat(TimeSpan? hint) =>
        hint is TimeSpan h && h > Floor ? h : Floor;
}

/// <summary>Poll cadence: fast while an agent works, slow when nothing is running, dimmed after long enough without an answer.</summary>
public sealed record Cadence(TimeSpan Active, TimeSpan Idle, TimeSpan StaleAfter)
{
    public static readonly Cadence Default = new(TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15));

    public bool ShouldPoll(bool busy, TimeSpan sinceLastAttempt) => busy || sinceLastAttempt >= Idle;
}
