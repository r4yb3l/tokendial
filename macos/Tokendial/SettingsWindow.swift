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
    let refreshAll: () -> Void
    let openSource: (String) -> Void
    let testAlert: () -> Void
    let version: String

    init(settings: Settings, version: String, save: @escaping (Settings) -> Void, connect: @escaping (String, Bool) -> Void, refresh: @escaping (String) -> Void, refreshAll: @escaping () -> Void, openSource: @escaping (String) -> Void, testAlert: @escaping () -> Void) {
        self.settings = settings
        self.version = version
        self.save = save
        self.connect = connect
        self.refresh = refresh
        self.refreshAll = refreshAll
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

    /// Whether this provider is unreadable only because the keychain turned Tokendial away, which is the one
    /// failure worth explaining in the row: the user can fix it, and deserves to know what the access is for.
    func keychainRefused(_ summary: ProviderSummary) -> Bool {
        guard case .failed(let why)? = reading(summary.id)?.status else { return false }
        return why == Strings.t("error.keychainRefused")
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
    @ObservedObject var installer: InstallAssistant

    var body: some View {
        VStack(spacing: 0) {
            ChromeHeader(title: Strings.t("settings.title"), badge: "v" + model.version) { statusPill }
            HStack(spacing: 0) {
                ScrollView {
                    VStack(alignment: .leading, spacing: 0) {
                        ProvidersSection(model: model, installer: installer)
                        ChromeRule().padding(.top, 20).padding(.bottom, 16)
                        GeneralSection(model: model)
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
            SettingsFooter(model: model)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Chrome.windowBackground)
        .environment(\.layoutDirection, Strings.rightToLeft ? .rightToLeft : .leftToRight)
        .sheet(item: $installer.sheet) { request in
            RunSheet(request: request, missingManager: installer.missingManager(request.recipe),
                     run: { installer.run(request) }, dismiss: { installer.sheet = nil })
        }
    }

    private var statusPill: some View {
        HStack(spacing: 8) {
            Circle().fill(Chrome.accent).frame(width: 6, height: 6)
            Text(Strings.plural("settings.connectedCount", model.summaries.filter { $0.connected && !installer.assisted($0) }.count))
                .font(Chrome.font(11)).foregroundStyle(Chrome.slate300)
        }
        .padding(.horizontal, 10).padding(.vertical, 3)
        .background(Capsule().fill(Chrome.wellStrong))
        .overlay(Capsule().stroke(Chrome.lineFaint, lineWidth: 1))
    }
}

private struct ProvidersSection: View {
    @ObservedObject var model: SettingsModel
    @ObservedObject var installer: InstallAssistant

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

            Text(Strings.t("settings.readToggle"))
                .font(Chrome.font(10, .medium)).foregroundStyle(Chrome.slate500)
                .frame(maxWidth: .infinity, alignment: .trailing)
                .padding(.top, 12).padding(.trailing, 15)

            VStack(spacing: 0) {
                ForEach(Array(model.summaries.enumerated()), id: \.element.id) { pair in
                    if pair.offset > 0 { Rectangle().fill(Chrome.lineFaint).frame(height: 1) }
                    ProviderRow(model: model, installer: installer, summary: pair.element)
                }
            }
            .background(RoundedRectangle(cornerRadius: 12).fill(Chrome.sheet))
            .overlay(RoundedRectangle(cornerRadius: 12).stroke(Chrome.line, lineWidth: 1))
            .clipShape(RoundedRectangle(cornerRadius: 12))
            .padding(.top, 6)

            HStack {
                Spacer(minLength: 0)
                ChromeButton(label: Strings.t("settings.refresh")) { model.refreshAll() }
            }
            .padding(.top, 10)
        }
    }
}

private struct ProviderRow: View {
    @ObservedObject var model: SettingsModel
    @ObservedObject var installer: InstallAssistant
    let summary: ProviderSummary
    @State private var hover = false

    /// While a tool is still being installed or signed in, the assistant owns the row's words and buttons.
    private var assisted: Bool { installer.assisted(summary) }
    private var recipe: InstallRecipe? { installer.recipe(summary.id) }

    private var reading: ProviderReading? { summary.connected ? model.reading(summary.id) : nil }
    private var fraction: Double? { reading?.hasReading == true ? reading?.headlineFraction : nil }
    private var tint: Color { Color(nsColor: Marks.tint(summary.id)) }

    var body: some View {
        HStack(spacing: 12) {
            ChromeDial(fraction: fraction) { mark }
            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: 8) {
                    Text(summary.name)
                        .font(Chrome.font(12, summary.connected ? .semibold : .medium))
                        .foregroundStyle(summary.connected ? Chrome.strong : Chrome.slate300)
                    if let fraction { usedBadge(fraction) }
                }
                Text(assisted && recipe != nil ? InstallDetail.text(summary, recipe!, installer) : model.detail(summary))
                    .font(Chrome.font(11))
                    .foregroundStyle(summary.connected ? Chrome.slate400 : Chrome.slate500)
                    .lineLimit(2)
                    .fixedSize(horizontal: false, vertical: true)
                if assisted {
                    InstallStrip(state: installer.state(summary)).padding(.top, 4)
                }
                if model.keychainRefused(summary) {
                    Text(Strings.t("card.keychainWhy"))
                        .font(Chrome.font(11))
                        .foregroundStyle(Chrome.slate500)
                        .fixedSize(horizontal: false, vertical: true)
                        .padding(.top, 2)
                }
            }
            Spacer(minLength: 8)
            actions
            ChromeSwitch(on: summary.connected) { on in model.connect(summary.id, on) }
                .help(Strings.t("settings.readToggleHint"))
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 12).padding(.vertical, 10)
        .background(summary.connected ? Chrome.brandFaint : Color.clear)
        .opacity(summary.connected || assisted || hover ? 1 : 0.75)
        .onHover { hover = $0 }
    }

    @ViewBuilder private var mark: some View {
        if let image = Marks.image(summary.id) {
            Image(nsImage: image)
                .renderingMode(.template)
                .resizable()
                .scaledToFit()
                .frame(width: 15, height: 15)
                .foregroundStyle(summary.connected ? tint : Chrome.slate500)
        } else {
            Text(String(summary.name.prefix(2)).uppercased())
                .font(Chrome.mono(9))
                .foregroundStyle(Chrome.slate500)
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
        if assisted {
            installAction
        } else if summary.connected {
            HStack(spacing: 8) {
                if summary.account == nil, case .openApp(_, let name) = summary.signIn {
                    ChromeButton(label: Strings.t("settings.open", ["name": name])) { model.openSource(summary.id) }
                }
                if let url = summary.account?.manageURL {
                    ChromeButton(label: Strings.t("settings.manage")) { NSWorkspace.shared.open(url) }
                }
            }
        } else {
            Text(Strings.t("settings.inactive")).font(Chrome.mono(11)).foregroundStyle(Chrome.slate500)
        }
    }

    /// One button, whichever step comes next; nothing once the tool is signed in and only the store is
    /// catching up.
    @ViewBuilder private var installAction: some View {
        switch installer.state(summary) {
        case .notInstalled:
            ChromeButton(label: Strings.t("install.button.install"), primary: true) { installer.ask(summary, .install) }
        case .installed where recipe?.kind == .cli:
            ChromeButton(label: Strings.t("install.button.signIn"), primary: true) { installer.ask(summary, .signIn) }
        case .installed:
            ChromeButton(label: Strings.t("settings.open", ["name": summary.name]), primary: true) { model.openSource(summary.id) }
        default:
            Text(Strings.t("settings.inactive")).font(Chrome.mono(11)).foregroundStyle(Chrome.slate500)
        }
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
                        ChromeChip(label: choice.label, flag: choice.value, globe: choice.value == nil,
                                   selected: model.settings.language == choice.value) { model.set(\.language, choice.value) }
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

/// The bar across the foot of the window: what this is, and where to go from here.
private struct SettingsFooter: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        HStack(spacing: 8) {
            VStack(alignment: .leading, spacing: 2) {
                Text("\(Strings.t("app.name")) \(model.version)")
                    .font(Chrome.font(12, .medium)).foregroundStyle(Chrome.slate200)
                Text(Strings.t("settings.license"))
                    .font(Chrome.font(11)).foregroundStyle(Chrome.slate400)
            }
            Spacer(minLength: 12)
            ChromeButton(label: Strings.t("settings.website")) { open("https://tokendial.app") }
            ChromeButton(label: Strings.t("settings.source")) { open("https://github.com/r4yb3l/tokendial") }
            ChromeButton(label: Strings.t("settings.dataFolder")) { NSWorkspace.shared.open(Paths.appSupport) }
        }
        .padding(.horizontal, 20).padding(.vertical, 12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Chrome.titleBarFill)
        .overlay(alignment: .top) { Rectangle().fill(Chrome.lineSoft).frame(height: 1) }
    }

    private func open(_ address: String) {
        guard let url = URL(string: address) else { return }
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
            HStack(alignment: .top, spacing: 8) {
                tile(.expandOnHover, "settings.panel.hover") { ChromeModeDiagram(kind: .compact) }
                tile(.alwaysExpanded, "settings.panel.always") { ChromeModeDiagram(kind: .wide) }
                tile(.hidden, "settings.panel.hidden") { ChromeModeDiagram(kind: .tray) }
            }
            .padding(.top, 12)
            Text(Strings.t(hintKey))
                .font(Chrome.font(11)).foregroundStyle(Chrome.slate500)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.top, 8)

            Text(Strings.t("settings.position"))
                .font(Chrome.font(12, .medium)).foregroundStyle(Chrome.slate300)
                .padding(.top, 14)
            HStack(alignment: .top, spacing: 8) {
                edge(.top, "settings.position.top", vertical: true, far: false)
                edge(.bottom, "settings.position.bottom", vertical: true, far: true)
                edge(.left, "settings.position.left", vertical: false, far: false)
                edge(.right, "settings.position.right", vertical: false, far: true)
            }
            .padding(.top, 8)
        }
    }

    private var hintKey: String {
        switch model.settings.panel {
        case .alwaysExpanded: return "settings.panel.alwaysHint"
        case .hidden: return "settings.panel.hiddenHint"
        default: return "settings.panel.hoverHint"
        }
    }

    private func tile<Diagram: View>(_ mode: PanelMode, _ key: String, @ViewBuilder diagram: @escaping () -> Diagram) -> some View {
        ChromeTile(label: Strings.t(key), selected: model.settings.panel == mode, action: { model.set(\.panel, mode) }, diagram: diagram)
    }

    private func edge(_ dockEdge: DockEdge, _ key: String, vertical: Bool, far: Bool) -> some View {
        let selected = model.settings.edge == dockEdge
        return ChromeTile(label: Strings.t(key), selected: selected, action: { model.set(\.edge, dockEdge) }) {
            ChromeEdgeDiagram(vertical: vertical, far: far, selected: selected)
        }
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
                ChromeRow(label: Strings.t("settings.thresholdsField"), hint: Strings.t("settings.thresholdsFieldHint")) { EmptyView() }
                ChromeWrap(spacing: 8) {
                    ForEach(offered(AlertsSection.thresholds, including: model.settings.thresholds), id: \.self) { pct in
                        ChromeChip(label: "\(pct)%", round: false, selected: model.settings.thresholds.contains(pct)) { toggle(pct) }
                    }
                }
                .padding(.top, 8)
            }
            AlertCard(label: Strings.t("settings.resetSoon"), hint: Strings.t("settings.resetSoonHint"), isOn: model.binding(\.alertResetSoon)) {
                ChromeRule().padding(.top, 10)
                ChromeRow(label: Strings.t("settings.leadTime")) { EmptyView() }
                ChromeWrap(spacing: 8) {
                    ForEach(offered(AlertsSection.leadMinutes, including: [model.settings.resetLeadMinutes]), id: \.self) { minutes in
                        ChromeChip(label: "\(minutes)", selected: model.settings.resetLeadMinutes == minutes) { model.set(\.resetLeadMinutes, minutes) }
                    }
                }
                .padding(.top, 8)
            }
            AlertCard(label: Strings.t("settings.waiting"), hint: Strings.t("settings.waitingHint"), isOn: model.binding(\.alertWaiting)) {
                ChromeRule().padding(.top, 10)
                ChromeRow(label: Strings.t("settings.afterWaiting")) { EmptyView() }
                ChromeWrap(spacing: 8) {
                    ForEach(offered(AlertsSection.waitingSeconds, including: [model.settings.waitingDebounceSeconds]), id: \.self) { seconds in
                        ChromeChip(label: "\(seconds)", selected: model.settings.waitingDebounceSeconds == seconds) { model.set(\.waitingDebounceSeconds, seconds) }
                    }
                }
                .padding(.top, 8)
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

    /// The numbers these three settings can take. They used to be typed in, which let a slip of the
    /// keyboard silence every alert; the alert engine only ever wanted a handful of values anyway.
    private static let thresholds = [50, 60, 70, 80, 90, 95]
    private static let leadMinutes = [5, 10, 15, 30, 60]
    private static let waitingSeconds = [10, 20, 30, 60]

    /// A value saved before this window offered a fixed set keeps its own chip, so nothing is dropped
    /// behind the user's back; picking any other one retires it.
    private func offered(_ choices: [Int], including current: [Int]) -> [Int] {
        Array(Set(choices).union(current)).sorted()
    }

    /// The last threshold cannot be cleared: with none left the alert would be on and silent.
    private func toggle(_ pct: Int) {
        var next = Set(model.settings.thresholds)
        if next.contains(pct) {
            guard next.count > 1 else { return }
            next.remove(pct)
        } else {
            next.insert(pct)
        }
        model.set(\.thresholds, next.sorted())
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
            ChromeSwitchRow(label: label, hint: hint, on: isOn) { isOn = $0 }
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
    @ObservedObject var installer: InstallAssistant
    let finish: ([String], Bool) -> Void
    @State private var chosen: Set<String>

    init(detected: [ProviderSummary], absent: [ProviderSummary], installer: InstallAssistant, finish: @escaping ([String], Bool) -> Void) {
        self.detected = detected
        self.absent = absent
        self.installer = installer
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
                    ChromeBody(Strings.t("card.keychainWhy"), size: 11)
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
        .sheet(item: $installer.sheet) { request in
            RunSheet(request: request, missingManager: installer.missingManager(request.recipe),
                     run: { installer.run(request) }, dismiss: { installer.sheet = nil })
        }
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
                            if let recipe = installer.recipe(provider.id) {
                                ChromeBody(InstallDetail.text(provider, recipe, installer), size: 11)
                                InstallStrip(state: installer.state(provider)).padding(.top, 4)
                            } else {
                                ChromeBody(provider.signIn.explanation, size: 11)
                            }
                        }
                        Spacer(minLength: 8)
                        if installer.recipe(provider.id) != nil, installer.state(provider) == .notInstalled {
                            ChromeButton(label: Strings.t("install.button.install"), primary: true) {
                                installer.ask(provider, .install)
                            }
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
        let host = NSHostingController(rootView: content)
        /// The window draws its own title bar, so the content has to reach the top of the frame. Without
        /// this SwiftUI insets it by the title bar's safe area and our header lands in a second row, under
        /// the close, minimise and zoom buttons instead of level with them.
        host.safeAreaRegions = []
        window = NSWindow(contentViewController: host)
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
