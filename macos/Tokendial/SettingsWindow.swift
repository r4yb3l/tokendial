import AppKit
import SwiftUI
import TokendialCore

/// Observable bridge between the JSON settings and SwiftUI.
final class SettingsModel: ObservableObject {
    @Published var settings: Settings
    @Published var summaries: [ProviderSummary] = []
    @Published var readings: [ProviderReading] = []
    let save: (Settings) -> Void
    let connect: (String, Bool) -> Void
    let refresh: (String) -> Void
    let openSource: (String) -> Void
    let testAlert: () -> Void
    let version: String

    init(settings: Settings, version: String, save: @escaping (Settings) -> Void, connect: @escaping (String, Bool) -> Void, refresh: @escaping (String) -> Void, openSource: @escaping (String) -> Void, testAlert: @escaping () -> Void) {
        self.settings = settings
        self.version = version
        self.save = save
        self.connect = connect
        self.refresh = refresh
        self.openSource = openSource
        self.testAlert = testAlert
    }

    func detail(_ summary: ProviderSummary) -> String {
        if !summary.connected { return "Off" }
        let reading = readings.first { $0.providerId == summary.id }
        let status: String
        switch reading?.status {
        case .live?: status = reading!.hasReading ? reading!.headlineText : "Connected"
        case .needsSignIn?: status = summary.signIn.explanation
        case .unsupported(let why)?: status = why
        case .failed(let why)?: status = "Unavailable (\(why))"
        case .stale(let since)? where since > .distantPast: status = "Last read \(Copy.ago(since, now: Date()))"
        default: status = summary.account == nil ? summary.signIn.explanation : "Waiting for the first reading"
        }
        if let account = summary.account?.summary { return "\(account) · \(status)" }
        return status
    }
}

struct SettingsView: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        Form {
            Section {
                ForEach(model.summaries, id: \.id) { summary in
                    HStack(alignment: .center) {
                        Toggle(isOn: Binding(get: { summary.connected }, set: { model.connect(summary.id, $0) })) { EmptyView() }
                            .toggleStyle(.checkbox)
                            .labelsHidden()
                        VStack(alignment: .leading, spacing: 2) {
                            Text(summary.name)
                            Text(model.detail(summary)).font(.caption).foregroundStyle(.secondary).lineLimit(2)
                        }
                        Spacer()
                        if summary.connected, summary.account == nil, case .openApp(_, let name) = summary.signIn {
                            Button("Open \(name)") { model.openSource(summary.id) }
                        }
                        if summary.connected, let url = summary.account?.manageURL {
                            Button("Manage") { NSWorkspace.shared.open(url) }
                        }
                        if summary.connected {
                            Button("Refresh") { model.refresh(summary.id) }
                        }
                    }
                }
            } header: { Text("Providers") } footer: {
                Text("Tokendial reads the sign-in each coding tool already keeps on this Mac and asks that tool's usage endpoint. Nothing is written back and nothing leaves the machine except that request.")
            }

            Section("Panel") {
                Picker("Panel", selection: binding(\.panel)) {
                    Text("Expand when the cursor reaches the top edge").tag(PanelMode.expandOnHover)
                    Text("Always expanded").tag(PanelMode.alwaysExpanded)
                    Text("Hidden (alerts and the menu bar item only)").tag(PanelMode.hidden)
                }.pickerStyle(.radioGroup).labelsHidden()
            }

            Section {
                Toggle("Usage thresholds", isOn: binding(\.alertThresholds))
                TextField("Thresholds (%)", text: Binding(get: { model.settings.thresholds.map(String.init).joined(separator: ", ") }, set: { text in
                    let parsed = Array(Set(text.split(whereSeparator: { ",; ".contains($0) }).compactMap { Int($0) }.filter { (1...100).contains($0) })).sorted()
                    if !parsed.isEmpty { model.settings.thresholds = parsed; model.save(model.settings) }
                }))
                Toggle("Reset soon and available again", isOn: binding(\.alertResetSoon))
                Stepper("Lead time: \(model.settings.resetLeadMinutes) min", value: binding(\.resetLeadMinutes), in: 1...120)
                Toggle("An agent is waiting for you", isOn: binding(\.alertWaiting))
                Stepper("After waiting: \(model.settings.waitingDebounceSeconds) s", value: binding(\.waitingDebounceSeconds), in: 5...600, step: 5)
                Toggle("Limit reached", isOn: binding(\.alertLimit))
                Picker("Delivery", selection: binding(\.delivery)) {
                    Text("Tokendial banners (top-right, accent colour, shown even under Focus)").tag(AlertDelivery.tokendialBanners)
                    Text("Notification Center").tag(AlertDelivery.systemNotifications)
                    Text("Notification Center, banners when notifications are not allowed").tag(AlertDelivery.systemThenBanners)
                }.pickerStyle(.radioGroup)
                Button("Send a test alert") { model.testAlert() }
            } header: { Text("Alerts") } footer: {
                Text("Silent while the panel is expanded under your cursor. At most one alert per provider each minute.")
            }

