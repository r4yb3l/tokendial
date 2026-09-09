import Foundation
import TokendialCore

enum PanelMode: String, Codable, CaseIterable {
    case expandOnHover
    case alwaysExpanded
    case hidden
}

/// Which look the app wears: the system's, or one the user pinned.
enum Appearance: String, Codable, CaseIterable {
    case system
    case dark
    case light
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
    var appearance: Appearance = .dark
    /// A code from Strings.languages, or nil to follow the system.
    var language: String?

    init() {}

    private enum CodingKeys: String, CodingKey {
        case schema, panel, disconnected, known, launchAtLogin, firstRunDone, thresholds
        case alertThresholds, alertResetSoon, alertWaiting, alertLimit, delivery
        case resetLeadMinutes, waitingDebounceSeconds, lastSeenVersion, appearance, language
    }

    /// Every key is optional on the way in, so a file written by an older build keeps its values and the
    /// fields that build did not know about fall back to their defaults instead of resetting everything.
    init(from decoder: Decoder) throws {
        let box = try decoder.container(keyedBy: CodingKeys.self)
        self.init()
        schema = try box.decodeIfPresent(Int.self, forKey: .schema) ?? schema
        panel = try box.decodeIfPresent(PanelMode.self, forKey: .panel) ?? panel
        disconnected = try box.decodeIfPresent(Set<String>.self, forKey: .disconnected) ?? disconnected
        known = try box.decodeIfPresent(Set<String>.self, forKey: .known) ?? known
        launchAtLogin = try box.decodeIfPresent(Bool.self, forKey: .launchAtLogin) ?? launchAtLogin
        firstRunDone = try box.decodeIfPresent(Bool.self, forKey: .firstRunDone) ?? firstRunDone
        thresholds = try box.decodeIfPresent([Int].self, forKey: .thresholds) ?? thresholds
        alertThresholds = try box.decodeIfPresent(Bool.self, forKey: .alertThresholds) ?? alertThresholds
        alertResetSoon = try box.decodeIfPresent(Bool.self, forKey: .alertResetSoon) ?? alertResetSoon
        alertWaiting = try box.decodeIfPresent(Bool.self, forKey: .alertWaiting) ?? alertWaiting
        alertLimit = try box.decodeIfPresent(Bool.self, forKey: .alertLimit) ?? alertLimit
        delivery = try box.decodeIfPresent(AlertDelivery.self, forKey: .delivery) ?? delivery
        resetLeadMinutes = try box.decodeIfPresent(Int.self, forKey: .resetLeadMinutes) ?? resetLeadMinutes
        waitingDebounceSeconds = try box.decodeIfPresent(Int.self, forKey: .waitingDebounceSeconds) ?? waitingDebounceSeconds
        lastSeenVersion = try box.decodeIfPresent(String.self, forKey: .lastSeenVersion)
        appearance = try box.decodeIfPresent(Appearance.self, forKey: .appearance) ?? appearance
        language = try box.decodeIfPresent(String.self, forKey: .language)
    }

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
