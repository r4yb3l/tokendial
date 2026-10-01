import Foundation
import XCTest
@testable import TokendialCore

/// Every response fixture in docs/fixtures is parsed by its provider and compared to what the spec says it means.
final class FixtureTests: XCTestCase {
    func testEveryFixtureParsesAsSpecified() throws {
        let files = try Docs.files(Docs.path("fixtures"), recursive: true)
        XCTAssertGreaterThan(files.count, 20)
        for file in files {
            try check(file)
        }
    }

    private func check(_ file: URL) throws {
        let name = file.lastPathComponent
        let provider = file.deletingLastPathComponent().lastPathComponent
        let root = try XCTUnwrap(JSON.object(Data(contentsOf: file)), name)
        let response = try JSONSerialization.data(withJSONObject: root["response"] ?? [:], options: [.fragmentsAllowed])
        let expected = try XCTUnwrap(root.obj("expected"), name)
        let now = root.num("now").map { Date(timeIntervalSince1970: $0) } ?? Date()

        if expected["gate"] != nil { return }
        if let token = expected.str("accessToken") {
            let text = String(decoding: response, as: UTF8.self)
            let credential = try XCTUnwrap(AntigravityUsage.Credential.decode("go-keyring-base64:" + Data(text.utf8).base64EncodedString()), name)
            XCTAssertEqual(token, credential.accessToken, name)
            XCTAssertEqual(expected.str("authMethod"), credential.authMethod, name)
            XCTAssertEqual(JSON.iso(expected.str("expiresAtUtc")), credential.expiresAt, name)
            return
        }

        func parse() throws -> Parsed {
            switch (provider, name) {
            case ("claude", _): return try ClaudeUsage.parse(response)
            case ("codex", _): return try CodexUsage.parse(response, now: now)
            case ("copilot", _): return try CopilotUsage.parse(response)
            case ("cursor", _): return try CursorUsage.parse(response)
            case ("glm", _): return try GlmUsage.parse(response)
            case ("grok", _): return try GrokUsage.parse(response)
            case ("opencode", _): return try OpenCodeUsage.parse(response)
            case ("antigravity", "bridge-quota.json"): return Parsed(windows: AntigravityUsage.parseBridge(response), headline: "gemini-weekly")
            case ("antigravity", "google-quota.json"): return Parsed(windows: AntigravityUsage.parseGoogleQuota(response), headline: nil)
            // The unsupported-client answer is the gate turning the account away, not a quota.
            case ("gemini", "unsupported-client.json"): return try GeminiUsage.parseGate(response)
            case ("gemini", _): return try GeminiUsage.parse(response)
            default: throw XCTSkip("no parser for \(provider)/\(name)")
            }
        }

        if let error = expected.str("error") {
            do {
                _ = try parse()
                XCTFail("\(name): expected \(error)")
            } catch let thrown as UsageError {
                let kind: String
                switch thrown.kind {
                case .needsSignIn: kind = "needsAuth"
                case .credentialExpired: kind = "credentialExpired"
                case .rateLimited: kind = "rateLimited"
                case .nothingMetered: kind = "nothingMetered"
                case .badResponse: kind = "badResponse"
                }
                XCTAssertEqual(error, kind, name)
                if let status = expected.num("status") { XCTAssertEqual(Int(status), thrown.status, name) }
            }
            return
        }

        let parsed = try parse()
        if let headline = expected.str("headline") { XCTAssertEqual(headline, parsed.headline, name) }
        if let plan = expected.str("plan") { XCTAssertEqual(plan, parsed.plan, name) }
        let windows = expected.objects("windows")
        XCTAssertEqual(windows.compactMap { $0.str("id") }, parsed.windows.map { $0.id }, name)
        for (i, window) in windows.enumerated() where i < parsed.windows.count {
            XCTAssertEqual(window.str("label"), parsed.windows[i].label, name)
            XCTAssertEqual(try XCTUnwrap(window.num("usedFraction")), try XCTUnwrap(parsed.windows[i].usedFraction), accuracy: 0.00001, name)
            if let reset = window.str("resetsAt") {
                XCTAssertEqual(JSON.iso(reset), parsed.windows[i].resetsAt, name)
            } else {
                XCTAssertNil(parsed.windows[i].resetsAt, name)
            }
        }
    }
}

