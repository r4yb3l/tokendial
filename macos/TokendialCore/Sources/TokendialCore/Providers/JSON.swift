import Foundation

public typealias JSONObject = [String: Any]

/// Lenient reads over JSONSerialization output: a missing or mistyped field is nil, never a crash.
public enum JSON {
    public static func parse(_ data: Data) -> Any? {
        try? JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed])
    }

    public static func parse(_ text: String) -> Any? {
        parse(Data(text.utf8))
    }

    public static func object(_ data: Data) -> JSONObject? {
        parse(data) as? JSONObject
    }

    private static let fractional: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    private static let plain: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return f
    }()

    private static let dateOnly: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(identifier: "UTC")
        f.dateFormat = "yyyy-MM-dd'T'HH:mm:ss"
        return f
    }()

    public static func iso(_ text: String?) -> Date? {
        guard let text, !text.isEmpty else { return nil }
        return fractional.date(from: text) ?? plain.date(from: text) ?? dateOnly.date(from: text)
    }

    /// A credential file another tool owns and rewrites whenever it refreshes the token, or nil when there is no file.
    /// One that is there but cannot be read now - locked mid-rewrite, or a read we were refused - is transient, and
    /// never the sign-out that would erase the last reading and send a signed-in user to sign in again.
    public static func credentialData(_ file: URL) throws -> Data? {
        guard FileManager.default.fileExists(atPath: file.path) else { return nil }
        guard let data = try? Data(contentsOf: file) else { throw UsageError.credentialExpired() }
        return data
    }

    /// The credential file as JSON. No file is a sign-in; one that is empty or not yet whole JSON is the owner caught
    /// mid-rewrite, transient for the same reason as an unreadable one.
    public static func credentialFile(_ file: URL) throws -> JSONObject {
        guard let data = try credentialData(file) else { throw UsageError.needsSignIn() }
        guard let root = object(data) else { throw UsageError.credentialExpired() }
        return root
    }
}

public extension Dictionary where Key == String, Value == Any {
    func obj(_ name: String) -> JSONObject? { self[name] as? JSONObject }
    func arr(_ name: String) -> [Any] { self[name] as? [Any] ?? [] }
    func objects(_ name: String) -> [JSONObject] { arr(name).compactMap { $0 as? JSONObject } }

    func str(_ name: String) -> String? {
        guard let s = self[name] as? String, !s.isEmpty else { return nil }
        return s
    }

    /// A finite number, or a string holding one: Double("nan") and Double("inf") parse, and trap later in Int(...).
    func num(_ name: String) -> Double? {
        let parsed: Double?
        switch self[name] {
        case let n as NSNumber where CFGetTypeID(n) != CFBooleanGetTypeID(): parsed = n.doubleValue
        case let s as String: parsed = Double(s)
        default: parsed = nil
        }
        return parsed.flatMap { $0.isFinite ? $0 : nil }
    }

    func bool(_ name: String) -> Bool? {
        guard let n = self[name] as? NSNumber, CFGetTypeID(n) == CFBooleanGetTypeID() else { return nil }
        return n.boolValue
    }

    func date(_ name: String) -> Date? { JSON.iso(str(name)) }
    func epochMillis(_ name: String) -> Date? { num(name).map { Date(timeIntervalSince1970: $0 / 1000) } }
    func epochSeconds(_ name: String) -> Date? { num(name).map { Date(timeIntervalSince1970: $0) } }
}