            Section("Startup") {
                Toggle("Launch at login", isOn: binding(\.launchAtLogin))
            }

            Section("About") {
                Text("Tokendial \(model.version) · MIT License").foregroundStyle(.secondary)
                HStack {
                    Button("Website") { NSWorkspace.shared.open(URL(string: "https://tokendial.app")!) }
                    Button("Source and issues") { NSWorkspace.shared.open(URL(string: "https://github.com/r4yb3l-qa/tokendial")!) }
                    Button("Open data folder") { NSWorkspace.shared.open(Paths.appSupport) }
                }
            }
        }
        .formStyle(.grouped)
        .frame(minWidth: 520, minHeight: 560)
    }

    private func binding<T>(_ path: WritableKeyPath<Settings, T>) -> Binding<T> {
        Binding(get: { model.settings[keyPath: path] }, set: { value in
            model.settings[keyPath: path] = value
            model.save(model.settings)
        })
    }
}

struct WelcomeView: View {
    let detected: [ProviderSummary]
    let absent: [ProviderSummary]
    let finish: ([String], Bool) -> Void
    @State private var chosen: Set<String>

    init(detected: [ProviderSummary], absent: [ProviderSummary], finish: @escaping ([String], Bool) -> Void) {
        self.detected = detected
        self.absent = absent
        self.finish = finish
        _chosen = State(initialValue: Set(detected.map { $0.id }))
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Tokendial").font(.system(size: 24, weight: .semibold))
            Text("A capsule at the top of the screen with one dial per coding assistant, showing how much of each usage limit you have spent, plus a notification when you cross a threshold, when a window is about to reset, when a limit is reached, and when an agent has been waiting on you.")
                .foregroundStyle(.secondary)
            Text("Each provider is opt-in. Tokendial reads the sign-in its own tool already stores on this Mac and calls that tool's usage endpoint; it never writes credentials and never sends them anywhere else. macOS will ask once before Tokendial may read a tool's keychain item; choose Always Allow.")
                .foregroundStyle(.secondary)
            GroupBox(detected.isEmpty ? "Nothing found yet" : "Found on this Mac") {
                VStack(alignment: .leading, spacing: 6) {
                    if detected.isEmpty { Text("Sign in to a supported tool and connect it later from Settings.").foregroundStyle(.secondary) }
                    ForEach(detected, id: \.id) { p in
                        Toggle(isOn: Binding(get: { chosen.contains(p.id) }, set: { on in if on { chosen.insert(p.id) } else { chosen.remove(p.id) } })) {
                            VStack(alignment: .leading) {
                                Text(p.name)
                                Text(p.account?.summary ?? "Signed in").font(.caption).foregroundStyle(.secondary)
                            }
                        }
                    }
                }.frame(maxWidth: .infinity, alignment: .leading).padding(6)
            }
            if !absent.isEmpty {
                GroupBox("Not signed in") {
                    Text(absent.map { $0.name }.joined(separator: " · ")).foregroundStyle(.secondary).frame(maxWidth: .infinity, alignment: .leading).padding(6)
                }
            }
            HStack {
                Button(detected.isEmpty ? "Start" : "Connect and start") { finish(Array(chosen), false) }.keyboardShortcut(.defaultAction)
                Button("Open settings") { finish(Array(chosen), true) }
            }
        }
        .padding(24)
        .frame(width: 520)
    }
}

/// Hosts a SwiftUI view in a regular window that can be shown again after closing.
final class HostedWindow<Content: View> {
    private let window: NSWindow

    init(title: String, content: Content) {
        window = NSWindow(contentViewController: NSHostingController(rootView: content))
        window.title = title
        window.styleMask = [.titled, .closable, .miniaturizable, .resizable]
        window.isReleasedWhenClosed = false
        window.center()
    }

    var onClose: (() -> Void)? {
        didSet {
            NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: window, queue: .main) { [weak self] _ in self?.onClose?() }
        }
    }

    func show() {
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func close() { window.close() }
}