final class CopyTests: XCTestCase {
    private let now = Date(timeIntervalSince1970: 1_700_000_000)
    private let utc = TimeZone(identifier: "UTC")!

    override func setUpWithError() throws {
        Strings.load(from: try Docs.path("i18n"), language: "en")
    }

    func testResetCopyFollowsTheRules() {
        XCTAssertEqual("Resets in 51 min", Copy.reset(now.addingTimeInterval(51 * 60), now: now))
        XCTAssertEqual("Resets in 50 min", Copy.reset(now.addingTimeInterval(50 * 60 + 20), now: now))
        XCTAssertEqual("Resets in 51 min", Copy.reset(now.addingTimeInterval(50 * 60 + 40), now: now))
        XCTAssertFalse(Copy.reset(now.addingTimeInterval(59 * 60 + 40), now: now).contains("min"))
        XCTAssertTrue(Copy.reset(now.addingTimeInterval(6 * 3600), now: now, zone: utc).contains(":"))
        XCTAssertEqual("Resets Nov 21", Copy.reset(now.addingTimeInterval(7 * 86400), now: now, zone: utc))
        XCTAssertEqual("Resetting…", Copy.reset(now.addingTimeInterval(-5), now: now))
    }

    func testElapsedCopyFollowsTheRules() {
        XCTAssertEqual("just now", Copy.elapsed(now.addingTimeInterval(-44), now: now))
        XCTAssertEqual("6 min", Copy.elapsed(now.addingTimeInterval(-6 * 60), now: now))
        XCTAssertEqual("1 hr", Copy.elapsed(now.addingTimeInterval(-60 * 60), now: now))
        XCTAssertEqual("1 hr 5 min", Copy.elapsed(now.addingTimeInterval(-65 * 60), now: now))
        XCTAssertEqual("just now", Copy.elapsed(now.addingTimeInterval(120), now: now))
        XCTAssertEqual("6 min ago", Copy.ago(now.addingTimeInterval(-6 * 60), now: now))
        XCTAssertEqual("Paused until Wed 4:00 AM", plainSpaces(Copy.until("Paused", now.addingTimeInterval(6 * 3600 - 13 * 60 - 20), now: now, zone: utc)))
    }

    /// macOS formats times with a narrow no-break space before AM/PM; compare on plain spaces so the
    /// assertions read as the copy does and do not depend on the system's ICU version.
    private func plainSpaces(_ text: String) -> String {
        text.replacingOccurrences(of: "\u{202F}", with: " ").replacingOccurrences(of: "\u{00A0}", with: " ")
    }

    func testSummaryReadsBothEnds() {
        XCTAssertEqual("63% used · 37% left", UsageWindow(id: "w", label: "W", usedFraction: 0.63).summary(.official))
        XCTAssertEqual("~104% used · 0% left", UsageWindow(id: "w", label: "W", usedFraction: 1.04).summary(.derived))
        XCTAssertEqual("~7 requests today", UsageWindow(id: "w", label: "W", count: 7).summary(.derived))
        XCTAssertEqual(Band.watch, Band.of(0.5))
        XCTAssertEqual(Band.critical, Band.of(0.8))
        XCTAssertEqual(Band.ample, Band.of(0.49))
    }
}

final class SecurityTests: XCTestCase {
    private final class Redirecting: Transport {
        var seen: [URL] = []
        func send(_ request: URLRequest) async throws -> (data: Data, response: HTTPURLResponse) {
            seen.append(request.url!)
            let response = HTTPURLResponse(url: request.url!, statusCode: 302, httpVersion: nil, headerFields: ["Location": "https://evil.example/usage"])!
            return (Data(), response)
        }
    }

