import AppKit
import TokendialCore

/// The composition root: settings, store, monitors, alert engine, panel, menu bar item, notifications, wired on the main thread.
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var settings = Settings.load()
    private let archive = ReadingArchive()
    private var store: UsageStore!
    private var hub: ActivityHub!
    private var alerts: AlertCoordinator!
    private var notifications: NotificationSink!
    private var banners: BannerSink!
    private var router: AlertRouter!
    private let panel = PanelController()
    private let statusItem = StatusItemController()
    private var settingsWindow: HostedWindow<SettingsView>?
    private var settingsModel: SettingsModel?
    private var welcome: HostedWindow<WelcomeView>?
    private var clock: Timer?
    private var muted = 0

    static var version: String { Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev" }

    func applicationDidFinishLaunching(_ notification: Notification) {
        FileLog.install()
        Log.ui.info("Tokendial \(Self.version) starting")

        var providers: [UsageProvider] = ClaudeProfile.discover().map { ClaudeProvider(profile: $0, archive: archive) }
        providers += [CodexProvider(archive: archive), CopilotProvider(archive: archive), CursorProvider(), AntigravityProvider(), GlmProvider(archive: archive), GrokProvider(), OpenCodeProvider(archive: archive)]
        store = UsageStore(providers: providers, archive: archive, disconnected: settings.disconnected, launcher: WorkspaceLauncher())
        hub = ActivityHub(monitors: ClaudeProfile.discover().map { ClaudeSessions(providerId: $0.id, directory: $0.sessionsDirectory) })
        store.isBusy = { [weak self] in self?.hub.anyWorking ?? false }

        let reading: (String) -> ProviderReading? = { [weak self] id in self?.store.readings.first { $0.providerId == id } }
        let activity: (String) -> Activity? = { [weak self] id in self?.hub.activity(id) }
        notifications = NotificationSink(reading: reading, activity: activity)
        notifications.onOpened = { [weak self] _ in self?.panel.flash(8) }
        banners = BannerSink(reading: reading, activity: activity)
        banners.onOpened = { [weak self] _ in self?.panel.flash(8) }
        router = AlertRouter(notifications: notifications, banners: banners) { [weak self] in self?.settings.delivery ?? .tokendialBanners }
        let current = settings
        alerts = AlertCoordinator(sink: router, config: current.alertConfig, stateFile: AlertCoordinator.defaultStateFile, wants: { current.wants($0) })

        panel.onHoverChanged = { [weak self] on in self?.alerts.onHover(on) }
        panel.onProviderClicked = { [weak self] id in self?.openProvider(id) }
        panel.onSettingsRequested = { [weak self] in self?.showSettings() }
        statusItem.onShow = { [weak self] in self?.panel.flash(6) }
        statusItem.onRefresh = { [weak self] in self?.store.pollNow() }
        statusItem.onSettings = { [weak self] in self?.showSettings() }
        statusItem.onTestAlert = { [weak self] in self?.testAlert() }
        statusItem.onQuit = { NSApp.terminate(nil) }
        statusItem.onLaunchAtLogin = { [weak self] on in self?.setLaunchAtLogin(on) }

        store.changed = { [weak self] in DispatchQueue.main.async { self?.onStoreChanged() } }
        hub.changed = { [weak self] in DispatchQueue.main.async { self?.onHubChanged() } }
        NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in self?.store.onWake() }
        NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main) { [weak self] _ in self?.panel.reposition() }

        panel.setMode(settings.panel)
        refreshModel()
        clock = Timer.scheduledTimer(withTimeInterval: 30, repeats: true) { [weak self] _ in self?.refreshModel() }

        if settings.firstRunDone { begin() } else { firstRun() }
    }

    /// tokendial://show, tokendial://show?provider=claude, tokendial://settings, tokendial://test-alert.
    func application(_ application: NSApplication, open urls: [URL]) {
        for url in urls {
            switch url.host {
            case "settings": showSettings()
            case "test-alert": testAlert()
            default:
                let provider = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems?.first { $0.name == "provider" }?.value
                panel.flash(8, showing: provider)
            }
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        store.stop()
        hub.stop()
        alerts.stop()
        banners.closeAll()
    }

    private func begin() {
        store.start()
        hub.start()
        alerts.start()
        if settings.launchAtLogin != LaunchAtLogin.isEnabled { LaunchAtLogin.set(settings.launchAtLogin) }
    }

    private func firstRun() {
        Task { [weak self] in
            await self?.store.refreshAccounts()
            await MainActor.run { self?.showWelcome() }
        }
    }

    private func showWelcome() {
        let all = store.summaries
        let detected = all.filter { $0.account != nil }
        let absent = all.filter { $0.account == nil }
        welcome = HostedWindow(title: "Welcome to Tokendial", content: WelcomeView(detected: detected, absent: absent) { [weak self] chosen, openSettings in
            guard let self else { return }
            self.settings.disconnected = Set(all.map { $0.id }).subtracting(chosen)
            self.settings.firstRunDone = true
            self.settings.lastSeenVersion = Self.version
            self.save()
            self.store.disconnected = self.settings.disconnected
            self.begin()
            self.welcome?.close()
            self.welcome = nil
            if openSettings { self.showSettings() }
        })
        welcome?.show()
    }

    private func onStoreChanged() {
        alerts.onReadings(store.readings)
        settingsModel?.readings = store.readings
        settingsModel?.summaries = store.summaries
        refreshModel()
    }

    private func onHubChanged() {
        alerts.onActivities(hub.activities)
        refreshModel()
    }

    private func refreshModel() {
        let model = PanelModel.build(readings: store.readings, summaries: store.summaries, activities: hub.activities, inFlight: store.inFlight, muted: muted)
        panel.update(model)
        let worst = model.tiles.filter { $0.hasReading }.compactMap { $0.fraction }.max()
        let lines = model.tiles.filter { $0.hasReading }.map { "\($0.name) \(Int(($0.fraction ?? 0) * 100).description)%" }
        statusItem.update(worst: worst, tooltip: lines.isEmpty ? "Tokendial · no providers connected" : "Tokendial\n" + lines.joined(separator: "\n"), launchAtLogin: settings.launchAtLogin)
    }

    private func openProvider(_ id: String) {
        let summary = store.summaries.first { $0.id == id }
        if store.readings.first(where: { $0.providerId == id })?.status == .needsSignIn, store.openSource(id) { return }
        if let url = summary?.account?.manageURL { NSWorkspace.shared.open(url) } else { showSettings() }
    }

    private func showSettings() {
        if settingsWindow == nil {
            let model = SettingsModel(settings: settings, version: Self.version, save: { [weak self] s in self?.settings = s; self?.save(); self?.applySettings() },
                                      connect: { [weak self] id, on in self?.connect(id, on) },
                                      refresh: { [weak self] id in self?.store.poll(id) },
                                      openSource: { [weak self] id in _ = self?.store.openSource(id) },
                                      testAlert: { [weak self] in self?.testAlert() })
            model.summaries = store.summaries
            model.readings = store.readings
            settingsModel = model
            settingsWindow = HostedWindow(title: "Tokendial Settings", content: SettingsView(model: model))
            settingsWindow?.onClose = { [weak self] in self?.settingsWindow = nil; self?.settingsModel = nil }
        }
        settingsWindow?.show()
    }

    private func applySettings() {
        panel.setMode(settings.panel)
        let current = settings
        alerts.reconfigure(current.alertConfig, wants: { current.wants($0) })
        if settings.launchAtLogin != LaunchAtLogin.isEnabled { LaunchAtLogin.set(settings.launchAtLogin) }
        refreshModel()
    }

    private func connect(_ id: String, _ on: Bool) {
        if on { settings.disconnected.remove(id); store.connect(id) } else { settings.disconnected.insert(id); store.disconnect(id) }
        save()
        settingsModel?.settings = settings
        settingsModel?.summaries = store.summaries
    }

    private func setLaunchAtLogin(_ on: Bool) {
        settings.launchAtLogin = on
        LaunchAtLogin.set(on)
        save()
        refreshModel()
    }

    /// A sample threshold alert straight to the sink, so the user sees what one looks like without waiting to cross 50 %.
    private func testAlert() {
        let reading = store.readings.first { $0.hasReading } ?? store.readings.first
        let window = reading?.headline
        router.deliver(Alert(kind: .threshold, provider: reading?.providerId ?? "claude", window: window?.id, pct: 80, resetsAt: window?.resetsAt ?? Date().addingTimeInterval(51 * 60)))
    }

    private func save() { settings.save() }
}

