import Foundation

/// The shared string catalogue in docs/i18n. English is the base; a language file overrides what it
/// translates. Plurals follow CLDR categories, dates and times follow the locale. Both platforms load the same files.
public final class Strings {
    public static let languages: [(code: String, name: String)] = [
        ("en", "English"), ("en-GB", "English (UK)"), ("es", "Español"), ("fr", "Français"), ("de", "Deutsch"), ("ar", "العربية")
    ]

    public static var directory: URL?
    private static let lock = NSLock()
    private static var base = Catalog.load("en")
    private static var current = base
    private static var localeValue = Locale.current
    public static var changed: (() -> Void)?

    public static var language: String { current.language }
    public static var locale: Locale { localeValue }
    public static var rightToLeft: Bool { current.direction == "rtl" }

    /// Point the loader at the directory holding the JSON files (the app bundle's copy of docs/i18n) and reload.
    public static func load(from directory: URL, language: String? = nil) {
        self.directory = directory
        base = Catalog.load("en")
        use(language)
    }

    /// Switch language. Nil follows the system; an unknown code falls back to English.
    public static func use(_ language: String?) {
        let code = resolve(language ?? Locale.preferredLanguages.first ?? "en")
        lock.lock()
        current = code == "en" ? base : Catalog.load(code)
        localeValue = Locale(identifier: code == "en" ? "en_US" : code == "ar" ? "ar_SA" : code.replacingOccurrences(of: "-", with: "_"))
        lock.unlock()
        changed?()
    }

    public static func resolve(_ name: String) -> String {
        if let exact = languages.first(where: { $0.code.lowercased() == name.lowercased() }) { return exact.code }
        let two = String(name.split(separator: "-").first ?? "").lowercased()
        return languages.contains { $0.code == two } ? two : "en"
    }

    public static func t(_ key: String, _ args: [String: Any] = [:]) -> String {
        fill(lookup(key) ?? key, args)
    }

    /// A plural form for n, chosen by the language's CLDR rules; {n} is filled with the number.
    public static func plural(_ key: String, _ n: Int, _ args: [String: Any] = [:]) -> String {
        var merged = args
        merged["n"] = n
        guard let forms = lookupForms(key) else { return t(key, merged) }
        let category = pluralCategory(current.language, n)
        return fill(forms[category] ?? forms["other"] ?? key, merged)
    }

    /// A provider-produced English window label in the current language; unknown labels pass through.
    public static func label(_ english: String) -> String {
        if let exact = current.labels[english] ?? base.labels[english] { return exact }
        for (pattern, key) in labelPatterns {
            guard let match = pattern.firstMatch(in: english, range: NSRange(english.startIndex..., in: english)),
                  let range = Range(match.range(at: 1), in: english),
                  let template = current.labels[key] ?? base.labels[key] else { continue }
            return template.replacingOccurrences(of: "{n}", with: String(english[range]))
        }
        return english
    }

    private static let labelPatterns: [(NSRegularExpression, String)] = [
        (try! NSRegularExpression(pattern: "^(\\d+)h limit$"), "{n}h limit"),
        (try! NSRegularExpression(pattern: "^(\\d+)m limit$"), "{n}m limit"),
        (try! NSRegularExpression(pattern: "^(\\d+)d limit$"), "{n}d limit"),
        (try! NSRegularExpression(pattern: "^Usage \\((\\d+) h\\)$"), "Usage ({n} h)"),
        (try! NSRegularExpression(pattern: "^Usage \\((\\d+) wk\\)$"), "Usage ({n} wk)")
    ]

    public static func pluralCategory(_ language: String, _ n: Int) -> String {
        switch String(language.split(separator: "-").first ?? "") {
        case "ar":
            if n == 0 { return "zero" }
            if n == 1 { return "one" }
            if n == 2 { return "two" }
            let mod = n % 100
            if (3...10).contains(mod) { return "few" }
            if (11...99).contains(mod) { return "many" }
            return "other"
        case "fr": return n == 0 || n == 1 ? "one" : "other"
        default: return n == 1 ? "one" : "other"
        }
    }

    private static func lookup(_ key: String) -> String? {
        if let s = current.strings[key] as? String { return s }
        return base.strings[key] as? String
    }

    private static func lookupForms(_ key: String) -> [String: String]? {
        (current.strings[key] as? [String: String]) ?? (base.strings[key] as? [String: String])
    }

    private static func fill(_ text: String, _ args: [String: Any]) -> String {
        var out = text
        for (name, value) in args { out = out.replacingOccurrences(of: "{\(name)}", with: "\(value)") }
        return out
    }

    public static func keys(_ language: String) -> [String: Any] { Catalog.load(language).strings }
    public static func labels(_ language: String) -> [String: String] { Catalog.load(language).labels }

    private struct Catalog {
        var language = "en"
        var direction = "ltr"
        var strings: [String: Any] = [:]
        var labels: [String: String] = [:]

        static func load(_ code: String) -> Catalog {
            guard let directory = Strings.directory, let data = try? Data(contentsOf: directory.appendingPathComponent("\(code).json")), let root = JSON.object(data) else {
                return code == "en" ? Catalog() : load("en")
            }
            var catalog = Catalog(language: root.str("language") ?? code, direction: root.str("direction") ?? "ltr")
            catalog.strings = root.obj("strings") ?? [:]
            catalog.labels = (root.obj("labels") ?? [:]).compactMapValues { $0 as? String }
            return catalog
        }
    }
}
