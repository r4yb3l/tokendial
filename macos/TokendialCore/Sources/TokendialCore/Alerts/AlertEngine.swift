import Foundation

/// The deterministic reducer specified in docs/alerts/spec.md. It owns no clock and no thread:
/// the host feeds it events and delivers what comes back. Every (provider, window) lives in an
/// epoch keyed by its reset time; a new epoch re-arms everything.
public final class AlertEngine {
    private let config: AlertConfig
    private var state: AlertState

    public init(config: AlertConfig = .default, state: AlertState? = nil) {
        self.config = config
        self.state = state ?? AlertState()
    }

    public static func load(_ json: Data, config: AlertConfig = .default) -> AlertEngine {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        let state = try? decoder.decode(AlertState.self, from: json)
        return AlertEngine(config: config, state: state?.schema == 1 ? state : nil)
    }

    public func save() -> Data {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        return (try? encoder.encode(state)) ?? Data()
    }

    /// Sessions the engine still believes are waiting; the host reconciles them against what it can see.
    public var waiting: [(provider: String, sessionId: String)] { state.waiting.map { ($0.provider, $0.sessionId) } }

    public func reduce(_ event: AlertEvent) -> [Alert] {
        var candidates: [Alert] = []
        switch event {
        case .usage(let at, let provider, let window, let usedPct, let resetsAt, let limited):
            candidates += applyUsage(at: at, provider: provider, window: window, usedPct: usedPct, resetsAt: resetsAt, limited: limited)
        case .session(let at, let provider, let sessionId, let activity):
            applySession(at: at, provider: provider, sessionId: sessionId, activity: activity)
        case .hover(_, let on):
            state.hoverOn = on
        case .restart(let at):
            prune(now: at)
        case .tick:
            break
        }
        candidates += timeChecks(now: event.at)
        return gate(candidates, now: event.at)
    }

    private func applyUsage(at: Date, provider: String, window: String, usedPct: Double, resetsAt: Date?, limited: Bool) -> [Alert] {
        let key = "\(provider)|\(window)"
        var index = state.epochs.firstIndex { $0.key == key }
        let existing = index.map { state.epochs[$0] }
        let fresh = existing == nil || existing!.closed || !Self.sameReset(existing!.resetsAt, resetsAt) || existing!.lastPct - usedPct > 10
        if fresh {
            state.epochs.removeAll { $0.key == key }
            state.epochs.append(Epoch(key: key, provider: provider, window: window, resetsAt: resetsAt))
            index = state.epochs.count - 1
        }
        let i = index!
        state.epochs[i].lastPct = usedPct
        state.epochs[i].resetsAt = resetsAt

        if limited || usedPct >= 100 {
            for threshold in config.thresholds { state.epochs[i].fired.insert(String(threshold)) }
            if state.epochs[i].fired.insert("limit").inserted {
                return [Alert(kind: .limit, provider: provider, window: window, resetsAt: resetsAt)]
            }
            return []
        }

        let crossed = config.thresholds.filter { Double($0) <= usedPct && !state.epochs[i].fired.contains(String($0)) }
        if crossed.isEmpty { return [] }
        for threshold in crossed { state.epochs[i].fired.insert(String(threshold)) }
        return [Alert(kind: .threshold, provider: provider, window: window, pct: crossed.max())]
    }

    private func applySession(at: Date, provider: String, sessionId: String, activity: SessionActivity) {
        let key = "\(provider)|\(sessionId)"
        if activity == .waiting {
            if !state.waiting.contains(where: { $0.key == key }) {
                state.waiting.append(WaitingEntry(key: key, provider: provider, sessionId: sessionId, since: at))
            }
            return
        }
        state.waiting.removeAll { $0.key == key }
    }

