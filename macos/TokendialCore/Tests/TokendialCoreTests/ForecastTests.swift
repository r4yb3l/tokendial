import XCTest
@testable import TokendialCore

private let t0 = Date(timeIntervalSince1970: 1_788_616_800) // 2026-09-08 14:00 UTC

private func at(_ minutes: Double) -> Date { t0.addingTimeInterval(minutes * 60) }

private func ramp(_ start: Double, _ perMinute: Double, _ minutes: Int, step: Int = 1, resetsAt: Date? = nil) -> [UsageSample] {
    stride(from: 0, through: minutes, by: step).map {
        UsageSample(takenAt: at(Double($0)), usedFraction: start + perMinute * Double($0), resetsAt: resetsAt)
    }
}

final class ForecastTests: XCTestCase {
    func testASteadyClimbRunsOutAtTheExpectedTime() throws {
        let reset = at(180)
        let forecast = try XCTUnwrap(Forecast.of(ramp(0.40, 0.01, 20, resetsAt: reset), now: at(20), resetsAt: reset))
        XCTAssertEqual(forecast.kind, .runsOut)
        let runsOut = try XCTUnwrap(forecast.runsOutAt)
        XCTAssertEqual(runsOut.timeIntervalSince(at(60)), 0, accuracy: 30)
    }

    func testASlowClimbLastsUntilTheReset() {
        let reset = at(40)
        let forecast = Forecast.of(ramp(0.40, 0.01, 20, resetsAt: reset), now: at(20), resetsAt: reset)
        XCTAssertEqual(forecast?.kind, .lastsUntilReset)
        XCTAssertNil(forecast?.runsOutAt)
    }

    func testAFlatWindowHasNoForecast() {
        XCTAssertNil(Forecast.of(ramp(0.40, 0, 30, step: 5), now: at(30), resetsAt: at(120)))
    }

    func testTooFewOrTooCloseSamplesHaveNoForecast() {
        XCTAssertNil(Forecast.of(ramp(0.40, 0.01, 2), now: at(2), resetsAt: at(120)))
        XCTAssertNil(Forecast.of(ramp(0.40, 0.01, 8), now: at(8), resetsAt: at(120)))
    }

    func testOldSamplesAreIgnored() {
        XCTAssertNil(Forecast.of(ramp(0.10, 0.01, 30), now: at(91), resetsAt: at(180)))
    }

    func testAFullWindowHasNoForecast() {
        XCTAssertNil(Forecast.of(ramp(0.80, 0.01, 20), now: at(20), resetsAt: at(180)))
    }

    func testWithoutAResetOnlyTheNextDayCounts() {
        XCTAssertEqual(Forecast.of(ramp(0.40, 0.01, 20), now: at(20), resetsAt: nil)?.kind, .runsOut)
        XCTAssertNil(Forecast.of(ramp(0.40, 0.0001, 20), now: at(20), resetsAt: nil))
    }

    /// Vendors publish whole percentages, so neighbouring samples read flat; the fit still finds the pace.
    func testIntegerPercentagesStillGiveASlope() {
        let samples = (0...20).map { m in
            UsageSample(takenAt: at(Double(m)), usedFraction: ((0.40 + 0.01 * Double(m)) * 100).rounded(.down) / 100, resetsAt: nil)
        }
        let slope = Forecast.slopePerHour(samples)
        XCTAssertGreaterThan(slope, 0.55)
        XCTAssertLessThan(slope, 0.65)
    }
}

final class UsageHistoryTests: XCTestCase {
    func testKeepsSamplesOfOneEpochAndTrimsByAgeAndCount() {
        let history = UsageHistory()
        for m in 0..<400 {
            history.add("claude", "session", UsageSample(takenAt: at(Double(m)), usedFraction: min(0.99, Double(m) / 500), resetsAt: at(480)))
        }
        let samples = history.samples("claude", "session")
        XCTAssertLessThanOrEqual(samples.count, UsageHistory.maxSamples)
        XCTAssertTrue(samples.allSatisfy { at(399).timeIntervalSince($0.takenAt) <= UsageHistory.maxAge })
    }

    func testAMovedResetStartsANewEpoch() {
        let history = UsageHistory()
        history.add("claude", "session", UsageSample(takenAt: t0, usedFraction: 0.9, resetsAt: at(60)))
        history.add("claude", "session", UsageSample(takenAt: at(1), usedFraction: 0.95, resetsAt: at(60).addingTimeInterval(30)))
        XCTAssertEqual(history.samples("claude", "session").count, 2)
        history.add("claude", "session", UsageSample(takenAt: at(2), usedFraction: 0.02, resetsAt: at(360)))
        XCTAssertEqual(history.samples("claude", "session").count, 1)
    }

    func testADropWithoutAResetStartsANewEpoch() {
        let history = UsageHistory()
        history.add("antigravity", "requests", UsageSample(takenAt: t0, usedFraction: 0.6, resetsAt: nil))
        history.add("antigravity", "requests", UsageSample(takenAt: at(1), usedFraction: 0.55, resetsAt: nil))
        XCTAssertEqual(history.samples("antigravity", "requests").count, 2)
        history.add("antigravity", "requests", UsageSample(takenAt: at(2), usedFraction: 0.1, resetsAt: nil))
        XCTAssertEqual(history.samples("antigravity", "requests").count, 1)
    }

    func testRecordsOnlyWindowsWithAFraction() {
        let history = UsageHistory()
        let reading = ProviderReading(providerId: "codex", displayName: "Codex", fidelity: .official, status: .live,
                                      windows: [UsageWindow(id: "primary", label: "5h limit", usedFraction: 0.3, resetsAt: at(120)),
                                                UsageWindow(id: "count", label: "Requests", count: 7)])
        history.record(reading, now: t0)
        XCTAssertEqual(history.samples("codex", "primary").count, 1)
        XCTAssertTrue(history.samples("codex", "count").isEmpty)
        history.forget("codex")
        XCTAssertTrue(history.samples("codex", "primary").isEmpty)
    }
}
