import Foundation

/// The host side of the alert engine: turns readings and sessions into events, runs the reducer under
/// one lock, persists its state, and hands alerts to the sink. The kinds the user switched off are the
/// engine's to drop, before they can hold or start a cooldown.
public final class AlertCoordinator {
    private let lock = NSLock()
    private let sink: AlertSink
    private let now: () -> Date
    private let stateFile: URL?
    private var engine: AlertEngine
    private var timer: DispatchSourceTimer?

    public init(sink: AlertSink, config: AlertConfig = .default, stateFile: URL? = nil, now: @escaping () -> Date = Date.init, wants: @escaping (AlertKind) -> Bool = { _ in true }) {
        self.sink = sink
        self.now = now
        self.stateFile = stateFile
        engine = Self.load(stateFile, config, wants)
        apply(.restart(at: now()))
    }

    public static var defaultStateFile: URL { Paths.appSupport.appendingPathComponent("alerts.json") }

    public func start(tick: TimeInterval = 15) {
        let timer = DispatchSource.makeTimerSource(queue: .global(qos: .utility))
        timer.schedule(deadline: .now() + tick, repeating: tick)
        timer.setEventHandler { [weak self] in guard let self else { return }; self.apply(.tick(at: self.now())) }
        timer.resume()
        self.timer = timer
    }

    public func stop() { timer?.cancel(); timer = nil }

    /// Thresholds or switches changed: the engine keeps its epochs and only the rules move.
    public func reconfigure(_ config: AlertConfig, wants: @escaping (AlertKind) -> Bool) {
        lock.withLock {
            engine = AlertEngine.load(engine.save(), config: config, wants: wants)
        }
    }

    public func onReadings(_ readings: [ProviderReading]) {
        let at = now()
        for reading in readings where reading.status == .live {
            guard let window = reading.headline, let used = window.usedFraction else { continue }
            apply(.usage(at: at, provider: reading.providerId, window: window.id, usedPct: (used * 10000).rounded() / 100, resetsAt: window.resetsAt, limited: reading.block != nil))
        }
    }

    /// One snapshot of every session, applied whole under the lock: applied piecemeal from two threads, one
    /// snapshot could close a session another had just reported waiting.
    public func onActivities(_ activities: [String: Activity]) {
        let alerts: [Alert] = lock.withLock {
            let at = now()
            var seen = Set<String>()
            var out: [Alert] = []
            for (provider, activity) in activities {
                for session in activity.sessions {
                    seen.insert(session.id)
                    let state: SessionActivity
                    switch session.state { case .waiting: state = .waiting; case .working: state = .working; case .idle: state = .done }
                    out += reduce(.session(at: at, provider: provider, sessionId: session.id, state: state))
                }
            }
            for entry in engine.waiting where !seen.contains(entry.sessionId) {
                out += reduce(.session(at: at, provider: entry.provider, sessionId: entry.sessionId, state: .done))
            }
            return out
        }
        deliver(alerts)
    }

    public func onHover(_ on: Bool) { apply(.hover(at: now(), on: on)) }
    public func tick() { apply(.tick(at: now())) }

    private func apply(_ event: AlertEvent) {
        deliver(lock.withLock { reduce(event) })
    }

    /// Runs one event through the engine and persists what it changed. The caller holds the lock.
    private func reduce(_ event: AlertEvent) -> [Alert] {
        let out = engine.reduce(event)
        switch event {
        case .usage, .restart: persist()
        default: if !out.isEmpty { persist() }
        }
        return out
    }

    /// Outside the lock, so a slow notification centre never stalls the next event.
    private func deliver(_ alerts: [Alert]) {
        for alert in alerts {
            Log.alerts.info("\(alert.kind.rawValue) \(alert.provider)\(alert.window.map { "/" + $0 } ?? "")\(alert.pct.map { " \($0)%" } ?? "")")
            sink.deliver(alert)
        }
    }

    private func persist() {
        guard let stateFile else { return }
        do {
            try FileManager.default.createDirectory(at: stateFile.deletingLastPathComponent(), withIntermediateDirectories: true)
            try engine.save().write(to: stateFile, options: .atomic)
        } catch { Log.alerts.error("persist: \(error.localizedDescription)") }
    }

    private static func load(_ file: URL?, _ config: AlertConfig, _ wants: @escaping (AlertKind) -> Bool) -> AlertEngine {
        if let file, let data = try? Data(contentsOf: file) { return AlertEngine.load(data, config: config, wants: wants) }
        return AlertEngine(config: config, wants: wants)
    }
}
