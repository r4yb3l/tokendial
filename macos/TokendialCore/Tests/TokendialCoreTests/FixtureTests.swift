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
        XCTAssertEqual("Paused until Wed 4:00 AM", Copy.until("Paused", now.addingTimeInterval(6 * 3600 - 13 * 60 - 20), now: now, zone: utc))
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
                                      credentialReader: { _ in ClaudeCredential(accessToken: "sk-test", expiresAt: .distantFuture, plan: "pro") })
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
}