    private func timeChecks(now: Date) -> [Alert] {
        var out: [Alert] = []
        for i in state.epochs.indices {
            guard !state.epochs[i].closed, let resetsAt = state.epochs[i].resetsAt else { continue }
            let epoch = state.epochs[i]
            if !epoch.resetSoonFired && !epoch.fired.contains("limit") && resetsAt > now
                && resetsAt.timeIntervalSince(now) <= config.resetLead && epoch.lastPct >= Double(config.resetLeadMinPct) {
                state.epochs[i].resetSoonFired = true
                out.append(Alert(kind: .resetSoon, provider: epoch.provider, window: epoch.window, resetsAt: resetsAt))
            }
            if now >= resetsAt && epoch.fired.contains("limit") && !epoch.resetDoneFired {
                state.epochs[i].resetDoneFired = true
                state.epochs[i].closed = true
                out.append(Alert(kind: .resetDone, provider: epoch.provider, window: epoch.window))
            }
        }
        for i in state.waiting.indices {
            let waiting = state.waiting[i]
            let due = waiting.lastFired.map { $0.addingTimeInterval(config.waitingRepeat) } ?? waiting.since.addingTimeInterval(config.waitingDebounce)
            if now < due { continue }
            state.waiting[i].lastFired = now
            out.append(Alert(kind: .waiting, provider: waiting.provider, sessionId: waiting.sessionId))
        }
        return out
    }

    /// Hover and cooldown decide what goes out now, what waits, and what is dropped.
    private func gate(_ candidates: [Alert], now: Date) -> [Alert] {
        var emitted: [Alert] = []
        if !state.hoverOn {
            for held in state.held {
                if inCooldown(held.provider, now: now) { continue }
                state.held.removeAll { $0 == held }
                emitted.append(held)
            }
        }
        for alert in candidates {
            if alert.kind == .threshold {
                if state.hoverOn || inCooldown(alert.provider, now: now) { continue }
                emitted.append(alert)
                state.cooldownUntil[alert.provider] = now.addingTimeInterval(config.perProviderCooldown)
                continue
            }
            if state.hoverOn || inCooldown(alert.provider, now: now) {
                state.held.append(alert)
                continue
            }
            emitted.append(alert)
            state.cooldownUntil[alert.provider] = now.addingTimeInterval(config.perProviderCooldown)
        }
        return emitted.sorted { a, b in
            if a.kind.order != b.kind.order { return a.kind.order < b.kind.order }
            return a.provider.utf8.lexicographicallyPrecedes(b.provider.utf8)
        }
    }

    /// Vendors compute resets_at on the fly, so consecutive polls differ by milliseconds; a real rollover moves it by hours.
    public static let resetTolerance: TimeInterval = 60

    private static func sameReset(_ a: Date?, _ b: Date?) -> Bool {
        guard let a, let b else { return a == b }
        return abs(a.timeIntervalSince(b)) <= resetTolerance
    }

    private func inCooldown(_ provider: String, now: Date) -> Bool {
        if let until = state.cooldownUntil[provider] { return until > now }
        return false
    }

    private func prune(now: Date) {
        state.epochs.removeAll { epoch in
            if let at = epoch.resetsAt { return at < now.addingTimeInterval(-24 * 3600) }
            return false
        }
    }
}

/// Everything the engine remembers, shaped for JSON.
public struct AlertState: Codable, Sendable {
    public var schema: Int = 1
    public var epochs: [Epoch] = []
    public var waiting: [WaitingEntry] = []
    public var held: [Alert] = []
    public var cooldownUntil: [String: Date] = [:]
    public var hoverOn: Bool = false

    public init() {}
}

public struct Epoch: Codable, Sendable {
    public var key: String
    public var provider: String
    public var window: String
    public var resetsAt: Date?
    public var lastPct: Double = 0
    public var fired: Set<String> = []
    public var resetSoonFired = false
    public var resetDoneFired = false
    public var closed = false

    init(key: String, provider: String, window: String, resetsAt: Date?) {
        self.key = key
        self.provider = provider
        self.window = window
        self.resetsAt = resetsAt
    }
}

public struct WaitingEntry: Codable, Sendable {
    public var key: String
    public var provider: String
    public var sessionId: String
    public var since: Date
    public var lastFired: Date?
}
