import AppKit
import SwiftUI
import TokendialCore

/// Observable bridge between the JSON settings and SwiftUI. Every change saves through `save`, which also
/// applies it: the host reads the settings back and retunes the panel, the alerts and the language.
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

    func reading(_ id: String) -> ProviderReading? { readings.first { $0.providerId == id } }

    /// "Team · 63% used · 37% left": the account, then where the reading stands.
    func detail(_ summary: ProviderSummary) -> String {
        guard summary.connected else { return Strings.t("status.off") }
        let status = statusLine(summary)
        guard let account = summary.account?.summary, !account.isEmpty else { return status }
        return "\(account) · \(status)"
    }

    private func statusLine(_ summary: ProviderSummary) -> String {
        guard let reading = reading(summary.id) else { return waiting(summary) }
        switch reading.status {
        case .live: return reading.hasReading ? reading.headlineText : Strings.t("status.connected")
        case .needsSignIn: return summary.signIn.explanation
        case .unsupported(let why): return why
        case .failed(let why): return Strings.t("card.unavailable", ["why": why])
        case .stale(let since) where since > .distantPast: return Strings.t("card.lastRead", ["ago": Copy.ago(since, now: Date())])
        default: return waiting(summary)
        }
    }

    private func waiting(_ summary: ProviderSummary) -> String {
        summary.account == nil ? summary.signIn.explanation : Strings.t("status.waitingFirst")
    }

    func set<T>(_ path: WritableKeyPath<Settings, T>, _ value: T) {
        settings[keyPath: path] = value
        save(settings)
    }

    func binding<T>(_ path: WritableKeyPath<Settings, T>) -> Binding<T> {
        Binding(get: { self.settings[keyPath: path] }, set: { self.set(path, $0) })
    }
}

/// Providers, panel, alerts, language and startup in two independently scrolling columns, the same shape as
/// the Windows window. Every change saves immediately and takes effect through the host.
struct SettingsView: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        VStack(spacing: 0) {
            ChromeHeader(title: Strings.t("settings.title"), badge: "v" + model.version) { statusPill }
            HStack(spacing: 0) {
                ScrollView {
                    VStack(alignment: .leading, spacing: 0) {
                        ProvidersSection(model: model)
                        ChromeRule().padding(.top, 20).padding(.bottom, 16)
                        GeneralSection(model: model)
                        AboutSection(model: model)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(20)
                }
                .overlay(alignment: .trailing) { Rectangle().fill(Chrome.lineSoft).frame(width: 1) }
                ScrollView {
                    VStack(alignment: .leading, spacing: 0) {
                        PanelSection(model: model)
                        AlertsSection(model: model)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(20)
                }
                .frame(width: 460)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Chrome.windowBackground)
        .environment(\.layoutDirection, Strings.rightToLeft ? .rightToLeft : .leftToRight)
    }

    private var statusPill: some View {
        HStack(spacing: 8) {
            Circle().fill(Chrome.accent).frame(width: 6, height: 6)
            Text(Strings.plural("settings.connectedCount", model.summaries.filter { $0.connected }.count))
                .font(Chrome.font(11)).foregroundStyle(Chrome.slate300)
        }
        .padding(.horizontal, 10).padding(.vertical, 3)
        .background(Capsule().fill(Chrome.wellStrong))
        .overlay(Capsule().stroke(Chrome.lineFaint, lineWidth: 1))
    }
}

private struct ProvidersSection: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ChromeSectionTitle(title: Strings.t("settings.providers"), subtitle: Strings.t("settings.providersSub")) {
                ChromePill(Strings.t("settings.readOnly"), foreground: Chrome.accent, background: Chrome.brandFaint, border: Chrome.brandSoft, mono: true, radius: 4)
            }
            HStack(alignment: .top, spacing: 10) {
                Image(systemName: "checkmark.shield").font(.system(size: 14, weight: .medium)).foregroundStyle(Chrome.accent)
                Text(Strings.t("settings.providersHint"))
                    .font(Chrome.font(12)).foregroundStyle(Chrome.slate300)
                    .lineSpacing(3)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .chromeCard(Chrome.sheet, Chrome.line, padding: 12)
            .padding(.top, 10)

            VStack(spacing: 10) {
                ForEach(model.summaries, id: \.id) { summary in
                    ProviderCard(model: model, summary: summary)
                }
            }
            .padding(.top, 12)
        }
    }
}

private struct ProviderCard: View {
    @ObservedObject var model: SettingsModel
    let summary: ProviderSummary
    @State private var hover = false

