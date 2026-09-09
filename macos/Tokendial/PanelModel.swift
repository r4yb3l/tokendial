import Foundation
import TokendialCore

/// One provider as the panel shows it.
struct Tile {
    let id: String
    let name: String
    let mark: String
    let reading: ProviderReading
    let account: ProviderAccount?
    let activity: Activity?
    let inFlight: Bool
    let forecast: Forecast?

    var headline: UsageWindow? { reading.headline }
    var fraction: Double? { reading.headlineFraction }
    var band: Band? { reading.band }
    var hasReading: Bool {
        guard reading.hasReading else { return false }
        switch reading.status { case .needsSignIn, .unsupported: return false; default: return true }
    }
    var secondary: [UsageWindow] { reading.windows.filter { $0.id != headline?.id } }
    var headlineLabel: String { headline.map { Strings.label($0.label) } ?? statusLabel }

    var statusLabel: String {
        switch reading.status {
        case .needsSignIn: return Strings.t("status.signIn")
        case .unsupported: return Strings.t("status.nothingMetered")
        case .failed: return Strings.t("status.unavailable")
        case .stale where !reading.hasReading: return Strings.t(inFlight ? "status.reading" : "status.noReadingYet")
        default: return ""
        }
    }

    static func mark(for id: String) -> String {
        if id.hasPrefix("claude") { return "CL" }
        switch id {
        case "codex": return "CX"
        case "copilot": return "CP"
        case "cursor": return "CU"
        case "antigravity": return "AG"
        case "glm": return "GL"
        case "grok": return "GK"
        case "opencode": return "OC"
        default: return String(id.prefix(2)).uppercased()
        }
    }
}

/// A snapshot the panel renders.
struct PanelModel {
    let tiles: [Tile]
    let sessions: [AgentSession]
    let muted: Int

    static let empty = PanelModel(tiles: [], sessions: [], muted: 0)

    static func build(readings: [ProviderReading], summaries: [ProviderSummary], activities: [String: Activity], inFlight: Set<String>, muted: Int, forecasts: [String: Forecast] = [:]) -> PanelModel {
        let tiles = readings.map { r in
            Tile(id: r.providerId, name: r.displayName, mark: Tile.mark(for: r.providerId), reading: r, account: summaries.first { $0.id == r.providerId }?.account, activity: activities[r.providerId], inFlight: inFlight.contains(r.providerId), forecast: forecasts[r.providerId])
        }
        func rank(_ s: SessionState) -> Int { switch s { case .waiting: return 0; case .working: return 1; case .idle: return 2 } }
        let sessions = activities.values.flatMap { $0.ordered }.sorted { a, b in rank(a.state) != rank(b.state) ? rank(a.state) < rank(b.state) : a.since > b.since }
        return PanelModel(tiles: tiles, sessions: sessions, muted: muted)
    }

    var anyWaiting: Bool { sessions.contains { $0.state == .waiting } }

    static func sessionsCopy(_ sessions: [AgentSession], now: Date) -> String {
        if sessions.isEmpty { return Strings.t("panel.noAgents") }
        let waiting = sessions.filter { $0.state == .waiting }
        if let first = waiting.first {
            let rest = waiting.count > 1 ? Strings.plural("panel.waitingMore", waiting.count - 1) : ""
            return Strings.t("panel.waiting", ["name": first.name, "elapsed": Copy.elapsed(first.since, now: now)]) + rest
        }
        let working = sessions.filter { $0.state == .working }
        if working.count == 1 { return Strings.t("panel.workingOne", ["name": working[0].name, "elapsed": Copy.elapsed(working[0].since, now: now)]) }
        if working.count > 1 { return Strings.t("panel.workingMany", ["n": working.count, "names": working.prefix(3).map { $0.name }.joined(separator: ", ")]) }
        return Strings.plural("panel.idle", sessions.count)
    }
}
