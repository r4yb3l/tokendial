import Foundation
import SQLite3
import XCTest
@testable import TokendialCore

/// opencode v2 keeps logins in opencode.db and stopped writing auth.json, so a replaced key never reached the
/// dial. And the usage endpoint answers api_key_expired for Go keys opencode itself runs on, while it accepts
/// the OpenCode Console key: reading only the Go key told a working user their key had expired.
final class OpenCodeTests: XCTestCase {
    private var root: URL!

    private var database: URL { root.appendingPathComponent("opencode.db") }
    private var authFile: URL { root.appendingPathComponent("auth.json") }

    override func setUpWithError() throws {
        root = FileManager.default.temporaryDirectory.appendingPathComponent("tokendial-opencode-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: root)
    }

    /// The table as opencode v2.0.32 creates it, with integration rows as (integration, active, created, updated, key).
    private func store(_ rows: [(integration: String, active: Int, created: Int, updated: Int, key: String)]) {
        var db: OpaquePointer?
        XCTAssertEqual(sqlite3_open(database.path, &db), SQLITE_OK)
        defer { sqlite3_close(db) }
        XCTAssertEqual(sqlite3_exec(db, "CREATE TABLE credential (id text PRIMARY KEY, integration_id text, label text NOT NULL, value text NOT NULL, connector_id text, method_id text, active integer, time_created integer NOT NULL, time_updated integer NOT NULL)", nil, nil, nil), SQLITE_OK)
        for row in rows {
            let value = "{\"type\":\"key\",\"key\":\"\(row.key)\"}"
            let sql = "INSERT INTO credential VALUES ('\(UUID().uuidString)', '\(row.integration)', 'default', '\(value)', NULL, NULL, \(row.active), \(row.created), \(row.updated))"
            XCTAssertEqual(sqlite3_exec(db, sql, nil, nil, nil), SQLITE_OK)
        }
    }

    private func writeAuth(_ key: String) throws {
        try Data("{\"opencode-go\":{\"type\":\"api\",\"key\":\"\(key)\"},\"openai\":{\"type\":\"api\",\"key\":\"not-ours\"}}".utf8).write(to: authFile)
    }

    private func read() throws -> String? {
        try OpenCodeCredential.read(file: authFile, database: database)
    }

    func testARowInTheDatabaseIsTheKey() throws {
        store([(integration: "opencode-go", active: 1, created: 1792337464370, updated: 1792337464370, key: "sk-db")])
        XCTAssertEqual(try read(), "sk-db")
    }

    func testTheDatabaseWinsOverAnAuthFileV1LeftBehind() throws {
        store([(integration: "opencode-go", active: 1, created: 1792337464370, updated: 1792337464370, key: "sk-db")])
        try writeAuth("sk-v1-stale")
        XCTAssertEqual(try read(), "sk-db")
    }

    /// The newest active Go row wins over a replaced one, and other vendors' rows are never claimed.
    func testTheNewestActiveGoRowWinsAndOtherVendorsAreIgnored() throws {
        store([(integration: "opencode-go", active: 0, created: 1790305105303, updated: 1792337464370, key: "sk-replaced"),
               (integration: "opencode-go", active: 1, created: 1792337464370, updated: 1792337464370, key: "sk-current"),
               (integration: "openai", active: 1, created: 1792338050439, updated: 1792338050439, key: "not-ours")])
        XCTAssertEqual(try read(), "sk-current")
    }

    /// The shape on the reporting machine: usage answered 200 only to the Console key; both Go keys got
    /// api_key_expired while opencode ran on them.
    func testTheConsoleKeyWinsOverEveryGoKey() throws {
        store([(integration: "opencode-go", active: 0, created: 1790305105303, updated: 1792337464370, key: "sk-legacy"),
               (integration: "opencode-go", active: 1, created: 1792337464370, updated: 1792337464370, key: "sk-go"),
               (integration: "opencode", active: 1, created: 1792338050439, updated: 1792338050439, key: "sk-console")])
        try writeAuth("sk-v1-stale")
        XCTAssertEqual(try read(), "sk-console")
    }

    func testAnInactiveConsoleKeyIsNotUsed() throws {
        store([(integration: "opencode", active: 0, created: 1, updated: 1, key: "sk-console-old"),
               (integration: "opencode-go", active: 1, created: 2, updated: 2, key: "sk-go")])
        XCTAssertEqual(try read(), "sk-go")
    }

    func testOfTwoActiveGoRowsTheNewestWins() throws {
        store([(integration: "opencode-go", active: 1, created: 1790305105303, updated: 1790305105303, key: "sk-older"),
               (integration: "opencode-go", active: 1, created: 1792337464370, updated: 1792337464370, key: "sk-newer")])
        XCTAssertEqual(try read(), "sk-newer")
    }

    func testInactiveRowsOnlyFallBackToTheAuthFile() throws {
        store([(integration: "opencode-go", active: 0, created: 1790305105303, updated: 1792337464370, key: "sk-replaced")])
        try writeAuth("sk-v1")
        XCTAssertEqual(try read(), "sk-v1")
    }

    func testWithoutADatabaseTheAuthFileIsStillRead() throws {
        try writeAuth("sk-v1")
        XCTAssertEqual(try read(), "sk-v1")
    }

    func testNeitherSourceIsNoKey() throws {
        XCTAssertNil(try read())
    }

    func testADatabaseWithoutTheTableFallsBackInsteadOfThrowing() throws {
        try Data().write(to: database)
        try writeAuth("sk-v1")
        XCTAssertEqual(try read(), "sk-v1")
    }

    func testAnApiKeyExpiredBodyIsARefusedGoKey() {
        XCTAssertTrue(OpenCodeUsage.refusesGoKey(Data(#"{"error":{"type":"authentication_error","code":"api_key_expired","message":"x"}}"#.utf8)))
    }

    func testAnUnauthorizedBodyIsNotARefusedGoKey() {
        XCTAssertFalse(OpenCodeUsage.refusesGoKey(Data(#"{"type":"error","error":{"type":"AuthError","message":"Unauthorized"}}"#.utf8)))
    }

    func testANonJSONBodyIsNotARefusedGoKey() {
        XCTAssertFalse(OpenCodeUsage.refusesGoKey(Data("not json".utf8)))
    }
}