    private var reading: ProviderReading? { summary.connected ? model.reading(summary.id) : nil }
    private var fraction: Double? { reading?.hasReading == true ? reading?.headlineFraction : nil }
    private var tint: Color { Color(nsColor: Marks.tint(summary.id)) }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 14) {
                Button { model.connect(summary.id, !summary.connected) } label: {
                    ChromeIndicator(round: false, on: summary.connected)
                }
                .buttonStyle(.plain)
                tile
                VStack(alignment: .leading, spacing: 2) {
                    HStack(spacing: 8) {
                        Text(summary.name)
                            .font(Chrome.font(12, summary.connected ? .semibold : .medium))
                            .foregroundStyle(summary.connected ? Chrome.strong : Chrome.slate300)
                        if let fraction { usedBadge(fraction) }
                    }
                    Text(model.detail(summary))
                        .font(Chrome.font(11))
                        .foregroundStyle(summary.connected ? Chrome.slate400 : Chrome.slate500)
                        .lineLimit(2)
                        .fixedSize(horizontal: false, vertical: true)
                }
                Spacer(minLength: 8)
                actions
            }
            if let fraction { ProviderBar(fraction: fraction).padding(.top, 12) }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(summary.connected ? 14 : 12)
        .background(background)
        .overlay(RoundedRectangle(cornerRadius: radius).stroke(border, lineWidth: 1))
        .opacity(summary.connected || hover ? 1 : 0.75)
        .onHover { hover = $0 }
    }

    private var radius: CGFloat { summary.connected ? 14 : 12 }

    @ViewBuilder private var background: some View {
        if summary.connected {
            RoundedRectangle(cornerRadius: radius).fill(Chrome.activeCard)
        } else {
            RoundedRectangle(cornerRadius: radius).fill(Chrome.sheetSoft)
        }
    }

    private var border: Color {
        guard summary.connected else { return Chrome.edge }
        return hover ? Chrome.brandLineStrong : Chrome.brandLine
    }

    private var tile: some View {
        RoundedRectangle(cornerRadius: 8)
            .fill(summary.connected ? tint.opacity(0.2) : Chrome.surface750)
            .overlay(RoundedRectangle(cornerRadius: 8).stroke(summary.connected ? tint.opacity(0.4) : Chrome.surface700, lineWidth: 1))
            .frame(width: 32, height: 32)
            .overlay { mark }
    }

    @ViewBuilder private var mark: some View {
        if let image = Marks.image(summary.id) {
            Image(nsImage: image)
                .renderingMode(.template)
                .resizable()
                .scaledToFit()
                .frame(width: 16, height: 16)
                .foregroundStyle(summary.connected ? tint : Chrome.slate400)
        } else {
            Text(String(summary.name.prefix(2)).uppercased())
                .font(Chrome.mono(9))
                .foregroundStyle(Chrome.slate400)
        }
    }

    private func usedBadge(_ fraction: Double) -> some View {
        let pct = Int((fraction * 100).rounded(.toNearestOrAwayFromZero))
        return ChromePill(Strings.t("settings.usedBadge", ["pct": pct]),
                          foreground: pct > 0 ? Chrome.accent : Chrome.slate300,
                          background: pct > 0 ? Chrome.brandSoft : Chrome.surface700,
                          border: .clear, mono: true, size: 10, radius: 4)
    }

    @ViewBuilder private var actions: some View {
        if summary.connected {
            HStack(spacing: 8) {
                if summary.account == nil, case .openApp(_, let name) = summary.signIn {
                    ChromeButton(label: Strings.t("settings.open", ["name": name])) { model.openSource(summary.id) }
                }
                if let url = summary.account?.manageURL {
                    ChromeButton(label: Strings.t("settings.manage")) { NSWorkspace.shared.open(url) }
                }
                ChromeButton(label: Strings.t("settings.refresh")) { model.refresh(summary.id) }
            }
        } else {
            Text(Strings.t("settings.inactive")).font(Chrome.mono(11)).foregroundStyle(Chrome.slate500)
        }
    }
}

/// A thin track under a connected provider, filled to its headline usage; the fill warms from green towards
/// the band colour as it grows.
private struct ProviderBar: View {
    let fraction: Double

    var body: some View {
        GeometryReader { geo in
            RoundedRectangle(cornerRadius: 3)
                .fill(fill)
                .frame(width: max(4, geo.size.width * min(max(fraction, 0.02), 1)))
        }
        .frame(height: 6)
        .background(RoundedRectangle(cornerRadius: 3).fill(Chrome.surface800))
    }

