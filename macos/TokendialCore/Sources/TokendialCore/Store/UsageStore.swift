import Foundation

/// Polls every connected provider, keeps the last good reading, and turns each failure into a status
/// the dial can show honestly. Thread-agnostic: `changed` fires on whatever thread made the change.
public final class UsageStore {
    private let lock = NSLock()
    private let providers: [UsageProvider]
    private let archive: ReadingArchive
    private let cadence: Cadence
    private let now: () -> Date
    private let launcher: AppLauncher?
    private var remembered: [String: Remembered]
    private var accounts: [String: ProviderAccount] = [:]
    private var generation: [String: Int] = [:]
    private var disconnectedSet: Set<String>
    private var inFlightSet: Set<String> = []
    private var current: [ProviderReading]
    private var lastAttempt: Date?
    private var polling = false
    private var timer: DispatchSourceTimer?

    public var changed: (() -> Void)?
    /// Whether an agent is working right now; usage cannot move while nothing runs.
    public var isBusy: () -> Bool = { false }

    public init(providers: [UsageProvider], archive: ReadingArchive = ReadingArchive(), disconnected: Set<String> = [], cadence: Cadence = .default, now: @escaping () -> Date = Date.init, launcher: AppLauncher? = nil) {
        self.providers = providers
        self.archive = archive
        self.cadence = cadence
        self.now = now
        self.launcher = launcher
        self.disconnectedSet = disconnected
        var loaded = archive.load()
        if loaded.keys.contains(where: { disconnected.contains($0) }) {
            for id in disconnected { loaded.removeValue(forKey: id) }
            archive.save(loaded)
        }
        remembered = loaded
        current = providers.filter { !disconnected.contains($0.id) }.map { p in loaded[p.id]?.reading ?? UsageStore.placeholder(p) }
    }

    public var readings: [ProviderReading] { lock.lock(); defer { lock.unlock() }; return current }
    public var inFlight: Set<String> { lock.lock(); defer { lock.unlock() }; return inFlightSet }
    public var disconnected: Set<String> {
        get { lock.lock(); defer { lock.unlock() }; return disconnectedSet }
        set { setDisconnected(newValue) }
    }

    /// Accounts come from the last `refreshAccounts`, never from a synchronous credential read: on macOS that read can block on a Keychain prompt.
    public var summaries: [ProviderSummary] {
        let (off, known) = lock.withLock { (disconnectedSet, accounts) }
        return providers.map { ProviderSummary(id: $0.id, name: $0.displayName, account: off.contains($0.id) ? nil : known[$0.id], signIn: $0.signIn, connected: !off.contains($0.id)) }
    }

    /// Asks every connected provider whose account it holds, off the caller's thread.
    public func refreshAccounts() async {
        let live = lock.withLock { providers.filter { !disconnectedSet.contains($0.id) } }
        var found: [String: ProviderAccount] = [:]
        for provider in live { if let account = provider.account() { found[provider.id] = account } }
        lock.withLock { accounts = found }
        changed?()
    }

    public func start() {
        pollNow()
        let timer = DispatchSource.makeTimerSource(queue: .global(qos: .utility))
        timer.schedule(deadline: .now() + cadence.active, repeating: cadence.active)
        timer.setEventHandler { [weak self] in self?.tick() }
        timer.resume()
        self.timer = timer
    }

    public func stop() { timer?.cancel(); timer = nil }
    public func onWake() { pollNow() }

    private func tick() {
        lock.lock()
        let waited = lastAttempt.map { now().timeIntervalSince($0) } ?? .greatestFiniteMagnitude
        lock.unlock()
        if cadence.shouldPoll(busy: isBusy(), sinceLastAttempt: waited) { pollNow() }
    }

    public func pollNow() {
        let proceed: Bool = lock.withLock {
            if polling { return false }
            polling = true
            lastAttempt = now()
            return true
        }
        guard proceed else { return }
        Task.detached(priority: .utility) { [self] in
            await poll()
            lock.withLock { polling = false }
        }
    }

    public func poll() async {
        await refreshAccounts()
        let (live, generations): ([UsageProvider], [String: Int]) = lock.withLock {
            let live = providers.filter { !disconnectedSet.contains($0.id) }
            inFlightSet = Set(live.map { $0.id })
            return (live, generation)
        }
        changed?()
        var next: [ProviderReading] = []
        for provider in live {
            if let reading = await readOne(provider, generation: generations[provider.id] ?? 0) { next.append(reading) }
        }
        lock.withLock {
            inFlightSet = []
            current = next.filter { isCurrent($0.providerId, generations[$0.providerId] ?? 0) }
        }
        changed?()
    }

