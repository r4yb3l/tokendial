import Foundation
import Darwin

public enum SessionState: String, Codable, Sendable {
    case working
    case waiting
    case idle
}

/// One agent session, in display terms; each monitor parses its own tool's format.
public struct AgentSession: Equatable, Sendable {
    public var id: String
    public var name: String
    public var location: String
    public var state: SessionState
    public var waitingFor: String?
    public var since: Date

    public init(id: String, name: String, location: String, state: SessionState, waitingFor: String?, since: Date) {
        self.id = id
        self.name = name
        self.location = location
        self.state = state
        self.waitingFor = waitingFor
        self.since = since
    }
}

/// What a provider's sessions add up to. Waiting outranks working: it is the one state that wants something from you.
public struct Activity: Equatable, Sendable {
    public var state: SessionState
    public var sessions: [AgentSession]

    public static func of(_ sessions: [AgentSession]) -> Activity? {
        if sessions.isEmpty { return nil }
        let state: SessionState = sessions.contains { $0.state == .waiting } ? .waiting : sessions.contains { $0.state == .working } ? .working : .idle
        return Activity(state: state, sessions: sessions)
    }

    /// Waiting, then working, then idle; newest first within each.
    public var ordered: [AgentSession] {
        func rank(_ s: SessionState) -> Int { switch s { case .waiting: return 0; case .working: return 1; case .idle: return 2 } }
        return sessions.sorted { a, b in rank(a.state) != rank(b.state) ? rank(a.state) < rank(b.state) : a.since > b.since }
    }
}

/// Publishes one tool's live sessions from a background queue.
public protocol ActivityMonitor: AnyObject {
    var providerId: String { get }
    var sessions: [AgentSession] { get }
    var changed: (() -> Void)? { get set }
    func start()
    func stop()
}

/// Re-reads a source on a timer and publishes only real changes.
open class PolledMonitor: ActivityMonitor {
    private let lock = NSLock()
    private let interval: TimeInterval
    private var timer: DispatchSourceTimer?
    private var reading = false
    private var current: [AgentSession] = []

    public let providerId: String
    public var changed: (() -> Void)?

    public init(providerId: String, interval: TimeInterval) {
        self.providerId = providerId
        self.interval = interval
    }

    public var sessions: [AgentSession] { lock.lock(); defer { lock.unlock() }; return current }

    public func start() {
        poll()
        let timer = DispatchSource.makeTimerSource(queue: .global(qos: .utility))
        timer.schedule(deadline: .now() + interval, repeating: interval)
        timer.setEventHandler { [weak self] in self?.poll() }
        timer.resume()
        self.timer = timer
    }

    public func stop() { timer?.cancel(); timer = nil }

    open func read() -> [AgentSession] { [] }

    public func poll() {
        lock.lock()
        if reading { lock.unlock(); return }
        reading = true
        lock.unlock()
        defer { lock.lock(); reading = false; lock.unlock() }
        let found = read()
        lock.lock()
        let didChange = found != current
        if didChange { current = found }
        lock.unlock()
        if didChange { changed?() }
    }
}

/// Is a pid alive, and still the same process? Pids get reused; start times settle it.
public enum Liveness {
    public static let tolerance: TimeInterval = 5 * 60

    public static func isAlive(_ pid: Int, startedAt: Date?) -> Bool {
        if kill(pid_t(pid), 0) != 0 && errno != EPERM { return false }
        guard let startedAt else { return true }
        var info = proc_bsdinfo()
        let size = Int32(MemoryLayout<proc_bsdinfo>.size)
        guard proc_pidinfo(Int32(pid), PROC_PIDTBSDINFO, 0, &info, size) == size else { return true }
        let actual = Date(timeIntervalSince1970: TimeInterval(info.pbi_start_tvsec) + TimeInterval(info.pbi_start_tvusec) / 1_000_000)
        return abs(actual.timeIntervalSince(startedAt)) < tolerance
    }
}

