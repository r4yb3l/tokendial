import Foundation

public enum ForecastKind: Sendable {
    /// At this pace the window empties before it resets.
    case runsOut
    /// At this pace the window lasts until its reset.
    case lastsUntilReset
}

/// Where the current pace takes a window. A least-squares slope over the recent samples of one epoch,
/// because vendors publish whole percentages and two neighbouring samples read as a flat line or a cliff.
public struct Forecast: Equatable, Sendable {
    public var kind: ForecastKind
    public var runsOutAt: Date?

    public init(kind: ForecastKind, runsOutAt: Date?) {
        self.kind = kind
        self.runsOutAt = runsOutAt
    }

    public static let minSpan: TimeInterval = 10 * 60
    public static let lookback: TimeInterval = 60 * 60
    public static let horizon: TimeInterval = 24 * 60 * 60
    /// Half a percentage point an hour: anything slower is a resting window, not a pace.
    public static let minSlopePerHour = 0.005

    public static func of(_ samples: [UsageSample], now: Date, resetsAt: Date?) -> Forecast? {
        let recent = samples
            .filter { now.timeIntervalSince($0.takenAt) <= lookback && $0.takenAt <= now }
            .sorted { $0.takenAt < $1.takenAt }
        guard recent.count >= 3, let first = recent.first, let last = recent.last else { return nil }
        guard last.takenAt.timeIntervalSince(first.takenAt) >= minSpan else { return nil }
        guard last.usedFraction < 1 else { return nil }

        let slope = slopePerHour(recent)
        guard slope.isFinite, slope > minSlopePerHour else { return nil }
        let hoursLeft = (1 - last.usedFraction) / slope
        guard hoursLeft.isFinite, hoursLeft >= 0 else { return nil }
        let runsOutAt = last.takenAt.addingTimeInterval(hoursLeft * 3600)

        if let reset = resetsAt, reset > now {
            return runsOutAt < reset ? Forecast(kind: .runsOut, runsOutAt: runsOutAt) : Forecast(kind: .lastsUntilReset, runsOutAt: nil)
        }
        return runsOutAt.timeIntervalSince(now) <= horizon ? Forecast(kind: .runsOut, runsOutAt: runsOutAt) : nil
    }

    /// Ordinary least squares of fraction against hours since the first sample.
    public static func slopePerHour(_ samples: [UsageSample]) -> Double {
        guard let origin = samples.first?.takenAt else { return .nan }
        let xs = samples.map { $0.takenAt.timeIntervalSince(origin) / 3600 }
        let ys = samples.map { $0.usedFraction }
        let meanX = xs.reduce(0, +) / Double(xs.count)
        let meanY = ys.reduce(0, +) / Double(ys.count)
        let covariance = zip(xs, ys).reduce(0) { $0 + ($1.0 - meanX) * ($1.1 - meanY) }
        let variance = xs.reduce(0) { $0 + ($1 - meanX) * ($1 - meanX) }
        return variance == 0 ? .nan : covariance / variance
    }
}
