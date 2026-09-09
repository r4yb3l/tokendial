import XCTest
@testable import TokendialCore

/// The recipes come from the shared specs, so these read the same files the app ships.
final class InstallCatalogTests: XCTestCase {
    override func setUpWithError() throws {
        InstallCatalog.load(from: try Docs.path("providers"))
        Strings.load(from: try Docs.path("i18n"), language: "en")
    }

    func testEveryToolThatCanBeInstalledHasARecipe() {
        XCTAssertEqual(InstallCatalog.all.keys.sorted(),
                       ["antigravity", "claude", "codex", "copilot", "cursor", "gemini", "grok", "opencode"])
        // GLM is an API key rather than a tool, so there is nothing to install.
        XCTAssertNil(InstallCatalog.recipe("glm"))
    }

    func testAProfileSharesItsToolsRecipe() {
        XCTAssertEqual(InstallCatalog.recipe("claude-work"), InstallCatalog.recipe("claude"))
    }

    func testRecipesAreComplete() throws {
        for (id, recipe) in InstallCatalog.all {
            XCTAssertFalse(recipe.vendor.isEmpty, id)
            XCTAssertTrue(recipe.docsUrl.hasPrefix("https://"), id)
            XCTAssertFalse(recipe.install.isEmpty, id)
            XCTAssertFalse(recipe.detect.commands.isEmpty && recipe.detect.paths.isEmpty, id)
            for manager in recipe.requires { XCTAssertTrue(["brew", "npm"].contains(manager), "\(id): \(manager)") }
            if recipe.install.hasPrefix("brew install") { XCTAssertTrue(recipe.needsBrew, id) }
            if recipe.install.hasPrefix("npm install -g") { XCTAssertTrue(recipe.needsNpm, id) }
            if let download = recipe.downloadUrl { XCTAssertTrue(download.hasPrefix("https://"), id) }
            if recipe.kind == .cli {
                let hint = try XCTUnwrap(recipe.signIn?.hint, id)
                XCTAssertNotNil(Strings.keys("en")[hint], "\(id): \(hint)")
            }
        }
        XCTAssertNotNil(Strings.keys("en")["install.hint.app"])
    }
}

final class ToolLocatorTests: XCTestCase {
    private var home: URL!

    override func setUpWithError() throws {
        home = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: home, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: home)
    }

    private func make(_ relative: String, directory: Bool = false) throws -> URL {
        let url = home.appendingPathComponent(relative)
        try FileManager.default.createDirectory(at: directory ? url : url.deletingLastPathComponent(),
                                                withIntermediateDirectories: true)
        if !directory { try Data().write(to: url) }
        return url
    }

    func testFindsADeclaredPathUnderHome() throws {
        _ = try make(".local/bin/claude")
        let locator = ToolLocator(path: "", home: home)
        XCTAssertTrue(locator.isInstalled(Detect(paths: ["~/.local/bin/claude"])))
        XCTAssertFalse(locator.isInstalled(Detect(paths: ["~/.local/bin/codex"])))
    }

    /// A cask installs an app bundle, which is a directory; the tool is there all the same.
    func testAnAppBundleCounts() throws {
        _ = try make("Applications/Cursor.app", directory: true)
        let locator = ToolLocator(path: "", home: home)
        XCTAssertTrue(locator.isInstalled(Detect(paths: ["~/Applications/Cursor.app"])))
    }

    func testAStarSegmentMatchesAVersionedDirectory() throws {
        _ = try make("Packages/opencode-1.2.3/opencode")
        let locator = ToolLocator(path: "", home: home)
        XCTAssertTrue(locator.isInstalled(Detect(paths: ["~/Packages/opencode-*/opencode"])))
        XCTAssertFalse(locator.isInstalled(Detect(paths: ["~/Packages/codex-*/codex"])))
    }

    func testScansThePath() throws {
        let bin = try make("brewbin/gemini").deletingLastPathComponent()
        let locator = ToolLocator(path: "/nowhere:\(bin.path)", home: home)
        XCTAssertEqual(locator.resolve(Detect(commands: ["gemini"])), bin.appendingPathComponent("gemini").path)
        XCTAssertNil(locator.resolve(Detect(commands: ["grok"])))
    }

    /// The spec's own locations win: an app launched by launchd rarely has the right PATH to trust.
    func testDeclaredPathsWinOverThePath() throws {
        let declared = try make(".local/bin/codex")
        let bin = try make("otherbin/codex").deletingLastPathComponent()
        let locator = ToolLocator(path: bin.path, home: home)
        XCTAssertEqual(locator.resolve(Detect(commands: ["codex"], paths: ["~/.local/bin/codex"])), declared.path)
    }
}

