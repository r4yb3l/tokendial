import Foundation

public enum InstallOutcome: Sendable {
    case success
    case timedOut
    case terminalClosed
}

/// Watches one install through: first the tool appears on disk, then its credential does. It polls, like the
/// session monitors, because none of what it waits for announces itself.
///
/// One difference from Windows, and it is forced: there the terminal is a child process, so its exit is an
/// event. Here the terminal belongs to the user, so the script drops a marker file on its way out and this
/// watches for that instead. A sign-in that lands in the same tick still wins, so closing the window right
/// after finishing does not lose the result.
public final class InstallWatcher {
    public static let defaultTimeout: TimeInterval = 15 * 60
    public static let defaultInterval: TimeInterval = 3

    public let providerId: String
    public private(set) var state: InstallState
    public private(set) var outcome: InstallOutcome?

    public var advanced: ((InstallState) -> Void)?
    public var completed: ((InstallOutcome) -> Void)?

    private let installed: () -> Bool
    private let signedIn: () -> Bool
    private let terminalFinished: () -> Bool
    private let waitsAfterTerminal: Bool
    private let timeout: TimeInterval
    private let now: () -> Date
    private let lock = NSLock()
    private var timer: DispatchSourceTimer?
    private var started: Date

    public init(providerId: String,
                installed: @escaping () -> Bool,
                signedIn: @escaping () -> Bool,
                terminalFinished: @escaping () -> Bool = { false },
                waitsAfterTerminal: Bool = false,
                now: @escaping () -> Date = Date.init,
                timeout: TimeInterval = InstallWatcher.defaultTimeout) {
        self.providerId = providerId
        self.installed = installed
        self.signedIn = signedIn
        self.terminalFinished = terminalFinished
        self.waitsAfterTerminal = waitsAfterTerminal
        self.now = now
        self.timeout = timeout
        self.started = now()
        self.state = installed() ? .installed : .notInstalled
    }

    public var running: Bool { lock.withLock { outcome == nil } }

    public func start(interval: TimeInterval = InstallWatcher.defaultInterval) {
        lock.withLock { started = now() }
        let timer = DispatchSource.makeTimerSource(queue: .global(qos: .utility))
        timer.schedule(deadline: .now() + interval, repeating: interval)
        timer.setEventHandler { [weak self] in self?.tick() }
        lock.withLock { self.timer = timer }
        timer.resume()
    }

    /// One look at the world. Public so a test can drive it without a clock.
    public func tick() {
        var advance: InstallState?
        var finish: InstallOutcome?
        lock.lock()
        if outcome == nil {
            if signedIn() {
                if state != .signedIn { state = .signedIn; advance = state }
                finish = .success
            } else if state == .notInstalled, installed() {
                state = .installed
                advance = state
            }
            if finish == nil, terminalFinished(), !(waitsAfterTerminal && state == .installed) {
                finish = .terminalClosed
            }
            if finish == nil, now().timeIntervalSince(started) >= timeout {
                finish = .timedOut
            }
            if let finish {
                outcome = finish
                timer?.cancel()
                timer = nil
            }
        }
        lock.unlock()
        if let advance { advanced?(advance) }
        if let finish { completed?(finish) }
    }

    public func stop() {
        lock.withLock {
            timer?.cancel()
            timer = nil
        }
    }
}
