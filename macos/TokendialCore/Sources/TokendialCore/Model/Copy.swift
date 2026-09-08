import Foundation

/// The words under the dials. English, invariant, matching docs/design/tokens.md.
public enum Copy {
    private static func formatter(_ format: String, _ zone: TimeZone) -> DateFormatter {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = zone
        f.dateFormat = format
        return f
    }

    /// "Resets in 51 min" under an hour, "Resets Thu 12:00 AM" within a week, "Resets Sep 28" beyond, "Resetting…" once passed.
    public static func reset(_ resetsAt: Date, now: Date, zone: TimeZone = .current) -> String {
        let seconds = resetsAt.timeIntervalSince(now)
        if seconds <= 0 { return "Resetting…" }
        let minutes = Int((seconds / 60).rounded(.toNearestOrAwayFromZero))
        if minutes < 60 { return "Resets in \(max(1, minutes)) min" }
        return calendarDaysBetween(now, resetsAt, zone) >= 7
            ? "Resets \(formatter("MMM d", zone).string(from: resetsAt))"
            : "Resets \(formatter("EEE h:mm a", zone).string(from: resetsAt))"
    }

    /// "just now" under 45 s, "6 min", "1 hr", "1 hr 5 min".
    public static func elapsed(_ since: Date, now: Date) -> String {
        let seconds = max(0, now.timeIntervalSince(since))
        if seconds < 45 { return "just now" }
        let minutes = Int((seconds / 60).rounded(.toNearestOrAwayFromZero))
        if minutes < 60 { return "\(max(1, minutes)) min" }
        let hours = minutes / 60
        let rest = minutes % 60
        return rest == 0 ? "\(hours) hr" : "\(hours) hr \(rest) min"
    }

    /// The elapsed span as an age: "6 min ago", or "just now".
    public static func ago(_ since: Date, now: Date) -> String {
        let text = elapsed(since, now: now)
        return text == "just now" ? text : "\(text) ago"
    }

    /// "Paused until 4:13 PM": the vendor's own clock, the way its banner writes it.
    public static func until(_ reason: String, _ until: Date?, now: Date, zone: TimeZone = .current) -> String {
        guard let at = until, at > now else { return reason }
        let format = calendarDaysBetween(now, at, zone) >= 1 ? "EEE h:mm a" : "h:mm a"
        return "\(reason) until \(formatter(format, zone).string(from: at))"
    }

    public static func calendarDaysBetween(_ from: Date, _ to: Date, _ zone: TimeZone) -> Int {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = zone
        let a = calendar.startOfDay(for: from)
        let b = calendar.startOfDay(for: to)
        return calendar.dateComponents([.day], from: a, to: b).day ?? 0
    }
}
