import Foundation
import XCTest
@testable import TokendialCore

private final class Answer: Transport {
    let status: Int
    private(set) var tokens: [String] = []

    init(_ status: Int) { self.status = status }

    func send(_ request: URLRequest) async throws -> (data: Data, response: HTTPURLResponse) {
        tokens.append(request.value(forHTTPHeaderField: "Authorization") ?? "")
        return (Data(), HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: nil, headerFields: nil)!)
    }
}

private func failure(_ provider: UsageProvider) async -> Error? {
    do {
        _ = try await provider.read()
        return nil
    } catch {
        return error
    }
}

final class ClaudeKeychainTests: XCTestCase {
    private var directory: URL!
    private var clock = Date(timeIntervalSince1970: 1_790_000_000)

    override func setUpWithError() throws {
        directory = FileManager.default.temporaryDirectory.appendingPathComponent("tokendial-keychain-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: directory)
    }

    /// Expected values from node, which is how Claude Code itself derives them:
    /// node -e "console.log(require('crypto').createHash('sha256').update('/Users/x/.claude-work').digest('hex').slice(0,8))"
    func testAProfileIsFiledUnderTheHashOfItsDirectory() {
        let work = ClaudeProfile(slug: "work", directory: URL(fileURLWithPath: "/Users/x/.claude-work"))
        XCTAssertEqual("Claude Code-credentials-74ed04d5", ClaudeCredential.keychainService(work))
        let slashed = ClaudeProfile(slug: "work", directory: URL(fileURLWithPath: "/Users/x/.claude-work/", isDirectory: true))
        XCTAssertEqual("Claude Code-credentials-74ed04d5", ClaudeCredential.keychainService(slashed))
        let discovered = ClaudeProfile(slug: "personal", directory: URL(fileURLWithPath: "/Users/ana", isDirectory: true).appendingPathComponent(".claude-personal"))
        XCTAssertEqual("Claude Code-credentials-ec56f7a8", ClaudeCredential.keychainService(discovered))
    }

    func testTheDefaultProfileKeepsTheBareService() {
        XCTAssertEqual("Claude Code-credentials", ClaudeCredential.keychainService(.default(home: URL(fileURLWithPath: "/Users/x", isDirectory: true))))
    }

    func testTheNewestItemIsTheOneRead() {
        let old = Keychain.Item(account: "x", modified: Date(timeIntervalSince1970: 100))
        let new = Keychain.Item(account: "y", modified: Date(timeIntervalSince1970: 200))
        let undated = Keychain.Item(account: "z", modified: nil)
        XCTAssertEqual(new, Keychain.newest([old, new, undated]))
        XCTAssertEqual(new, Keychain.newest([undated, new, old]))
        XCTAssertNil(Keychain.newest([]))
    }

    /// Once the held token ages out, the item's date says whether Claude Code stored another. Until it does,
    /// the secret is not read again, so a prompt answered once is not raised again every poll.
    func testAnExpiredTokenIsReadAgainOnlyWhenTheItemChanges() async {
        var modified = Date(timeIntervalSince1970: 1_000)
        var reads = 0
        let transport = Answer(200)
        let provider = ClaudeProvider(profile: ClaudeProfile(slug: nil, directory: directory), transport: transport, archive: ReadingArchive(directory: directory), now: { self.clock },
                                      newestItem: { _ in Keychain.Item(account: "x", modified: modified) },
                                      credentialReader: { _, _ in
                                          reads += 1
                                          return ClaudeCredential(accessToken: "sk-\(reads)", expiresAt: self.clock.addingTimeInterval(-1), plan: "pro")
                                      })
        let first = await failure(provider)
        let second = await failure(provider)
        XCTAssertEqual(.credentialExpired, (first as? UsageError)?.kind)
        XCTAssertEqual(.credentialExpired, (second as? UsageError)?.kind)
        XCTAssertNotNil(provider.account())
        XCTAssertEqual(1, reads)

        modified = Date(timeIntervalSince1970: 2_000)
        _ = await failure(provider)
        XCTAssertEqual(2, reads)
        XCTAssertEqual([], transport.tokens)
    }

    /// Reading the same item again after the endpoint refused its token would only hand back that token.
    func testARejectedTokenIsNotReadAgainUntilTheItemChanges() async {
        var modified = Date(timeIntervalSince1970: 1_000)
        var reads = 0
        let transport = Answer(401)
        let provider = ClaudeProvider(profile: ClaudeProfile(slug: nil, directory: directory), transport: transport, archive: ReadingArchive(directory: directory), now: { self.clock },
                                      newestItem: { _ in Keychain.Item(account: "x", modified: modified) },
                                      credentialReader: { _, _ in
                                          reads += 1
                                          return ClaudeCredential(accessToken: "sk-\(reads)", expiresAt: .distantFuture, plan: "pro")
                                      })
        let first = await failure(provider)
        let second = await failure(provider)
        XCTAssertEqual(.needsSignIn, (first as? UsageError)?.kind)
        XCTAssertEqual(.needsSignIn, (second as? UsageError)?.kind)
        XCTAssertEqual(1, reads)
        XCTAssertEqual(["Bearer sk-1"], transport.tokens)

        modified = Date(timeIntervalSince1970: 2_000)
        _ = await failure(provider)
        XCTAssertEqual(2, reads)
        XCTAssertEqual(["Bearer sk-1", "Bearer sk-2"], transport.tokens)
    }

    func testARefusalIsHeldForAQuarterHourUnlessTheUserAsks() async {
        var attempts = 0
        let provider = ClaudeProvider(profile: ClaudeProfile(slug: nil, directory: directory), transport: Answer(200), archive: ReadingArchive(directory: directory), now: { self.clock },
                                      newestItem: { _ in Keychain.Item(account: "x", modified: Date(timeIntervalSince1970: 1_000)) },
                                      credentialReader: { _, _ in
                                          attempts += 1
                                          throw Keychain.Refused()
                                      })
        let refused = await failure(provider)
        XCTAssertTrue(refused is Keychain.Refused)
        XCTAssertNil(provider.account())
        _ = await failure(provider)
        XCTAssertEqual(1, attempts)

        clock += 15 * 60
        _ = await failure(provider)
        XCTAssertEqual(2, attempts)

        provider.forgetCredential()
        _ = await failure(provider)
        XCTAssertEqual(3, attempts)
    }
}

final class AntigravityKeychainTests: XCTestCase {
    private var clock = Date(timeIntervalSince1970: 1_790_000_000)

    /// A refusal is not a sign-out, and one "Deny" must not bring the same dialog back on every poll.
    func testARefusalIsHeldForAQuarterHourInReadAndAccountAlike() async {
        var attempts = 0
        let provider = AntigravityProvider(google: Answer(500), local: Answer(500),
                                           readCredential: {
                                               attempts += 1
                                               throw Keychain.Refused()
                                           },
                                           discover: { nil }, now: { self.clock })
        let refused = await failure(provider)
        XCTAssertTrue(refused is Keychain.Refused)
        XCTAssertNil(provider.account())
        _ = await failure(provider)
        XCTAssertEqual(1, attempts)

        clock += 15 * 60
        XCTAssertNil(provider.account())
        XCTAssertEqual(2, attempts)
    }
}
