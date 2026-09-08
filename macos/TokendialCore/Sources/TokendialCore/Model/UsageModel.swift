import Foundation

/// Whether a number came from the vendor or was worked out locally. Derived readings are shown with a tilde and no dial arc.
public enum Fidelity: String, Codable, Sendable {
    case official
    case derived
}

/// Colour band of a reading. Critical starts at 0.80 so the colour change and the 80 % alert coincide.
public enum Band: Sendable {
    case ample
    case watch
    case critical

    public static func of(_ usedFraction: Double) -> Band {
        if usedFraction < 0.50 { return .ample }
        if usedFraction < 0.80 { return .watch }
        return .critical
    }
}

/// How much to trust the reading on screen right now.
public enum ReadingStatus: Equatable, Codable, Sendable {
    case live
    case stale(since: Date)
    case needsSignIn
    case unsupported(why: String)
    case failed(why: String)

    public var isStale: Bool { if case .stale = self { return true } else { return false } }
    public var staleSince: Date? { if case .stale(let since) = self { return since } else { return nil } }
}

/// One metered window: a fraction used, or a bare count when the vendor publishes no ceiling.
public struct UsageWindow: Equatable, Codable, Sendable {
    public var id: String
    public var label: String
    public var usedFraction: Double?
    public var count: Int?
    public var resetsAt: Date?

    public init(id: String, label: String, usedFraction: Double? = nil, count: Int? = nil, resetsAt: Date? = nil) {
        self.id = id
        self.label = label
        self.usedFraction = usedFraction
        self.count = count
        self.resetsAt = resetsAt
    }

    /// "63% used · 37% left", or "~7 requests today" for a count.
    public func summary(_ fidelity: Fidelity) -> String {
        if let f = usedFraction {
            let used = Int((f * 100).rounded(.toNearestOrAwayFromZero))
            let tilde = fidelity == .derived ? "~" : ""
            return tilde + Strings.t("copy.usedLeft", ["used": used, "left": max(0, 100 - used)])
        }
        if let n = count { return Strings.plural("copy.requests", n) }
        return Strings.t("copy.noReading")
    }
}

/// Set when a limit has been reached even where the headline still shows room.
public struct Blocked: Equatable, Codable, Sendable {
    public var reason: String
    public var until: Date?

    public init(reason: String, until: Date?) {
        self.reason = reason
        self.until = until
    }
}

/// Everything the panel shows for one provider.
public struct ProviderReading: Equatable, Codable, Sendable {
    public var providerId: String
    public var displayName: String
    public var fidelity: Fidelity
    public var status: ReadingStatus
    public var windows: [UsageWindow]
    public var headlineId: String?
    public var block: Blocked?

    public init(providerId: String, displayName: String, fidelity: Fidelity, status: ReadingStatus, windows: [UsageWindow], headlineId: String? = nil, block: Blocked? = nil) {
        self.providerId = providerId
        self.displayName = displayName
        self.fidelity = fidelity
        self.status = status
        self.windows = windows
        self.headlineId = headlineId
        self.block = block
    }

    /// The window the dial means. Declared by the provider; when it is missing the dial shows no reading rather than another window.
    public var headline: UsageWindow? {
        guard let id = headlineId else { return windows.first }
        return windows.first { $0.id == id }
    }

    public var headlineFraction: Double? { headline?.usedFraction }
    public var hasReading: Bool { !windows.isEmpty }

    /// "63%", a bare count, or a dash.
    public var headlineText: String {
        if let f = headlineFraction { return "\(Int((f * 100).rounded(.toNearestOrAwayFromZero)))%" }
        if let n = headline?.count { return String(n) }
        return "—"
    }

    public var band: Band? {
        if block != nil { return .critical }
        if let f = headlineFraction { return Band.of(f) }
        return nil
    }
}