    private var fill: LinearGradient {
        let end = fraction < 0.5 ? Chrome.brand500 : Color(nsColor: Theme.color(fraction: fraction))
        return LinearGradient(colors: [Theme.UI.ample, end], startPoint: .leading, endPoint: .trailing)
    }
}

private struct GeneralSection: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ChromeSmallTitle(Strings.t("settings.startup"))
            VStack(alignment: .leading, spacing: 0) {
                ChromeCheck(label: Strings.t("settings.launchAtLogin"), hint: Strings.t("settings.launchAtLoginHint"), isOn: model.binding(\.launchAtLogin))
                ChromeRule().padding(.vertical, 12)
                Text(Strings.t("settings.language")).font(Chrome.font(12, .medium)).foregroundStyle(Chrome.slate300)
                ChromeWrap(spacing: 8) {
                    ForEach(languages) { choice in
                        ChromeChip(label: choice.label, selected: model.settings.language == choice.value) { model.set(\.language, choice.value) }
                    }
                }
                .padding(.top, 8)
                ChromeRule().padding(.top, 16).padding(.bottom, 12)
                Text(Strings.t("settings.theme")).font(Chrome.font(12, .medium)).foregroundStyle(Chrome.slate300)
                ChromeWrap(spacing: 8) {
                    ForEach(themes) { choice in
                        ChromeChip(label: choice.label, selected: model.settings.appearance == choice.value) { model.set(\.appearance, choice.value) }
                    }
                }
                .padding(.top, 8)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .chromeCard(Chrome.sheetSoft, Chrome.edge, padding: 16)
            .padding(.top, 10)
        }
    }

    private var languages: [ChromeChoice<String?>] {
        [ChromeChoice(value: nil, label: Strings.t("settings.language.system"))]
            + Strings.languages.map { ChromeChoice(value: Optional($0.code), label: $0.name) }
    }

    private var themes: [ChromeChoice<Appearance>] {
        [ChromeChoice(value: .system, label: Strings.t("settings.theme.system")),
         ChromeChoice(value: .dark, label: Strings.t("settings.theme.dark")),
         ChromeChoice(value: .light, label: Strings.t("settings.theme.light"))]
    }
}

private struct AboutSection: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ChromeSmallTitle(Strings.t("settings.about")).padding(.top, 20)
            HStack(alignment: .center, spacing: 12) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("\(Strings.t("app.name")) \(model.version)").font(Chrome.font(12, .medium)).foregroundStyle(Chrome.slate200)
                    ChromeBody(Strings.t("settings.license"), size: 11)
                }
                Spacer(minLength: 8)
                HStack(spacing: 8) {
                    ChromeButton(label: Strings.t("settings.website")) { open("https://tokendial.app") }
                    ChromeButton(label: Strings.t("settings.source")) { open("https://github.com/r4yb3l/tokendial") }
                    ChromeButton(label: Strings.t("settings.dataFolder")) { NSWorkspace.shared.open(Paths.appSupport) }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .chromeCard(Chrome.sheetFaint, Chrome.edgeSoft, padding: 16)
            .padding(.top, 10)
        }
    }

    private func open(_ target: String) {
        guard let url = URL(string: target) else { return }
        NSWorkspace.shared.open(url)
    }
}

private struct PanelSection: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ChromeSectionTitle(title: Strings.t("settings.panel")) {
                Text(Strings.t("settings.panelSub")).font(Chrome.font(12)).foregroundStyle(Chrome.slate400)
            }
            VStack(spacing: 10) {
                ForEach(modes) { choice in
                    ChromeRadioCard(label: choice.label, hint: choice.hint, selected: model.settings.panel == choice.value) { model.set(\.panel, choice.value) }
                }
            }
            .padding(.top, 12)
            Text(Strings.t("settings.position")).font(Chrome.font(12, .medium)).foregroundStyle(Chrome.slate300)
                .padding(.top, 12)
            ChromeWrap(spacing: 8) {
                ForEach(edges) { choice in
                    ChromeChip(label: choice.label, selected: model.settings.edge == choice.value) { model.set(\.edge, choice.value) }
                }
            }
            .padding(.top, 8)
        }
    }

    private var modes: [ChromeChoice<PanelMode>] {
        [ChromeChoice(value: .expandOnHover, label: Strings.t("settings.panel.hover"), hint: Strings.t("settings.panel.hoverHint")),
         ChromeChoice(value: .alwaysExpanded, label: Strings.t("settings.panel.always"), hint: Strings.t("settings.panel.alwaysHint")),
         ChromeChoice(value: .hidden, label: Strings.t("settings.panel.hidden"), hint: Strings.t("settings.panel.hiddenHint"))]
    }

    private var edges: [ChromeChoice<DockEdge>] {
        [ChromeChoice(value: .top, label: Strings.t("settings.position.top")),
         ChromeChoice(value: .bottom, label: Strings.t("settings.position.bottom")),
         ChromeChoice(value: .left, label: Strings.t("settings.position.left")),
         ChromeChoice(value: .right, label: Strings.t("settings.position.right"))]
    }
}

