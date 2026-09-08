import Foundation
import XCTest
@testable import TokendialCore

/// Runs every conformance vector in docs/alerts/vectors. The C# core runs the same files; the two engines must emit identical sequences.
final class AlertVectorTests: XCTestCase {
    private let base = Date(timeIntervalSince1970: 1_767_225_600)

    func testEveryVectorPasses() throws {
        let files = try Docs.files(Docs.path("alerts", "vectors"))
        XCTAssertGreaterThan(files.count, 10)
        for file in files {
            try run(file)
        }
    }

    private func run(_ file: URL) throws {
        let root = try XCTUnwrap(JSON.object(Data(contentsOf: file)), file.lastPathComponent)
        let config = Self.config(root.obj("config") ?? [:])
        var engine = AlertEngine(config: config)
        var emitted: [String] = []

        for step in root.objects("steps") {
            let t = try XCTUnwrap(step.num("t"))
            let at = base.addingTimeInterval(t)
            let e = try XCTUnwrap(step.obj("event"))
            let event: AlertEvent
            switch e.str("kind") {
            case "usage":
                event = .usage(at: at, provider: e.str("provider")!, window: e.str("window")!, usedPct: e.num("usedPct")!, resetsAt: e.num("resetsAt").map { base.addingTimeInterval($0) }, limited: e.bool("limited") ?? false)
            case "session":
                event = .session(at: at, provider: e.str("provider")!, sessionId: e.str("sessionId")!, state: SessionActivity(rawValue: e.str("state")!.lowercased())!)
            case "hover":
                event = .hover(at: at, on: e.bool("on") ?? false)
            case "tick":
                event = .tick(at: at)
            case "restart":
                event = .restart(at: at)
            default:
                XCTFail("unknown event in \(file.lastPathComponent)"); return
            }
            if step.bool("persistAndRestart") == true {
                engine = AlertEngine.load(engine.save(), config: config)
            }
            for alert in engine.reduce(event) {
                emitted.append(Self.describe(t, alert))
            }
        }

        let expected = root.objects("expected").map { e -> String in
            let pct = e.num("pct").map { String(Int($0)) } ?? ""
            return "\(Self.number(e.num("t")!)) \(e.str("kind")!) \(e.str("provider")!) \(e.str("window") ?? e.str("sessionId") ?? "") \(pct)".trimmingCharacters(in: .whitespaces)
        }
        XCTAssertEqual(expected, emitted, file.lastPathComponent)
    }

    private static func describe(_ t: Double, _ alert: Alert) -> String {
        "\(number(t)) \(alert.kind.rawValue) \(alert.provider) \(alert.window ?? alert.sessionId ?? "") \(alert.pct.map(String.init) ?? "")".trimmingCharacters(in: .whitespaces)
    }

    private static func number(_ value: Double) -> String {
        value == value.rounded() ? String(Int(value)) : String(value)
    }

    private static func config(_ element: JSONObject) -> AlertConfig {
        let d = AlertConfig.default
        func seconds(_ name: String, _ fallback: TimeInterval) -> TimeInterval { element.num(name) ?? fallback }
        let thresholds = element.arr("thresholds").compactMap { ($0 as? NSNumber)?.intValue }
        return AlertConfig(
            thresholds: thresholds.isEmpty ? d.thresholds : thresholds,
            resetLead: seconds("resetLeadSeconds", d.resetLead),
            resetLeadMinPct: Int(element.num("resetLeadMinPct") ?? Double(d.resetLeadMinPct)),
            waitingDebounce: seconds("waitingDebounceSeconds", d.waitingDebounce),
            waitingRepeat: seconds("waitingRepeatSeconds", d.waitingRepeat),
            perProviderCooldown: seconds("perProviderCooldownSeconds", d.perProviderCooldown))
    }
}
