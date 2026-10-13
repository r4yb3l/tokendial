import XCTest
@testable import TokendialCore

/// What the floating face shows, and how "take me there" finds the app a session lives in.
final class FaceTests: XCTestCase {
    private func reading(_ id: String, _ used: Double, block: Blocked? = nil, status: ReadingStatus = .live) -> ProviderReading {
        ProviderReading(providerId: id, displayName: id, fidelity: .official, status: status, windows: [UsageWindow(id: "w", label: "W", usedFraction: used)], block: block)
    }

    private func session(_ id: String, _ state: SessionState) -> AgentSession {
        AgentSession(id: id, name: id, location: "Terminal · utils", state: state, waitingFor: nil, since: Date(timeIntervalSince1970: 0))
    }

    func testColourAndFillComeFromTheSameProvider() {
        let face = Face.of([reading("a", 0.10, block: Blocked(reason: "rate limited", until: nil)), reading("b", 0.70)], sessions: [])
        XCTAssertEqual(face.band, .critical)
        XCTAssertEqual(face.fraction, 1)
        XCTAssertTrue(face.spent)
    }

    func testWithinOneBandTheFullerProviderWins() {
        let face = Face.of([reading("a", 0.55), reading("b", 0.75), reading("c", 0.20)], sessions: [])
        XCTAssertEqual(face.band, .watch)
        XCTAssertEqual(face.fraction, 0.75)
    }

    func testProvidersThatCannotBeReadAreLeftOut() {
        let face = Face.of([reading("a", 0.95, status: .needsSignIn), reading("b", 0.30)], sessions: [])
        XCTAssertEqual(face.band, .ample)
        XCTAssertEqual(face.fraction, 0.30)
    }

    func testNothingToReadIsAnEmptyArcAndAContentFace() {
        let face = Face.of([], sessions: [])
        XCTAssertEqual(face, .empty)
        XCTAssertEqual(face.eyes, "^ ^")
        XCTAssertEqual(face.mouth, "◡")
    }

    func testWaitingOutranksWorking() {
        let face = Face.of([reading("a", 0.2)], sessions: [session("x", .working), session("y", .waiting)])
        XCTAssertEqual(face.mood, .waiting)
        XCTAssertEqual(face.eyes, "O O")
        XCTAssertEqual(face.mouth, "o")
    }

    func testTheGlyphTable() {
        let rows: [(Double, SessionState?, String, String)] = [
            (0.20, nil, "^ ^", "◡"),
            (0.60, nil, "- -", "_"),
            (0.90, nil, "> <", "~"),
            (1.00, .waiting, "x x", "_"),
            (0.20, .working, "o o", "◡")
        ]
        for (used, mood, eyes, mouth) in rows {
            let face = Face(band: Band.of(used), fraction: used, mood: mood)
            XCTAssertEqual(face.eyes, eyes, "\(used) \(String(describing: mood))")
            XCTAssertEqual(face.mouth, mouth, "\(used) \(String(describing: mood))")
        }
    }

    func testTheWalkStopsAtTheFirstProcessWithAnApp() {
        let parents: [Int32: Int32] = [30: 20, 20: 10, 10: 1]
        let found = Ancestry.firstAnswer(30, parentOf: { parents[$0] }, pick: { $0 == 20 || $0 == 10 ? $0 * 10 : nil })
        XCTAssertEqual(found, 200)
    }

    func testARecycledParentIdCannotLoopForever() {
        let loop: [Int32: Int32] = [30: 20, 20: 30]
        XCTAssertNil(Ancestry.firstAnswer(30, parentOf: { loop[$0] }, pick: { _ -> Int32? in nil }))
    }
}