private struct AlertsSection: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ChromeRule().padding(.top, 6).padding(.bottom, 16)
            ChromeSectionTitle(Strings.t("settings.alerts"))
            ChromeBody(Strings.t("settings.alertsHint"), size: 11).padding(.top, 4)

            AlertCard(label: Strings.t("settings.thresholds"), hint: Strings.t("settings.thresholdsHint"), isOn: model.binding(\.alertThresholds)) {
                ChromeRule().padding(.top, 10)
                ChromeRow(label: Strings.t("settings.thresholdsField"), hint: Strings.t("settings.thresholdsFieldHint")) {
                    ChromeField(model.settings.thresholds.map(String.init).joined(separator: ", "), width: 128) { text in
                        let parsed = text.split(whereSeparator: { ",; ".contains($0) }).compactMap { Int($0) }.filter { $0 > 0 && $0 <= 100 }
                        let unique = Array(Set(parsed)).sorted()
                        if !unique.isEmpty { model.set(\.thresholds, unique) }
                    }
                }
            }
            AlertCard(label: Strings.t("settings.resetSoon"), hint: Strings.t("settings.resetSoonHint"), isOn: model.binding(\.alertResetSoon)) {
                ChromeRule().padding(.top, 10)
                ChromeRow(label: Strings.t("settings.leadTime")) {
                    ChromeField(String(model.settings.resetLeadMinutes), width: 80) { text in
                        if let n = Int(text), (1...120).contains(n) { model.set(\.resetLeadMinutes, n) }
                    }
                }
            }
            AlertCard(label: Strings.t("settings.waiting"), hint: Strings.t("settings.waitingHint"), isOn: model.binding(\.alertWaiting)) {
                ChromeRule().padding(.top, 10)
                ChromeRow(label: Strings.t("settings.afterWaiting")) {
                    ChromeField(String(model.settings.waitingDebounceSeconds), width: 80) { text in
                        if let n = Int(text), (5...600).contains(n) { model.set(\.waitingDebounceSeconds, n) }
                    }
                }
            }
            AlertCard(label: Strings.t("settings.limit"), hint: Strings.t("settings.limitHint"), isOn: model.binding(\.alertLimit)) { EmptyView() }

            Text(Strings.t("settings.delivery")).font(Chrome.font(12, .medium)).foregroundStyle(Chrome.slate300)
                .padding(.top, 6).padding(.bottom, 6)
            ForEach(deliveries) { choice in
                ChromeRadio(label: choice.label, hint: choice.hint, selected: model.settings.delivery == choice.value) { model.set(\.delivery, choice.value) }
            }
            ChromeWideButton(icon: "bell", label: Strings.t("settings.testAlert")) { model.testAlert() }
                .padding(.top, 10)
        }
    }

    private var deliveries: [ChromeChoice<AlertDelivery>] {
        [ChromeChoice(value: .tokendialBanners, label: Strings.t("settings.delivery.banners"), hint: Strings.t("settings.delivery.bannersHint")),
         ChromeChoice(value: .systemNotifications, label: Strings.t("settings.delivery.system"), hint: Strings.t("settings.delivery.systemHint")),
         ChromeChoice(value: .systemThenBanners, label: Strings.t("settings.delivery.both"))]
    }
}

/// One alert kind: its switch, and under a rule the number it takes.
private struct AlertCard<Input: View>: View {
    let label: String
    let hint: String
    @Binding var isOn: Bool
    @ViewBuilder var input: () -> Input

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ChromeCheck(label: label, hint: hint, isOn: $isOn)
            input()
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .chromeCard(Chrome.sheetStrong, Chrome.edge, padding: 14)
        .padding(.top, 10)
    }
}