final class InstallScriptTests: XCTestCase {
    private let marker = URL(fileURLWithPath: "/tmp/tokendial-test.done")

    override func setUpWithError() throws {
        InstallCatalog.load(from: try Docs.path("providers"))
        Strings.load(from: try Docs.path("i18n"), language: "en")
    }

    private func recipe(_ id: String) throws -> InstallRecipe { try XCTUnwrap(InstallCatalog.recipe(id)) }

    func testShowsTheCommandThenRunsItInAChildShellAndStopsOnFailure() throws {
        let script = InstallScript.compose("OpenCode", try recipe("opencode"), action: .install, marker: marker)
        let shown = try XCTUnwrap(script.range(of: "> %s"))
        let ran = try XCTUnwrap(script.range(of: "/bin/sh -c 'brew install opencode'"))
        XCTAssertLessThan(shown.lowerBound, ran.lowerBound)
        XCTAssertTrue(script.contains("if [ $? -ne 0 ]; then"))
        XCTAssertTrue(script.contains("trap finish EXIT"))
        XCTAssertTrue(script.contains("Done. You can close this window."))
    }

    func testChainsTheSignInAfterInstallingACli() throws {
        let script = InstallScript.compose("Grok", try recipe("grok"), action: .install, marker: marker)
        let install = try XCTUnwrap(script.range(of: "x.ai/cli/install.sh"))
        let signIn = try XCTUnwrap(script.range(of: "/bin/sh -c 'grok login'"))
        XCTAssertLessThan(install.lowerBound, signIn.lowerBound)
        XCTAssertTrue(script.contains("Grok opens the browser"))
    }

    func testSignInOnlyRunsNoInstaller() throws {
        let script = InstallScript.compose("Codex", try recipe("codex"), action: .signIn, marker: marker)
        XCTAssertFalse(script.contains("brew install --cask codex"))
        XCTAssertFalse(script.contains("if [ $? -ne 0 ]; then"))
        XCTAssertTrue(script.contains("/bin/sh -c 'codex login'"))
    }

    func testAnAppEndsByAskingTheUserToOpenIt() throws {
        let script = InstallScript.compose("Cursor", try recipe("cursor"), action: .install, marker: marker)
        XCTAssertTrue(script.contains("Open Cursor and sign in"))
        XCTAssertFalse(script.contains("/bin/sh -c 'Cursor'"))
    }

    func testAMissingManagerIsOfferedFirst() throws {
        let brew = InstallScript.compose("Cursor", try recipe("cursor"), action: .install, marker: marker)
        XCTAssertTrue(brew.contains("if ! command -v brew >/dev/null 2>&1; then"))
        XCTAssertFalse(brew.contains("command -v npm"))

        let npm = InstallScript.compose("Gemini CLI", try recipe("gemini"), action: .install, marker: marker)
        let brewFirst = try XCTUnwrap(npm.range(of: "command -v brew"))
        let thenNode = try XCTUnwrap(npm.range(of: "/bin/sh -c 'brew install node'"))
        XCTAssertLessThan(brewFirst.lowerBound, thenNode.lowerBound)
    }

    /// The security invariant: strip out the recipe's own strings and the two pinned manager lines, and no
    /// address or fetch may remain anywhere in the script.
    func testContainsOnlyRecipeTextAndTheFixedTemplate() throws {
        for (id, recipe) in InstallCatalog.all {
            var script = InstallScript.compose(id, recipe, action: .install, marker: marker)
            for text in [recipe.install, recipe.docsUrl, recipe.signIn?.command ?? "",
                         InstallScript.brewInstall, InstallScript.brewShellEnv, InstallScript.nodeInstall] where !text.isEmpty {
                script = script.replacingOccurrences(of: text, with: "")
            }
            XCTAssertFalse(script.contains("http"), id)
            XCTAssertFalse(script.contains("curl"), id)
        }
    }

