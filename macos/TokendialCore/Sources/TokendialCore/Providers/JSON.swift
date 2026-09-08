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
}

public extension Dictionary where Key == String, Value == Any {
    func obj(_ name: String) -> JSONObject? { self[name] as? JSONObject }
    func arr(_ name: String) -> [Any] { self[name] as? [Any] ?? [] }
    func objects(_ name: String) -> [JSONObject] { arr(name).compactMap { $0 as? JSONObject } }

    func str(_ name: String) -> String? {
        guard let s = self[name] as? String, !s.isEmpty else { return nil }
        return s
    }

    func num(_ name: String) -> Double? {
        switch self[name] {
        case let n as NSNumber where CFGetTypeID(n) != CFBooleanGetTypeID(): return n.doubleValue
        case let s as String: return Double(s)
        default: return nil
        }
    }

    func bool(_ name: String) -> Bool? {
        guard let n = self[name] as? NSNumber, CFGetTypeID(n) == CFBooleanGetTypeID() else { return nil }
        return n.boolValue
    }

    func date(_ name: String) -> Date? { JSON.iso(str(name)) }
    func epochMillis(_ name: String) -> Date? { num(name).map { Date(timeIntervalSince1970: $0 / 1000) } }
    func epochSeconds(_ name: String) -> Date? { num(name).map { Date(timeIntervalSince1970: $0) } }
}
