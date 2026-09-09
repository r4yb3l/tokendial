import Foundation

/// One reading of one window, kept only long enough to see which way it is heading.
public struct UsageSample: Equatable, Sendable {
    public var takenAt: Date
    public var usedFraction: Double
    public var resetsAt: Date?

    public init(takenAt: Date, usedFraction: Double, resetsAt: Date?) {
        self.takenAt = takenAt
        self.usedFraction = usedFraction
        self.resetsAt = resetsAt
    }
}

/// A short ring of samples per provider window, in memory only. A window starts a new ring when its reset
/// moves or its fraction falls, which is how a rollover looks from the outside; the ring never outlives the
/// epoch.
public final class UsageHistory {
    public static let maxSamples = 180
    public static let maxAge: TimeInterval = 3 * 60 * 60
    public static let resetTolerance: TimeInterval = 60
    public static let dropThatEndsAnEpoch = 0.10

    private struct Key: Hashable {
        var provider: String
        var window: String
    }

    private var series: [Key: [UsageSample]] = [:]

    public init() {}

    /// One sample per window that carries a fraction; count-only windows are not forecastable.
    public func record(_ reading: ProviderReading, now: Date) {
        for window in reading.windows {
            guard let fraction = window.usedFraction else { continue }
            add(reading.providerId, window.id, UsageSample(takenAt: now, usedFraction: fraction, resetsAt: window.resetsAt))
        }
    }

    public func add(_ providerId: String, _ windowId: String, _ sample: UsageSample) {
        let key = Key(provider: providerId, window: windowId)
        var list = series[key] ?? []
        if let last = list.last, !Self.sameEpoch(last, sample) { list.removeAll() }
        list.append(sample)
        list.removeAll { sample.takenAt.timeIntervalSince($0.takenAt) > Self.maxAge }
        if list.count > Self.maxSamples { list.removeFirst(list.count - Self.maxSamples) }
        series[key] = list
    }

    public func samples(_ providerId: String, _ windowId: String) -> [UsageSample] {
        series[Key(provider: providerId, window: windowId)] ?? []
    }

    public func forget(_ providerId: String) {
        for key in series.keys where key.provider == providerId { series.removeValue(forKey: key) }
    }

    /// The same epoch as long as the reset stays put, within the tolerance the alert engine uses, and usage
    /// did not fall.
    public static func sameEpoch(_ previous: UsageSample, _ next: UsageSample) -> Bool {
        sameReset(previous.resetsAt, next.resetsAt) && previous.usedFraction - next.usedFraction <= dropThatEndsAnEpoch
    }

    private static func sameReset(_ a: Date?, _ b: Date?) -> Bool {
        guard let a else { return b == nil }
        guard let b else { return false }
        return abs(a.timeIntervalSince(b)) <= resetTolerance
    }
}