/// First run: what will be read, which tools were found, and the choice before anything is polled.
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
        VStack(spacing: 0) {
            ChromeHeader(title: Strings.t("welcome.title"), badge: nil) { EmptyView() }
            ScrollView {
                VStack(alignment: .leading, spacing: 10) {
                    Text(Strings.t("app.name")).font(Chrome.font(22, .semibold)).foregroundStyle(Chrome.strong)
                    ChromeBody(Strings.t("welcome.intro"))
                    ChromeBody(Strings.t("welcome.optIn"))
                    found
                    if !absent.isEmpty { notSignedIn }
                    buttons.padding(.top, 6)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(EdgeInsets(top: 22, leading: 24, bottom: 22, trailing: 24))
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Chrome.windowBackground)
        .environment(\.layoutDirection, Strings.rightToLeft ? .rightToLeft : .leftToRight)
    }

    private var found: some View {
        ChromeSection(title: Strings.t(detected.isEmpty ? "welcome.nothing" : "welcome.found"),
                      description: Strings.t(detected.isEmpty ? "welcome.nothingHint" : "welcome.foundHint")) {
            VStack(alignment: .leading, spacing: 2) {
                ForEach(detected, id: \.id) { provider in
                    HStack(spacing: 8) {
                        ChromeCheck(label: provider.name,
                                    hint: provider.account?.summary ?? Strings.t("status.signedIn"),
                                    isOn: Binding(get: { chosen.contains(provider.id) },
                                                  set: { on in if on { chosen.insert(provider.id) } else { chosen.remove(provider.id) } }))
                        mark(provider.id, colour: Chrome.slate200)
                    }
                }
            }
        }
    }

    private var notSignedIn: some View {
        ChromeSection(title: Strings.t("welcome.notSignedIn"), description: Strings.t("welcome.notSignedInHint")) {
            VStack(alignment: .leading, spacing: 10) {
                ForEach(absent, id: \.id) { provider in
                    HStack(alignment: .top, spacing: 10) {
                        mark(provider.id, colour: Chrome.slate500).padding(.top, 1)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(provider.name).font(Chrome.font(12, .medium)).foregroundStyle(Chrome.slate200)
                            ChromeBody(provider.signIn.explanation, size: 11)
                        }
                    }
                }
            }
        }
    }

    private var buttons: some View {
        HStack(spacing: 8) {
            ChromeButton(label: Strings.t(detected.isEmpty ? "welcome.start" : "welcome.connectStart"), primary: true) {
                finish(Array(chosen), false)
            }
            ChromeButton(label: Strings.t("welcome.openSettings")) { finish(Array(chosen), true) }
        }
    }

    @ViewBuilder private func mark(_ id: String, colour: Color) -> some View {
        if let image = Marks.image(id) {
            Image(nsImage: image)
                .renderingMode(.template)
                .resizable()
                .scaledToFit()
                .frame(width: 18, height: 18)
                .foregroundStyle(colour)
        }
    }
}

/// Hosts a SwiftUI view in a window whose title bar is ours: transparent, with the traffic lights left in
/// place over the header the design draws.
final class HostedWindow<Content: View> {
    private let window: NSWindow

    init(title: String, width: CGFloat, height: CGFloat, content: Content) {
        window = NSWindow(contentViewController: NSHostingController(rootView: content))
        window.title = title
        window.styleMask = [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView]
        window.titlebarAppearsTransparent = true
        window.titleVisibility = .hidden
        window.isMovableByWindowBackground = true
        window.backgroundColor = NSColor(Chrome.windowBackground)
        window.isReleasedWhenClosed = false
        window.contentMinSize = NSSize(width: 480, height: 360)
        window.setContentSize(NSSize(width: width, height: height))
        applyAppearance()
    }

    /// The window wears the theme's appearance, not the system's. Everything the system draws for us - the
    /// selection behind a field's text, the caret, the focus ring, the scrollers - takes its colour from
    /// there, so a light window under a dark system would otherwise fill a field with a black selection.
    private func applyAppearance() {
        window.appearance = NSAppearance(named: Theme.dark ? .darkAqua : .aqua)
    }

    private var centred = false

    var onClose: (() -> Void)? {
        didSet {
            NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: window, queue: .main) { [weak self] _ in self?.onClose?() }
        }
    }

    /// The window frame keeps the look too, so a theme switch does not leave a pale edge behind the content.
    func retheme() {
        window.backgroundColor = NSColor(Chrome.windowBackground)
        applyAppearance()
    }

    func show() {
        window.makeKeyAndOrderFront(nil)
        window.makeFirstResponder(nil)
        if !centred, let screen = window.screen ?? NSScreen.main {
            let size = window.frame.size
            let visible = screen.visibleFrame
            window.setFrameOrigin(NSPoint(x: visible.midX - size.width / 2, y: visible.midY - size.height / 2))
            centred = true
        }
        NSApp.activate(ignoringOtherApps: true)
    }

    func close() { window.close() }
}