    func testQuotingClosesAndReopensAroundASingleQuote() {
        XCTAssertEqual(InstallScript.quote("it's here"), "'it'\\''s here'")
        XCTAssertEqual(InstallScript.quote("plain"), "'plain'")
    }

    func testTheScriptIsWrittenExecutable() throws {
        let directory = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let script = try InstallScript.write(directory, providerId: "codex", content: "#!/bin/sh\n")
        XCTAssertEqual(script.lastPathComponent, "codex.command")
        let mode = try FileManager.default.attributesOfItem(atPath: script.path)[.posixPermissions] as? NSNumber
        XCTAssertEqual(mode?.int16Value, 0o755)
    }
}

final class InstallWatcherTests: XCTestCase {
    private var clock = Date(timeIntervalSince1970: 1_000_000)

    func testAdvancesToSignedInAndCompletesOnce() {
        var installed = false
        var signedIn = false
        var states: [InstallState] = []
        var outcomes: [InstallOutcome] = []
        let watcher = InstallWatcher(providerId: "codex", installed: { installed }, signedIn: { signedIn },
                                     now: { self.clock })
        watcher.advanced = { states.append($0) }
        watcher.completed = { outcomes.append($0) }

        watcher.tick()
        XCTAssertEqual(states, [])
        installed = true
        watcher.tick()
        XCTAssertEqual(states, [.installed])
        signedIn = true
        watcher.tick()
        XCTAssertEqual(states, [.installed, .signedIn])
        XCTAssertEqual(outcomes, [.success])
        watcher.tick()
        XCTAssertEqual(outcomes, [.success])
        XCTAssertFalse(watcher.running)
    }

    func testTimesOutAfterFifteenMinutes() {
        var outcomes: [InstallOutcome] = []
        let watcher = InstallWatcher(providerId: "grok", installed: { false }, signedIn: { false },
                                     now: { self.clock })
        watcher.completed = { outcomes.append($0) }
        clock += 14 * 60
        watcher.tick()
        XCTAssertEqual(outcomes, [])
        clock += 60
        watcher.tick()
        XCTAssertEqual(outcomes, [.timedOut])
    }

    func testTheMarkerEndsTheWaitWhenNothingSignedIn() {
        var outcomes: [InstallOutcome] = []
        let watcher = InstallWatcher(providerId: "grok", installed: { false }, signedIn: { false },
                                     terminalFinished: { true }, now: { self.clock })
        watcher.completed = { outcomes.append($0) }
        watcher.tick()
        XCTAssertEqual(outcomes, [.terminalClosed])
    }

    func testASignInThatLandsWithTheMarkerStillCounts() {
        var outcomes: [InstallOutcome] = []
        let watcher = InstallWatcher(providerId: "codex", installed: { true }, signedIn: { true },
                                     terminalFinished: { true }, now: { self.clock })
        watcher.completed = { outcomes.append($0) }
        watcher.tick()
        XCTAssertEqual(outcomes, [.success])
    }

    /// An app is installed by the terminal but signed into later in its own window, so the marker must not
    /// end the wait — unless the install never happened at all.
    func testAnInstalledAppKeepsWaitingPastTheMarker() {
        var signedIn = false
        var outcomes: [InstallOutcome] = []
        let watcher = InstallWatcher(providerId: "cursor", installed: { true }, signedIn: { signedIn },
                                     terminalFinished: { true }, waitsAfterTerminal: true, now: { self.clock })
        watcher.completed = { outcomes.append($0) }
        watcher.tick()
        XCTAssertEqual(outcomes, [])
        signedIn = true
        watcher.tick()
        XCTAssertEqual(outcomes, [.success])
    }

    func testAnAppThatNeverInstalledEndsWithTheMarker() {
        var outcomes: [InstallOutcome] = []
        let watcher = InstallWatcher(providerId: "cursor", installed: { false }, signedIn: { false },
                                     terminalFinished: { true }, waitsAfterTerminal: true, now: { self.clock })
        watcher.completed = { outcomes.append($0) }
        watcher.tick()
        XCTAssertEqual(outcomes, [.terminalClosed])
    }
}
