import AppKit
import SwiftUI
import TokendialCore

/// What the user is about to be shown before anything runs.
struct InstallRequest: Identifiable {
    let providerId: String
    let name: String
    let recipe: InstallRecipe
    let action: InstallAction

    var id: String { providerId + (action == .install ? ":install" : ":signIn") }
}

/// Owns the install recipes, what each provider's state is, and the watchers of the runs in flight. It
/// outlives the settings window on purpose: an install started from there keeps being watched after it
/// closes, the way it does on Windows.
final class InstallAssistant: ObservableObject {
    /// Bumped whenever a step is passed or a wait begins or ends; the windows redraw off this.
    @Published private(set) var revision = 0
    @Published var sheet: InstallRequest?

    var signedIn: ((String) -> Void)?

    private let providers: [UsageProvider]
    private let locator = ToolLocator()
    private let directory = Paths.appSupport.appendingPathComponent("install")
    private var watchers: [String: InstallWatcher] = [:]
    private var outcomes: [String: InstallOutcome] = [:]

    /// The managers a recipe can ask for, found the same way a tool is.
    private static let managers: [String: Detect] = [
        "brew": Detect(commands: ["brew"], paths: ["/opt/homebrew/bin/brew", "/usr/local/bin/brew"]),
        "npm": Detect(commands: ["npm"], paths: ["/opt/homebrew/bin/npm", "/usr/local/bin/npm"])
    ]

    init(providers: [UsageProvider]) {
        self.providers = providers
    }

    func recipe(_ providerId: String) -> InstallRecipe? { InstallCatalog.recipe(providerId) }

    /// Where a provider stands. The account comes from the summary the store already cached, never from a
    /// live read: that one can stop on a keychain prompt.
    func state(_ summary: ProviderSummary) -> InstallState {
        guard let recipe = recipe(summary.id) else { return .connected }
        let installed = ProviderFamily.isProfile(summary.id) || locator.isInstalled(recipe.detect)
        return InstallState.of(installed: installed, signedIn: summary.account != nil, connected: summary.connected)
    }

    /// True while this provider's row should be showing the assistant rather than the usual usage detail.
    func assisted(_ summary: ProviderSummary) -> Bool {
        recipe(summary.id) != nil && state(summary) != .connected
    }

    func waiting(_ providerId: String) -> Bool { watchers[providerId]?.running ?? false }

    func outcome(_ providerId: String) -> InstallOutcome? { outcomes[providerId] }

    /// The manager this recipe needs and the Mac does not have, if any.
    func missingManager(_ recipe: InstallRecipe) -> String? {
        recipe.requires.first { name in
            guard let detect = Self.managers[name] else { return false }
            return !locator.isInstalled(detect)
        }
    }

    func ask(_ summary: ProviderSummary, _ action: InstallAction) {
        guard let recipe = recipe(summary.id) else { return }
        sheet = InstallRequest(providerId: summary.id, name: summary.name, recipe: recipe, action: action)
    }

    /// Write the script, open it in the user's terminal, and watch until the tool and its credential are
    /// there. Nothing here runs a command in the app's own process.
    func run(_ request: InstallRequest) {
        guard let provider = providers.first(where: { $0.id == request.providerId }) else { return }
        let recipe = signInCommand(for: request)
        let paths = InstallScript.paths(directory, providerId: request.providerId)
        try? FileManager.default.removeItem(at: paths.marker)
        let script = InstallScript.compose(request.name, recipe, action: request.action, marker: paths.marker)
        do {
            try InstallScript.write(directory, providerId: request.providerId, content: script)
        } catch {
            Log.ui.error("install \(request.providerId): could not write the script, \(error.localizedDescription)")
            return
        }

        watchers[request.providerId]?.stop()
        outcomes[request.providerId] = nil
        let watcher = InstallWatcher(
            providerId: request.providerId,
            installed: { [locator] in locator.isInstalled(recipe.detect) },
            signedIn: { provider.account() != nil },
            terminalFinished: { FileManager.default.fileExists(atPath: paths.marker.path) },
            waitsAfterTerminal: recipe.kind == .app)
        watcher.advanced = { [weak self] _ in self?.bump() }
        watcher.completed = { [weak self] outcome in
            guard let self else { return }
            DispatchQueue.main.async {
                self.outcomes[request.providerId] = outcome
                Log.ui.info("install \(request.providerId): \(String(describing: outcome))")
                if outcome == .success { self.signedIn?(request.providerId) }
                self.bump()
            }
        }
        guard TerminalRunner.open(paths.script) else {
            Log.ui.error("install \(request.providerId): no terminal would open the script")
            return
        }
        watchers[request.providerId] = watcher
        watcher.start()
        bump()
    }

    func stop() {
        for watcher in watchers.values { watcher.stop() }
        watchers = [:]
    }

    /// A profile signs in through its own directory, so the recipe's command is rewritten for it.
    private func signInCommand(for request: InstallRequest) -> InstallRecipe {
        guard let claude = providers.first(where: { $0.id == request.providerId }) as? ClaudeProvider,
              ProviderFamily.isProfile(request.providerId), var recipe = recipe(request.providerId),
              let step = recipe.signIn else { return request.recipe }
        recipe.signIn = SignInStep(command: claude.profile.signInCommand, hint: step.hint)
        return recipe
    }

    private func bump() {
        if Thread.isMainThread { revision += 1 } else { DispatchQueue.main.async { self.revision += 1 } }
    }
}