    func testARedirectIsABadResponseNotASecondRequest() async throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("tokendial-sec-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let transport = Redirecting()
        let provider = ClaudeProvider(profile: ClaudeProfile(slug: nil, directory: directory), transport: transport, archive: ReadingArchive(directory: directory),
                                      newestItem: { _ in nil },
                                      credentialReader: { _, _ in ClaudeCredential(accessToken: "sk-test", expiresAt: .distantFuture, plan: "pro") })
        do {
            _ = try await provider.read()
            XCTFail("expected a bad response")
        } catch let error as UsageError {
            XCTAssertEqual(.badResponse, error.kind)
            XCTAssertEqual(302, error.status)
        }
        XCTAssertEqual(1, transport.seen.count)
        XCTAssertEqual("api.anthropic.com", transport.seen.first?.host)
    }

    func testSelfSignedCertificatesAreOnlyAcceptedOnLoopback() {
        XCTAssertTrue(SessionTransport.isLoopback("127.0.0.1"))
        XCTAssertTrue(SessionTransport.isLoopback("localhost"))
        XCTAssertTrue(SessionTransport.isLoopback("::1"))
        XCTAssertFalse(SessionTransport.isLoopback("cloudcode-pa.googleapis.com"))
        XCTAssertFalse(SessionTransport.isLoopback("127.0.0.1.evil.example"))
    }

    /// gh keeps one block per host and, since 2.40, every signed-in account's token under users:. The first oauth_token
    /// in the file used to win, which here is an Enterprise host's; only github.com's own, the active account's, may
    /// reach api.github.com.
    func testOnlyGitHubDotComsActiveTokenLeavesTheGhHostsFile() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("tokendial-sec-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let hosts = directory.appendingPathComponent("hosts.yml")
        try Data("""
            ghe.corp.example:
                oauth_token: ghe_enterprise
                user: ada-corp
            github.com:
                users:
                    ada:
                        oauth_token: gho_personal
                    ada-work:
                        oauth_token: gho_work
                git_protocol: https
                user: ada-work
                oauth_token: gho_work
            """.utf8).write(to: hosts)
        let credential = try XCTUnwrap(try CopilotCredential.fromGhHosts(hosts))
        XCTAssertEqual("gho_work", credential.token)
        XCTAssertEqual("ada-work", credential.user)

        let keyring = directory.appendingPathComponent("keyring.yml")
        try Data("""
            ghe.corp.example:
                oauth_token: ghe_enterprise
            github.com:
                user: ada
            """.utf8).write(to: keyring)
        XCTAssertNil(try CopilotCredential.fromGhHosts(keyring))
    }

