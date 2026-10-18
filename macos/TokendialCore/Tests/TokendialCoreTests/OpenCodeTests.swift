import Foundation
import SQLite3
import XCTest
@testable import TokendialCore

/// opencode v2 keeps logins in opencode.db and stopped writing auth.json, so a replaced key never reached the
/// dial; and an expired key read as signed out, sending the user to a sign-in that OpenCode says will not help.
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

    /// The shape seen on the reporting machine: a replaced key goes inactive and a newer row holds the live one.
    func testTheNewestActiveGoRowWinsAndOtherIntegrationsAreIgnored() throws {
        store([(integration: "opencode-go", active: 0, created: 1790305105303, updated: 1792337464370, key: "sk-replaced"),
               (integration: "opencode-go", active: 1, created: 1792337464370, updated: 1792337464370, key: "sk-current"),
               (integration: "opencode", active: 1, created: 1792338050439, updated: 1792338050439, key: "oc-zen-not-go")])
        XCTAssertEqual(try read(), "sk-current")
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

    func testAnApiKeyExpiredBodyIsExpired() {
        XCTAssertTrue(OpenCodeUsage.keyExpired(Data(#"{"error":{"type":"authentication_error","code":"api_key_expired","message":"x"}}"#.utf8)))
    }

    func testAnUnauthorizedBodyIsNotExpired() {
        XCTAssertFalse(OpenCodeUsage.keyExpired(Data(#"{"type":"error","error":{"type":"AuthError","message":"Unauthorized"}}"#.utf8)))
    }

    func testANonJSONBodyIsNotExpired() {
        XCTAssertFalse(OpenCodeUsage.keyExpired(Data("not json".utf8)))
    }
}