    /// Refetch one provider without spending the others' rate-limit budget. The user asked for this one, so
    /// the held credential goes with it: a read that was refused earlier is worth trying again now.
    public func poll(_ providerId: String) {
        guard let provider = providers.first(where: { $0.id == providerId }) else { return }
        provider.forgetCredential()
        lock.lock()
        if disconnectedSet.contains(providerId) || inFlightSet.contains(providerId) { lock.unlock(); return }
        inFlightSet.insert(providerId)
        let gen = generation[providerId] ?? 0
        lock.unlock()
        changed?()
        Task.detached(priority: .utility) { [self] in
            if let reading = await readOne(provider, generation: gen) {
                lock.withLock {
                    if let index = current.firstIndex(where: { $0.providerId == providerId }) { current[index] = reading }
                    lastAttempt = now()
                }
                changed?()
            }
            try? await Task.sleep(nanoseconds: 350_000_000)
            lock.withLock { _ = inFlightSet.remove(providerId) }
            changed?()
        }
    }

    public func disconnect(_ providerId: String) { var set = disconnected; set.insert(providerId); setDisconnected(set) }

    public func connect(_ providerId: String) {
        var set = disconnected
        guard set.remove(providerId) != nil else { return }
        setDisconnected(set)
        Task.detached(priority: .utility) { [self] in
            if providers.first(where: { $0.id == providerId })?.account() == nil { _ = openSource(providerId) }
        }
    }

    public func openSource(_ providerId: String) -> Bool {
        guard let provider = providers.first(where: { $0.id == providerId }), case .openApp(let key, _) = provider.signIn, let launcher else { return false }
        return launcher.isInstalled(key) && launcher.open(key)
    }

    public func reauthorize(_ providerId: String) {
        providers.first { $0.id == providerId }?.forgetCredential()
        poll(providerId)
    }

    private func setDisconnected(_ value: Set<String>) {
        lock.lock()
        if disconnectedSet == value { lock.unlock(); return }
        let changedIds = disconnectedSet.symmetricDifference(value)
        for id in changedIds { generation[id, default: 0] += 1 }
        disconnectedSet = value
        current.removeAll { disconnectedSet.contains($0.providerId) }
        for id in disconnectedSet where remembered.removeValue(forKey: id) != nil { archive.forget(id) }
        for id in changedIds where !disconnectedSet.contains(id) {
            if let provider = providers.first(where: { $0.id == id }), !current.contains(where: { $0.providerId == id }) { current.append(Self.placeholder(provider)) }
        }
        current = providers.compactMap { p in current.first { $0.providerId == p.id } }
        lock.unlock()
        changed?()
        pollNow()
    }

    private func isCurrent(_ providerId: String, _ gen: Int) -> Bool {
        !disconnectedSet.contains(providerId) && (generation[providerId] ?? 0) == gen
    }

    private func readOne(_ provider: UsageProvider, generation gen: Int) async -> ProviderReading? {
        let ok = lock.withLock { isCurrent(provider.id, gen) }
        if !ok { return nil }
        do {
            let fresh = try await provider.read()
            let still: Bool = lock.withLock {
                let still = isCurrent(provider.id, gen)
                if still {
                    remembered[provider.id] = Remembered(reading: fresh, takenAt: now())
                    archive.save(remembered)
                }
                return still
            }
            if !still { return nil }
            Log.usage.debug("\(provider.id): \(fresh.windows.count) window(s)")
            return fresh
        } catch {
            let still = lock.withLock { isCurrent(provider.id, gen) }
            if !still { return nil }
            Log.usage.error("\(provider.id): \(error)")
            return degrade(provider, error)
        }
    }

    /// Never invents a number: re-show the last good reading, dimmed when old, or a dial with no reading.
    private func degrade(_ provider: UsageProvider, _ error: Error) -> ProviderReading {
        let status = Self.status(for: error, now: now())
        lock.lock(); defer { lock.unlock() }
        if Self.supersedes(status) {
            if remembered.removeValue(forKey: provider.id) != nil { archive.save(remembered) }
            var placeholder = Self.placeholder(provider); placeholder.status = status
            return placeholder
        }
        guard let previous = remembered[provider.id] else {
            var placeholder = Self.placeholder(provider); placeholder.status = status
            return placeholder
        }
        var reading = previous.reading
        if now().timeIntervalSince(previous.takenAt) > cadence.staleAfter { reading.status = .stale(since: previous.takenAt) }
        return reading
    }

    /// A sign-out or an unmetered plan makes the old number untrue, not merely old.
    public static func supersedes(_ status: ReadingStatus) -> Bool {
        switch status { case .needsSignIn, .unsupported: return true; default: return false }
    }

    public static func status(for error: Error, now: Date) -> ReadingStatus {
        guard let usage = error as? UsageError else { return .failed(why: error.localizedDescription) }
        switch usage.kind {
        case .needsSignIn: return .needsSignIn
        case .credentialExpired, .rateLimited: return .stale(since: now)
        case .nothingMetered: return .unsupported(why: usage.message)
        case .badResponse: return .failed(why: "HTTP \(usage.status)")
        }
    }

    private static func placeholder(_ p: UsageProvider) -> ProviderReading {
        ProviderReading(providerId: p.id, displayName: p.displayName, fidelity: .official, status: .stale(since: .distantPast), windows: [])
    }
}