/// Opens the script in whichever terminal the user actually uses. The file is a `.command`, so the default
/// handler is Terminal unless they changed it; either way the app never owns that process, which is why the
/// script reports its own ending with a marker file.
enum TerminalRunner {
    static func open(_ script: URL) -> Bool {
        guard FileManager.default.fileExists(atPath: script.path) else { return false }
        if NSWorkspace.shared.open(script) { return true }
        guard let terminal = NSWorkspace.shared.urlForApplication(withBundleIdentifier: "com.apple.Terminal") else { return false }
        NSWorkspace.shared.open([script], withApplicationAt: terminal, configuration: NSWorkspace.OpenConfiguration())
        return true
    }
}

/// The three dots under a provider that is still being set up.
struct InstallStrip: View {
    let state: InstallState

    private var steps: [(String, InstallState)] {
        [(Strings.t("install.step.install"), .installed),
         (Strings.t("install.step.signIn"), .signedIn),
         (Strings.t("install.step.ready"), .connected)]
    }

    var body: some View {
        HStack(spacing: 6) {
            ForEach(Array(steps.enumerated()), id: \.offset) { index, step in
                if index > 0 {
                    Rectangle().fill(state >= steps[index - 1].1 ? Chrome.accent : Chrome.surface700)
                        .frame(width: 16, height: 1)
                }
                dot(done: state >= step.1, current: current(index))
                Text(step.0)
                    .font(Chrome.font(10))
                    .foregroundStyle(state >= step.1 || current(index) ? Chrome.slate200 : Chrome.slate500)
            }
        }
    }

    private func current(_ index: Int) -> Bool {
        guard state < steps[index].1 else { return false }
        return index == 0 || state >= steps[index - 1].1
    }

    @ViewBuilder private func dot(done: Bool, current: Bool) -> some View {
        Circle()
            .fill(done ? Chrome.accent : .clear)
            .overlay(Circle().stroke(done || current ? Chrome.accent : Chrome.surface600, lineWidth: 1))
            .frame(width: 7, height: 7)
    }
}

/// The one line under the provider name while the assistant owns the row.
enum InstallDetail {
    static func text(_ summary: ProviderSummary, _ recipe: InstallRecipe, _ assistant: InstallAssistant) -> String {
        if assistant.waiting(summary.id) { return Strings.t("install.waiting", ["name": summary.name]) }
        let state = assistant.state(summary)
        if state < .signedIn, let outcome = assistant.outcome(summary.id), outcome != .success {
            return Strings.t("install.state.notSeen")
        }
        let where_ = switch state {
        case .notInstalled: Strings.t("install.state.notInstalled")
        case .installed: Strings.t("install.state.installed")
        default: Strings.t("install.state.signedIn")
        }
        return Strings.t("install.state.by", ["vendor": recipe.vendor, "state": where_])
    }
}

/// The sheet the user reads before anything runs: the exact command, where it comes from, and the buttons.
struct RunSheet: View {
    let request: InstallRequest
    let missingManager: String?
    let run: () -> Void
    let dismiss: () -> Void

    private var command: String {
        request.action == .install ? request.recipe.install : (request.recipe.signIn?.command ?? "")
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ChromeHeading(Strings.t(request.action == .install ? "install.sheet.title" : "install.sheet.signInTitle",
                                    ["name": request.name]))
            ChromeBody(Strings.t(request.action == .install ? "install.sheet.intro" : "install.sheet.signInIntro",
                                 ["vendor": request.recipe.vendor, "name": request.name])).padding(.top, 8)

            if let missingManager {
                ChromeBody(Strings.t("install.requires.\(missingManager)"), size: 11)
                    .foregroundStyle(Chrome.accent)
                    .padding(.top, 10)
            }

            Text(command)
                .font(Chrome.mono(12))
                .foregroundStyle(Chrome.slate200)
                .textSelection(.enabled)
                .environment(\.layoutDirection, .leftToRight)
                .frame(maxWidth: .infinity, alignment: .leading)
                .chromeCard(Chrome.windowBackground, Chrome.surface700, padding: 10)
                .padding(.top, 10)

            ChromeBody(hint, size: 11).padding(.top, 10)
            ChromeBody(Strings.t("install.sheet.sudo"), size: 11).padding(.top, 6)

            HStack(spacing: 8) {
                ChromeButton(label: Strings.t("install.button.run"), primary: true) { dismiss(); run() }
                ChromeButton(label: Strings.t("install.button.copy")) {
                    NSPasteboard.general.clearContents()
                    NSPasteboard.general.setString(command, forType: .string)
                }
                ChromeButton(label: Strings.t("install.sheet.docs")) { open(request.recipe.docsUrl) }
                if request.action == .install, let download = request.recipe.downloadUrl {
                    ChromeButton(label: Strings.t("install.button.download")) { open(download) }
                }
                Spacer()
                ChromeButton(label: Strings.t("install.button.cancel")) { dismiss() }
                    .keyboardShortcut(.cancelAction)
            }
            .padding(.top, 16)
        }
        .padding(20)
        .frame(width: 560)
        .background(Chrome.windowBackground)
        .environment(\.layoutDirection, Strings.rightToLeft ? .rightToLeft : .leftToRight)
    }

    private var hint: String {
        if request.recipe.kind == .app { return Strings.t("install.hint.app", ["name": request.name]) }
        return Strings.t(request.recipe.signIn?.hint ?? "install.hint.app", ["name": request.name])
    }

    private func open(_ target: String) {
        guard let url = URL(string: target) else { return }
        NSWorkspace.shared.open(url)
    }
}
