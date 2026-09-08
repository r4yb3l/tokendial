import Foundation
import XCTest
@testable import TokendialCore

/// Every language carries every English key with the same placeholders; copy rules hold in each.
final class I18nTests: XCTestCase {
    override func setUpWithError() throws {
        Strings.load(from: try Docs.path("i18n"), language: "en")
    }

    override func tearDown() { Strings.use("en") }

    func testCataloguesCoverEveryEnglishKey() throws {
        let english = Strings.keys("en")
        for language in Strings.languages.map({ $0.code }) where language != "en" && language != "en-GB" {
            let other = Strings.keys(language)
            let missing = english.keys.filter { other[$0] == nil }.sorted()
            XCTAssertTrue(missing.isEmpty, "\(language) lacks: \(missing.joined(separator: ", "))")
            for (key, value) in english {
                XCTAssertEqual(placeholders(value), placeholders(other[key] ?? ""), "\(language).\(key)")
            }
            let labels = Strings.labels(language)
            XCTAssertTrue(Strings.labels("en").keys.allSatisfy { labels[$0] != nil }, "\(language) lacks labels")
        }
    }

    private func placeholders(_ value: Any) -> Set<String> {
        let text: String
        if let s = value as? String { text = s } else if let forms = value as? [String: String] { text = forms.values.joined(separator: " ") } else { text = "" }
        let regex = try! NSRegularExpression(pattern: "\\{(\\w+)\\}")
        var set = Set(regex.matches(in: text, range: NSRange(text.startIndex..., in: text)).compactMap { Range($0.range(at: 1), in: text).map { String(text[$0]) } })
        set.remove("n")
        return set
    }

    func testSpanishCopyFollowsTheRules() {
        Strings.use("es")
        let now = Date(timeIntervalSince1970: 1_700_000_000)
        XCTAssertEqual("Se reinicia en 51 min", Copy.reset(now.addingTimeInterval(51 * 60), now: now))
        XCTAssertEqual("Se reinicia 21 nov", Copy.reset(now.addingTimeInterval(7 * 86400), now: now, zone: TimeZone(identifier: "UTC")!))
        XCTAssertEqual("hace 6 min", Copy.ago(now.addingTimeInterval(-6 * 60), now: now))
        XCTAssertEqual("Sesión actual", Strings.label("Current session"))
        XCTAssertEqual("Límite de 5 h", Strings.label("5h limit"))
        XCTAssertEqual("2 sesiones inactivas", Strings.plural("panel.idle", 2))
    }

    func testArabicPluralsAndDirection() {
        Strings.use("ar")
        XCTAssertTrue(Strings.rightToLeft)
        XCTAssertEqual("جلستان خاملتان", Strings.plural("panel.idle", 2))
        XCTAssertEqual("11 جلسة خاملة", Strings.plural("panel.idle", 11))
        XCTAssertEqual("one", Strings.pluralCategory("fr", 0))
    }
}