/// Opens the desktop app that owns a credential, by bundle id.
struct WorkspaceLauncher: AppLauncher {
    private static let bundles = ["cursor": "com.todesktop.230313mzl4w4u92", "antigravity": "com.google.antigravity", "codex": "com.openai.codex"]

    func isInstalled(_ appKey: String) -> Bool { url(appKey) != nil }

    func open(_ appKey: String) -> Bool {
        guard let url = url(appKey) else { return false }
        NSWorkspace.shared.openApplication(at: url, configuration: NSWorkspace.OpenConfiguration(), completionHandler: nil)
        return true
    }

    private func url(_ appKey: String) -> URL? {
        guard let bundle = Self.bundles[appKey] else { return nil }
        return NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundle)
    }
}

/// Rolling text log under Application Support/Tokendial/logs. Debug lines only with TOKENDIAL_DEBUG set.
enum FileLog {
    private static var handle: FileHandle?
    private static let queue = DispatchQueue(label: "tokendial.log")

    static func install() {
        let directory = Paths.appSupport.appendingPathComponent("logs")
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let file = directory.appendingPathComponent("tokendial.log")
        if let size = try? FileManager.default.attributesOfItem(atPath: file.path)[.size] as? Int, size > 1_000_000 {
            try? FileManager.default.removeItem(at: file.appendingPathExtension("1"))
            try? FileManager.default.moveItem(at: file, to: file.appendingPathExtension("1"))
        }
        if !FileManager.default.fileExists(atPath: file.path) { FileManager.default.createFile(atPath: file.path, contents: nil) }
        handle = try? FileHandle(forWritingTo: file)
        handle?.seekToEndOfFile()
        let debug = ProcessInfo.processInfo.environment["TOKENDIAL_DEBUG"] != nil
        let formatter = DateFormatter()
        formatter.dateFormat = "yyyy-MM-dd HH:mm:ss.SSS"
        Log.sink = { level, area, message in
            if level == .debug && !debug { return }
            queue.async { handle?.write(Data("\(formatter.string(from: Date())) [\(level.rawValue)] \(area): \(message)\n".utf8)) }
        }
    }
}
