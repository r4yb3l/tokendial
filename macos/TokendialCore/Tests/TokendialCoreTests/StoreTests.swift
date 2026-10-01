import Foundation
import XCTest
@testable import TokendialCore

final class ReadingArchiveTests: XCTestCase {
    private final class Unanswered: UsageProvider {
        let id = "claude"
        let displayName = "Claude Code"
        let signIn = SignInRoute.guidance("signin.claude")
        func read() async throws -> ProviderReading { throw UsageError.needsSignIn() }
        func account() -> ProviderAccount? { nil }
    }

    private var directory: URL!
    private let takenAt = Date(timeIntervalSince1970: 1_790_000_000)

    override func setUpWithError() throws {
        directory = FileManager.default.temporaryDirectory.appendingPathComponent("tokendial-archive-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let reading = ProviderReading(providerId: "claude", displayName: "Claude Code", fidelity: .official, status: .live,
                                      windows: [UsageWindow(id: "session", label: "Current session", usedFraction: 0.92)], headlineId: "session")
        ReadingArchive(directory: directory).save(["claude": Remembered(reading: reading, takenAt: takenAt)])
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: directory)
    }

    func testAReadingSavedLiveComesBackStaleSinceItWasTaken() {
        let loaded = ReadingArchive(directory: directory).load()["claude"]
        XCTAssertEqual(.stale(since: takenAt), loaded?.reading.status)
        XCTAssertEqual(takenAt, loaded?.takenAt)
        XCTAssertEqual(0.92, loaded?.reading.headlineFraction)
    }

    /// The first change after launch fires before any provider has answered. What it carries must not read
    /// as live, or the alert engine takes days-old numbers for news.
    func testTheStoreOpensOnTheArchiveWithNothingLive() {
        let store = UsageStore(providers: [Unanswered()], archive: ReadingArchive(directory: directory))
        XCTAssertEqual([.stale(since: takenAt)], store.readings.map(\.status))
    }
}
