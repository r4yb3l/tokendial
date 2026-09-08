import Foundation

/// The host side of the alert engine: turns readings and sessions into events, runs the reducer under
/// one lock, persists its state, and hands alerts to the sink after filtering the kinds the user switched off.
public final class AlertCoordinator {
    private let lock = NSLock()
    private let sink: AlertSink
    private let now: () -> Date
    private let stateFile: URL?
    private var engine: AlertEngine
    private var wants: (AlertKind) -> Bool
    private var known: [String: String] = [:]
    private var timer: DispatchSourceTimer?

    public init(sink: AlertSink, config: AlertConfig = .default, stateFile: URL? = nil, now: @escaping () -> Date = Date.init, wants: @escaping (AlertKind) -> Bool = { _ in true }) {
        self.sink = sink
        self.now = now
        self.stateFile = stateFile
        self.wants = wants
        engine = Self.load(stateFile, config)
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
            engine = AlertEngine.load(engine.save(), config: config)
            self.wants = wants
        }
    }

    public func onReadings(_ readings: [ProviderReading]) {
        let at = now()
        for reading in readings where reading.status == .live {
            let limited = reading.block != nil
            for window in reading.windows {
                guard let used = window.usedFraction else { continue }
                let isHeadline = reading.headline?.id == window.id
                apply(.usage(at: at, provider: reading.providerId, window: window.id, usedPct: (used * 10000).rounded() / 100, resetsAt: window.resetsAt, limited: limited && isHeadline))
            }
        }
    }

    public func onActivities(_ activities: [String: Activity]) {
        let at = now()
        var seen: [String: String] = [:]
        for (provider, activity) in activities {
            for session in activity.sessions {
                seen[session.id] = provider
                let state: SessionActivity
                switch session.state { case .waiting: state = .waiting; case .working: state = .working; case .idle: state = .done }
                apply(.session(at: at, provider: provider, sessionId: session.id, state: state))
            }
        }
        let gone = lock.withLock { known.filter { seen[$0.key] == nil } }
        for (id, provider) in gone { apply(.session(at: at, provider: provider, sessionId: id, state: .done)) }
        lock.withLock { known = seen }
    }

    public func onHover(_ on: Bool) { apply(.hover(at: now(), on: on)) }
    public func tick() { apply(.tick(at: now())) }

    private func apply(_ event: AlertEvent) {
        let alerts: [Alert] = lock.withLock {
            let out = engine.reduce(event).filter { wants($0.kind) }
            switch event {
            case .usage, .restart: persist()
            default: if !out.isEmpty { persist() }
            }
            return out
        }
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

    private static func load(_ file: URL?, _ config: AlertConfig) -> AlertEngine {
        if let file, let data = try? Data(contentsOf: file) { return AlertEngine.load(data, config: config) }
        return AlertEngine(config: config)
    }
}
