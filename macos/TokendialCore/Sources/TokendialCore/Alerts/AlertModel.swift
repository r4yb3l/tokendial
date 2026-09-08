import Foundation

/// Tunable rules of the alert engine. Defaults match docs/alerts/spec.md.
public struct AlertConfig: Equatable, Sendable {
    public var thresholds: [Int]
    public var resetLead: TimeInterval
    public var resetLeadMinPct: Int
    public var waitingDebounce: TimeInterval
    public var waitingRepeat: TimeInterval
    public var perProviderCooldown: TimeInterval

    public init(thresholds: [Int], resetLead: TimeInterval, resetLeadMinPct: Int, waitingDebounce: TimeInterval, waitingRepeat: TimeInterval, perProviderCooldown: TimeInterval) {
        self.thresholds = thresholds
        self.resetLead = resetLead
        self.resetLeadMinPct = resetLeadMinPct
        self.waitingDebounce = waitingDebounce
        self.waitingRepeat = waitingRepeat
        self.perProviderCooldown = perProviderCooldown
    }

    public static let `default` = AlertConfig(thresholds: [50, 80, 95], resetLead: 600, resetLeadMinPct: 50, waitingDebounce: 20, waitingRepeat: 300, perProviderCooldown: 60)
}

public enum SessionActivity: String, Codable, Sendable {
    case working
    case waiting
    case done
}

/// What the host tells the engine. Time is always explicit.
public enum AlertEvent: Sendable {
    case usage(at: Date, provider: String, window: String, usedPct: Double, resetsAt: Date?, limited: Bool)
    case session(at: Date, provider: String, sessionId: String, state: SessionActivity)
    case hover(at: Date, on: Bool)
    case tick(at: Date)
    case restart(at: Date)

    public var at: Date {
        switch self {
        case .usage(let at, _, _, _, _, _), .session(let at, _, _, _), .hover(let at, _), .tick(let at), .restart(let at): return at
        }
    }
}

/// Declared in emission order: within one reduction, limits go out before thresholds, and so on.
public enum AlertKind: String, Codable, CaseIterable, Sendable {
    case limit
    case threshold
    case resetSoon
    case resetDone
    case waiting

    var order: Int { AlertKind.allCases.firstIndex(of: self)! }
}

/// One notification the host should show.
public struct Alert: Equatable, Codable, Sendable {
    public var kind: AlertKind
    public var provider: String
    public var window: String?
    public var pct: Int?
    public var resetsAt: Date?
    public var sessionId: String?

    public init(kind: AlertKind, provider: String, window: String? = nil, pct: Int? = nil, resetsAt: Date? = nil, sessionId: String? = nil) {
        self.kind = kind
        self.provider = provider
        self.window = window
        self.pct = pct
        self.resetsAt = resetsAt
        self.sessionId = sessionId
    }
}

/// The platform's notification centre. Contains no logic.
public protocol AlertSink: AnyObject {
    func deliver(_ alert: Alert)
}