    /// A bare "github.com" prefix also matched github.company.com, an Enterprise host.
    func testACopilotPluginEntryIsGitHubDotComsOnlyWhenItsKeySaysSoWhole() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("tokendial-sec-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let enterprise = directory.appendingPathComponent("enterprise.json")
        try Data(#"{"github.company.com:Iv1.x":{"oauth_token":"ghe_token"}}"#.utf8).write(to: enterprise)
        XCTAssertNil(try CopilotCredential.fromPluginFile(enterprise))
        let both = directory.appendingPathComponent("apps.json")
        try Data(#"{"github.company.com:Iv1.x":{"oauth_token":"ghe_token"},"github.com:Iv1.y":{"oauth_token":"gho_token","user":"ada"}}"#.utf8).write(to: both)
        XCTAssertEqual("gho_token", try CopilotCredential.fromPluginFile(both)?.token)
    }

    /// Windsurf and the Codeium extensions run the same language_server with the same flags; the first one found used to win.
    func testTheBridgeIsAntigravitysOwnLanguageServerAndNeverAnotherProducts() {
        let processes = [
            "1 /Applications/Windsurf.app/Contents/Resources/app/extensions/windsurf/bin/language_server_macos_arm --csrf_token w",
            "2 /Users/ada/.vscode/extensions/codeium.codeium-1.2.3/dist/language_server_macos_arm --csrf_token c",
            "3 /opt/tools/language_server_macos_arm --csrf_token u",
            "4 /Applications/Antigravity.app/Contents/Resources/app/extensions/antigravity/bin/language_server_macos_arm --csrf_token a"
        ]
        XCTAssertEqual(["4", "3"], Bridge.candidates(processes).map { String($0.prefix(1)) })
    }

    /// A profile's directory name is whatever the user called a folder; inside sh's double quotes it must stay text.
    func testAProfileSignInKeepsItsSlugInsideTheQuotes() {
        let home = URL(fileURLWithPath: "/tmp")
        XCTAssertEqual(#"CLAUDE_CONFIG_DIR="$HOME/.claude-work" claude"#, ClaudeProfile(slug: "work", directory: home).signInCommand)
        XCTAssertEqual(#"CLAUDE_CONFIG_DIR="$HOME/.claude-it's \$(x) \`y\` \"z\" \\" claude"#, ClaudeProfile(slug: #"it's $(x) `y` "z" \"#, directory: home).signInCommand)
    }
}

/// What a provider says when it cannot get a usable answer right now. None of these is a sign-out: a sign-out erases
/// the last reading and sends a signed-in user to sign in again.
final class CredentialReadTests: XCTestCase {
    private var directory: URL!

    override func setUpWithError() throws {
        directory = FileManager.default.temporaryDirectory.appendingPathComponent("tokendial-tests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: directory)
    }

    private func kind(_ error: Error) -> UsageErrorKind? { (error as? UsageError)?.kind }

    /// The owner rewrites its file in place when it refreshes the token, so a read can land on an empty one.
    func testAHalfWrittenCredentialIsTransientNotASignOut() throws {
        let empty = directory.appendingPathComponent("credential.json")
        try Data().write(to: empty)
        let none = directory.appendingPathComponent("none.json")
        XCTAssertThrowsError(try CodexCredential.read(file: empty, now: Date())) { XCTAssertEqual(.credentialExpired, self.kind($0)) }
        XCTAssertThrowsError(try GrokCredential.read(file: empty, now: Date())) { XCTAssertEqual(.credentialExpired, self.kind($0)) }
        XCTAssertThrowsError(try GeminiCredential.read(file: empty, settings: none, accounts: none)) { XCTAssertEqual(.credentialExpired, self.kind($0)) }
        XCTAssertThrowsError(try ClaudeCredential.parse(Data())) { XCTAssertEqual(.credentialExpired, self.kind($0)) }

        XCTAssertThrowsError(try CodexCredential.read(file: none, now: Date())) { XCTAssertEqual(.needsSignIn, self.kind($0)) }
        XCTAssertThrowsError(try ClaudeCredential.parse(Data("{}".utf8))) { XCTAssertEqual(.needsSignIn, self.kind($0)) }
    }

    /// GitHub answers an exhausted rate limit with 403 as often as 429; only the headers tell it from a missing seat.
    func testACopilot403IsARateLimitOnlyWhenGitHubSaysSo() throws {
        let url = try XCTUnwrap(URL(string: "https://api.github.com/copilot_internal/user"))
        func forbidden(_ headers: [String: String]) throws -> HTTPURLResponse {
            try XCTUnwrap(HTTPURLResponse(url: url, statusCode: 403, httpVersion: nil, headerFields: headers))
        }
        let exhausted = try forbidden(["x-ratelimit-remaining": "0"])
        let throttled = try forbidden(["Retry-After": "30"])
        let seatless = try forbidden(["x-ratelimit-remaining": "4999"])
        XCTAssertTrue(CopilotUsage.rateLimited(exhausted))
        XCTAssertTrue(CopilotUsage.rateLimited(throttled))
        XCTAssertFalse(CopilotUsage.rateLimited(seatless))
    }
}
