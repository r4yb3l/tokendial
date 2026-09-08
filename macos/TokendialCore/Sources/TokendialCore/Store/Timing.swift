import Foundation

/// Retry-After is seconds or an HTTP date; a past date is zero.
public enum RetryAfterHeader {
    private static let httpDate: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(identifier: "GMT")
        f.dateFormat = "EEE, dd MMM yyyy HH:mm:ss zzz"
        return f
    }()

    public static func parse(_ value: String?, now: Date) -> TimeInterval? {
        guard let value else { return nil }
        let text = value.trimmingCharacters(in: .whitespaces)
        if text.isEmpty { return nil }
        if let seconds = Double(text), seconds.isFinite { return max(0, seconds) }
        if let date = httpDate.date(from: text) { return max(0, date.timeIntervalSince(now)) }
        return nil
    }

    public static func from(_ response: HTTPURLResponse, now: Date) -> TimeInterval? {
        parse(response.value(forHTTPHeaderField: "Retry-After"), now: now)
    }
}

/// How long to wait after a 429. The vendor's hint only raises the floor: some answer Retry-After: 0.
/// Starts at a minute, doubles per consecutive 429, caps at fifteen minutes so it always recovers on its own.
public enum Backoff {
    public static let floor: TimeInterval = 60
    public static let ceiling: TimeInterval = 15 * 60

    public static func exponential(_ consecutive: Int, hint: TimeInterval?) -> TimeInterval {
        min(ceiling, max(floor * pow(2, Double(min(consecutive, 4))), hint ?? 0))
    }

    public static func flat(_ hint: TimeInterval?) -> TimeInterval {
        if let hint, hint > floor { return hint }
        return floor
    }
}

/// Poll cadence: fast while an agent works, slow when nothing is running, dimmed after long enough without an answer.
public struct Cadence: Sendable {
    public var active: TimeInterval
    public var idle: TimeInterval
    public var staleAfter: TimeInterval

    public init(active: TimeInterval, idle: TimeInterval, staleAfter: TimeInterval) {
        self.active = active
        self.idle = idle
        self.staleAfter = staleAfter
    }

    public static let `default` = Cadence(active: 60, idle: 5 * 60, staleAfter: 15 * 60)

    public func shouldPoll(busy: Bool, sinceLastAttempt: TimeInterval) -> Bool { busy || sinceLastAttempt >= idle }
}