/// Claude Code writes ~/.claude/sessions/<pid>.json the moment its state changes; polled every few seconds, pids checked for life.
public final class ClaudeSessions: PolledMonitor {
    private let directory: URL
    private let alive: (Int, Date?) -> Bool

    public init(providerId: String, directory: URL, alive: @escaping (Int, Date?) -> Bool = Liveness.isAlive) {
        self.directory = directory
        self.alive = alive
        super.init(providerId: providerId, interval: 3)
    }

    public override func read() -> [AgentSession] { Self.read(directory, alive: alive) }

    public static func read(_ directory: URL, alive: (Int, Date?) -> Bool) -> [AgentSession] {
        guard let names = try? FileManager.default.contentsOfDirectory(atPath: directory.path) else { return [] }
        var found: [AgentSession] = []
        for name in names where name.hasSuffix(".json") {
            guard let data = try? Data(contentsOf: directory.appendingPathComponent(name)), let parsed = parse(data) else { continue }
            if alive(parsed.pid, parsed.startedAt) { found.append(parsed.session) }
        }
        return found.sorted { $0.since > $1.since }
    }

    /// Decoded leniently: an unknown field must never cost a session.
    public static func parse(_ data: Data) -> (pid: Int, startedAt: Date?, session: AgentSession)? {
        guard let root = JSON.object(data), let pid = root.num("pid"), let cwd = root.str("cwd") else { return nil }
        let state: SessionState
        switch (root.str("tempo"), root.str("status")) {
        case ("blocked", _), (_, "waiting"): state = .waiting
        case ("active", _), (_, "busy"): state = .working
        default: state = .idle
        }
        let folder = URL(fileURLWithPath: cwd).lastPathComponent
        let surface: String
        switch root.str("entrypoint") {
        case "claude-desktop", "claude-desktop-3p": surface = "Desktop"
        case "claude-vscode": surface = "VS Code"
        case "local-agent": surface = "Agent"
        default: surface = "Terminal"
        }
        let started = root.epochMillis("startedAt") ?? procStart(root.str("procStart"))
        let since = root.epochMillis("statusUpdatedAt") ?? root.epochMillis("updatedAt") ?? Date()
        return (Int(pid), started, AgentSession(id: "claude.\(Int(pid))", name: root.str("name") ?? folder, location: "\(surface) · \(folder)", state: state, waitingFor: root.str("waitingFor") ?? root.str("needs"), since: since))
    }

    private static let ctime: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(identifier: "UTC")
        f.dateFormat = "EEE MMM d HH:mm:ss yyyy"
        return f
    }()

    /// A UTC ctime string with a space-padded day.
    public static func procStart(_ text: String?) -> Date? {
        guard let text else { return nil }
        let collapsed = text.split(separator: " ").joined(separator: " ")
        return ctime.date(from: collapsed)
    }
}

/// Every monitor's sessions, keyed by provider, with one change event for the whole set.
public final class ActivityHub {
    private let monitors: [ActivityMonitor]
    private let lock = NSLock()
    private var current: [String: Activity] = [:]

    public var changed: (() -> Void)?

    public init(monitors: [ActivityMonitor]) {
        self.monitors = monitors
        for monitor in monitors { monitor.changed = { [weak self, weak monitor] in if let monitor { self?.refresh(monitor) } } }
    }

    public var activities: [String: Activity] { lock.lock(); defer { lock.unlock() }; return current }
    public func activity(_ providerId: String) -> Activity? { lock.lock(); defer { lock.unlock() }; return current[providerId] }
    public var anyWorking: Bool { lock.lock(); defer { lock.unlock() }; return current.values.contains { $0.state == .working } }

    public func start() { monitors.forEach { $0.start() } }
    public func stop() { monitors.forEach { $0.stop() } }

    private func refresh(_ monitor: ActivityMonitor) {
        lock.lock()
        if let activity = Activity.of(monitor.sessions) { current[monitor.providerId] = activity } else { current.removeValue(forKey: monitor.providerId) }
        lock.unlock()
        changed?()
    }
}
