using System.Globalization;

namespace Tokendial.Core.Model;

/// <summary>The words under the dials. English, invariant, matching docs/design/tokens.md.</summary>
public static class Copy
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>"Resets in 51 min" under an hour, "Resets Thu 12:00 AM" within a week, "Resets Sep 28" beyond, "Resetting…" once passed.</summary>
    public static string Reset(DateTimeOffset resetsAt, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var seconds = (resetsAt - now).TotalSeconds;
        if (seconds <= 0) return "Resetting…";
        var minutes = (int)Math.Round(seconds / 60, MidpointRounding.AwayFromZero);
        if (minutes < 60) return $"Resets in {Math.Max(1, minutes)} min";
        var local = TimeZoneInfo.ConvertTime(resetsAt, zone);
        return CalendarDaysBetween(now, resetsAt, zone) >= 7
            ? $"Resets {local.ToString("MMM d", Invariant)}"
            : $"Resets {local.ToString("ddd h:mm tt", Invariant)}";
    }

    /// <summary>"just now" under 45 s, "6 min", "1 hr", "1 hr 5 min".</summary>
    public static string Elapsed(DateTimeOffset since, DateTimeOffset now)
    {
        var seconds = Math.Max(0, (now - since).TotalSeconds);
        if (seconds < 45) return "just now";
        var minutes = (int)Math.Round(seconds / 60, MidpointRounding.AwayFromZero);
        if (minutes < 60) return $"{Math.Max(1, minutes)} min";
        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"{hours} hr" : $"{hours} hr {rest} min";
    }

    /// <summary>The elapsed span as an age: "6 min ago", or "just now".</summary>
    public static string Ago(DateTimeOffset since, DateTimeOffset now)
    {
        var text = Elapsed(since, now);
        return text == "just now" ? text : $"{text} ago";
    }

    /// <summary>"Paused until 4:13 PM": the vendor's own clock, the way its banner writes it.</summary>
    public static string Until(string reason, DateTimeOffset? until, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        if (until is not DateTimeOffset at || at <= now) return reason;
        zone ??= TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(at, zone);
        var format = CalendarDaysBetween(now, at, zone) >= 1 ? "ddd h:mm tt" : "h:mm tt";
        return $"{reason} until {local.ToString(format, Invariant)}";
    }

    public static int CalendarDaysBetween(DateTimeOffset from, DateTimeOffset to, TimeZoneInfo zone) =>
        (TimeZoneInfo.ConvertTime(to, zone).Date - TimeZoneInfo.ConvertTime(from, zone).Date).Days;
}
