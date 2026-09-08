import Foundation

public struct Remembered: Codable, Sendable {
    public var reading: ProviderReading
    public var takenAt: Date
}

/// The last good reading per provider and any rate-limit hold, on disk so the panel has numbers at launch.
/// Never holds a credential.
public final class ReadingArchive {
    public let directory: URL
    private let now: () -> Date
    private let lock = NSLock()

    public init(directory: URL? = nil, now: @escaping () -> Date = Date.init) {
        self.directory = directory ?? Paths.appSupport
        self.now = now
    }

    private var readingsFile: URL { directory.appendingPathComponent("readings.json") }
    private var backoffFile: URL { directory.appendingPathComponent("backoff.json") }

    private static let decoder: JSONDecoder = { let d = JSONDecoder(); d.dateDecodingStrategy = .iso8601; return d }()
    private static let encoder: JSONEncoder = { let e = JSONEncoder(); e.dateEncodingStrategy = .iso8601; e.outputFormatting = [.prettyPrinted, .sortedKeys]; return e }()

    public func load() -> [String: Remembered] {
        lock.lock(); defer { lock.unlock() }
        guard let data = try? Data(contentsOf: readingsFile), let list = try? Self.decoder.decode([Remembered].self, from: data) else { return [:] }
        return Dictionary(list.map { ($0.reading.providerId, $0) }, uniquingKeysWith: { a, _ in a })
    }

    public func save(_ readings: [String: Remembered]) {
        lock.lock(); defer { lock.unlock() }
        let list = readings.values.sorted { $0.reading.providerId < $1.reading.providerId }
        write(list, to: readingsFile)
    }

    public func forget(_ providerId: String) {
        var current = load()
        current.removeValue(forKey: providerId)
        save(current)
    }

    public func backoffUntil(_ providerId: String) -> Date? {
        lock.lock(); defer { lock.unlock() }
        return loadBackoff()[providerId]
    }

    public func setBackoff(_ providerId: String, until: Date?) {
        lock.lock(); defer { lock.unlock() }
        var all = loadBackoff()
        if let until, until > now() { all[providerId] = until } else { all.removeValue(forKey: providerId) }
        write(all, to: backoffFile)
    }

    private func loadBackoff() -> [String: Date] {
        guard let data = try? Data(contentsOf: backoffFile), let all = try? Self.decoder.decode([String: Date].self, from: data) else { return [:] }
        return all
    }

    private func write<T: Encodable>(_ value: T, to file: URL) {
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let data = try Self.encoder.encode(value)
            let temporary = file.appendingPathExtension("tmp")
            try data.write(to: temporary, options: .atomic)
            _ = try FileManager.default.replaceItemAt(file, withItemAt: temporary)
        } catch {
            Log.usage.error("archive \(file.lastPathComponent): \(error.localizedDescription)")
        }
    }
}

/// Text log sink the app redirects to a file. Debug lines only when asked for.
public enum Log {
    public enum Level: String { case debug, info, warn, error }
    public static var sink: (Level, String, String) -> Void = { level, area, message in
        NSLog("[%@] %@: %@", level.rawValue, area, message)
    }
    public static let usage = Area("usage")
    public static let sessions = Area("sessions")
    public static let alerts = Area("alerts")
    public static let ui = Area("ui")

    public struct Area {
        let name: String
        init(_ name: String) { self.name = name }
        public func debug(_ message: String) { Log.sink(.debug, name, message) }
        public func info(_ message: String) { Log.sink(.info, name, message) }
        public func warn(_ message: String) { Log.sink(.warn, name, message) }
        public func error(_ message: String) { Log.sink(.error, name, message) }
    }
}
