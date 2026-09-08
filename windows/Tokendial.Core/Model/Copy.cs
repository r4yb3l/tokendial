using System.Globalization;
using Tokendial.Core.I18n;

namespace Tokendial.Core.Model;

/// <summary>The words under the dials, in the current language and the culture's time formats. Rules match docs/design/tokens.md.</summary>
public static class Copy
{
    /// <summary>"Resets in 51 min" under an hour, "Resets Thu 12:00 AM" within a week, "Resets Sep 28" beyond, "Resetting…" once passed.</summary>
    public static string Reset(DateTimeOffset resetsAt, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var seconds = (resetsAt - now).TotalSeconds;
        if (seconds <= 0) return Strings.T("copy.resetting");
        var minutes = (int)Math.Round(seconds / 60, MidpointRounding.AwayFromZero);
        if (minutes < 60) return Strings.Plural("copy.resetIn", Math.Max(1, minutes));
        var local = TimeZoneInfo.ConvertTime(resetsAt, zone);
        var time = CalendarDaysBetween(now, resetsAt, zone) >= 7 ? MonthDay(local) : $"{local.ToString("ddd", Strings.Culture)} {ShortTime(local)}";
        return Strings.T("copy.resetAt", ("time", time));
    }

    /// <summary>"just now" under 45 s, "6 min", "1 hr", "1 hr 5 min".</summary>
    public static string Elapsed(DateTimeOffset since, DateTimeOffset now)
    {
        var seconds = Math.Max(0, (now - since).TotalSeconds);
        if (seconds < 45) return Strings.T("copy.justNow");
        var minutes = (int)Math.Round(seconds / 60, MidpointRounding.AwayFromZero);
        if (minutes < 60) return Strings.T("copy.minutes", ("n", Math.Max(1, minutes)));
        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? Strings.T("copy.hours", ("n", hours)) : Strings.T("copy.hoursMinutes", ("h", hours), ("m", rest));
    }

    /// <summary>The elapsed span as an age: "6 min ago", or "just now".</summary>
    public static string Ago(DateTimeOffset since, DateTimeOffset now)
    {
        var text = Elapsed(since, now);
        return text == Strings.T("copy.justNow") ? text : Strings.T("copy.ago", ("elapsed", text));
    }

    /// <summary>"Paused until 4:13 PM": the vendor's own clock, the way its banner writes it.</summary>
    public static string Until(string reason, DateTimeOffset? until, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        if (until is not DateTimeOffset at || at <= now) return reason;
        zone ??= TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(at, zone);
        var time = CalendarDaysBetween(now, at, zone) >= 1 ? $"{local.ToString("ddd", Strings.Culture)} {ShortTime(local)}" : ShortTime(local);
        return Strings.T("copy.until", ("reason", reason), ("time", time));
    }

    public static int CalendarDaysBetween(DateTimeOffset from, DateTimeOffset to, TimeZoneInfo zone) =>
        (TimeZoneInfo.ConvertTime(to, zone).Date - TimeZoneInfo.ConvertTime(from, zone).Date).Days;

    /// <summary>The culture's short time: "12:00 AM" in US English, "00:00" in most others.</summary>
    public static string ShortTime(DateTimeOffset local) => local.ToString(Strings.Culture.DateTimeFormat.ShortTimePattern, Strings.Culture);

    /// <summary>"Sep 28" in English, "28 sept" where the day leads.</summary>
    public static string MonthDay(DateTimeOffset local)
    {
        var pattern = Strings.Culture.DateTimeFormat.MonthDayPattern;
        var dayFirst = pattern.IndexOf('d') < pattern.IndexOf('M');
        return local.ToString(dayFirst ? "d MMM" : "MMM d", Strings.Culture);
    }
}
