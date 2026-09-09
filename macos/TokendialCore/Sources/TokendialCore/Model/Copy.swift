import Foundation

/// The words under the dials, in the current language and the locale's time formats. Rules match docs/design/tokens.md.
public enum Copy {
    private static func formatter(_ format: String, _ zone: TimeZone) -> DateFormatter {
        let f = DateFormatter()
        f.locale = Strings.locale
        f.timeZone = zone
        f.dateFormat = format
        return f
    }

    /// The locale's short time: "12:00 AM" in US English, "00:00" in most others.
    private static func shortTime(_ date: Date, _ zone: TimeZone) -> String {
        let f = DateFormatter()
        f.locale = Strings.locale
        f.timeZone = zone
        f.timeStyle = .short
        f.dateStyle = .none
        return f.string(from: date)
    }

    /// "Sep 28" in English, "28 sept" where the day leads.
    private static func monthDay(_ date: Date, _ zone: TimeZone) -> String {
        let template = DateFormatter.dateFormat(fromTemplate: "MMM d", options: 0, locale: Strings.locale) ?? "MMM d"
        return formatter(template, zone).string(from: date)
    }

    /// "Resets in 51 min" under an hour, "Resets Thu 12:00 AM" within a week, "Resets Sep 28" beyond, "Resetting…" once passed.
    public static func reset(_ resetsAt: Date, now: Date, zone: TimeZone = .current) -> String {
        let seconds = resetsAt.timeIntervalSince(now)
        if seconds <= 0 { return Strings.t("copy.resetting") }
        let minutes = Int((seconds / 60).rounded(.toNearestOrAwayFromZero))
        if minutes < 60 { return Strings.plural("copy.resetIn", max(1, minutes)) }
        let time = calendarDaysBetween(now, resetsAt, zone) >= 7
            ? monthDay(resetsAt, zone)
            : "\(formatter("EEE", zone).string(from: resetsAt)) \(shortTime(resetsAt, zone))"
        return Strings.t("copy.resetAt", ["time": time])
    }

    /// Where the pace leads: the hour it empties, with the weekday in front when that is another day.
    public static func forecast(_ forecast: Forecast, now: Date, zone: TimeZone = .current) -> String {
        guard forecast.kind == .runsOut, let at = forecast.runsOutAt else { return Strings.t("copy.forecastLasts") }
        let time = calendarDaysBetween(now, at, zone) >= 1
            ? "\(formatter("EEE", zone).string(from: at)) \(shortTime(at, zone))"
            : shortTime(at, zone)
        return Strings.t("copy.forecastRunsOut", ["time": time])
    }

    /// "just now" under 45 s, "6 min", "1 hr", "1 hr 5 min".
    public static func elapsed(_ since: Date, now: Date) -> String {
        let seconds = max(0, now.timeIntervalSince(since))
        if seconds < 45 { return Strings.t("copy.justNow") }
        let minutes = Int((seconds / 60).rounded(.toNearestOrAwayFromZero))
        if minutes < 60 { return Strings.t("copy.minutes", ["n": max(1, minutes)]) }
        let hours = minutes / 60
        let rest = minutes % 60
        return rest == 0 ? Strings.t("copy.hours", ["n": hours]) : Strings.t("copy.hoursMinutes", ["h": hours, "m": rest])
    }

    /// The elapsed span as an age: "6 min ago", or "just now".
    public static func ago(_ since: Date, now: Date) -> String {
        let text = elapsed(since, now: now)
        return text == Strings.t("copy.justNow") ? text : Strings.t("copy.ago", ["elapsed": text])
    }

    /// "Paused until 4:13 PM": the vendor's own clock, the way its banner writes it.
    public static func until(_ reason: String, _ until: Date?, now: Date, zone: TimeZone = .current) -> String {
        guard let at = until, at > now else { return reason }
        let time = calendarDaysBetween(now, at, zone) >= 1 ? "\(formatter("EEE", zone).string(from: at)) \(shortTime(at, zone))" : shortTime(at, zone)
        return Strings.t("copy.until", ["reason": reason, "time": time])
    }

    public static func calendarDaysBetween(_ from: Date, _ to: Date, _ zone: TimeZone) -> Int {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = zone
        let a = calendar.startOfDay(for: from)
        let b = calendar.startOfDay(for: to)
        return calendar.dateComponents([.day], from: a, to: b).day ?? 0
    }
}
