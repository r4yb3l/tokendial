import Foundation
import TokendialCore

enum PanelMode: String, Codable, CaseIterable {
    case expandOnHover
    case alwaysExpanded
    case hidden
}

enum AlertDelivery: String, Codable, CaseIterable {
    case tokendialBanners
    case systemNotifications
    case systemThenBanners
}

/// Everything the user can change, one JSON file under Application Support.
struct Settings: Codable {
    var schema = 1
    var panel: PanelMode = .expandOnHover
    var disconnected: Set<String> = []
    /// Provider ids this install has seen. A provider added by an update joins connected only when its tool is signed in.
    var known: Set<String> = []
    var launchAtLogin = false
    var firstRunDone = false
    var thresholds: [Int] = [50, 80, 95]
    var alertThresholds = true
    var alertResetSoon = true
    var alertWaiting = true
    var alertLimit = true
    var delivery: AlertDelivery = .tokendialBanners
    var resetLeadMinutes = 10
    var waitingDebounceSeconds = 20
    var lastSeenVersion: String?

    var alertConfig: AlertConfig {
        var config = AlertConfig.default
        config.thresholds = Array(Set(thresholds.filter { (1...100).contains($0) })).sorted()
        config.resetLead = TimeInterval(min(120, max(1, resetLeadMinutes)) * 60)
        config.waitingDebounce = TimeInterval(min(600, max(5, waitingDebounceSeconds)))
        return config
    }

    func wants(_ kind: AlertKind) -> Bool {
        switch kind {
        case .threshold: return alertThresholds
        case .resetSoon, .resetDone: return alertResetSoon
        case .waiting: return alertWaiting
        case .limit: return alertLimit
        }
    }

    static var defaultFile: URL { Paths.appSupport.appendingPathComponent("settings.json") }

    static func load(_ file: URL = defaultFile) -> Settings {
        guard let data = try? Data(contentsOf: file), let loaded = try? JSONDecoder().decode(Settings.self, from: data) else { return Settings() }
        return loaded
    }

    func save(_ file: URL = defaultFile) {
        do {
            try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            try encoder.encode(self).write(to: file, options: .atomic)
        } catch { Log.ui.error("settings: \(error.localizedDescription)") }
    }
}
