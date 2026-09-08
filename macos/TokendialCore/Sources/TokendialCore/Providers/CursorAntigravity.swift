import Foundation
import SQLite3

/// Read-only access to a database another app owns in WAL mode: plain read-only first, immutable next.
/// No copy is ever made: a copy of Cursor's store would hold its session token.
public enum Sqlite {
    public static func column(_ path: URL, _ sql: String, parameter: String? = nil) -> [String]? {
        rows(path, sql, parameter: parameter)?.compactMap { $0.first ?? nil }
    }

    public static func rows(_ path: URL, _ sql: String, parameter: String? = nil) -> [[String?]]? {
        guard FileManager.default.fileExists(atPath: path.path) else { return nil }
        let candidates = ["file:\(path.path)?mode=ro", "file:\(path.path)?immutable=1"]
        for uri in candidates {
            var db: OpaquePointer?
            guard sqlite3_open_v2(uri, &db, SQLITE_OPEN_READONLY | SQLITE_OPEN_URI | SQLITE_OPEN_NOMUTEX, nil) == SQLITE_OK, let db else { sqlite3_close(db); continue }
            defer { sqlite3_close(db) }
            sqlite3_busy_timeout(db, 1000)
            var statement: OpaquePointer?
            guard sqlite3_prepare_v2(db, sql, -1, &statement, nil) == SQLITE_OK, let statement else { continue }
            defer { sqlite3_finalize(statement) }
            if let parameter { sqlite3_bind_text(statement, 1, parameter, -1, unsafeBitCast(-1, to: sqlite3_destructor_type.self)) }
            var result: [[String?]] = []
            var failed = false
            loop: while true {
                switch sqlite3_step(statement) {
                case SQLITE_ROW:
                    let count = Int(sqlite3_column_count(statement))
                    result.append((0..<count).map { i in sqlite3_column_text(statement, Int32(i)).map { String(cString: $0) } })
                case SQLITE_DONE: break loop
                default: failed = true; break loop
                }
            }
            if !failed { return result }
        }
        return nil
    }
}

/// The editor's own session, read from its global state database. The cookie pair is what the usage endpoint accepts.
public struct CursorCredential: Equatable {
    public var accessToken: String
    public var accountId: String

    public static var defaultStore: URL { Paths.under("Library", "Application Support", "Cursor", "User", "globalStorage", "state.vscdb") }
    public var cookie: String { "WorkosCursorSessionToken=\(accountId)::\(accessToken)" }

    public static func read(store: URL = defaultStore) throws -> CursorCredential {
        guard let token = value(store, "cursorAuth/accessToken"), let account = value(store, "cursorAuth/stripeMembershipAuthId") else { throw UsageError.needsSignIn() }
        return CursorCredential(accessToken: token, accountId: account)
    }

    public static func account(store: URL = defaultStore) -> ProviderAccount? {
        guard let email = value(store, "cursorAuth/cachedEmail") else { return nil }
        return ProviderAccount(label: email, plan: value(store, "cursorAuth/stripeMembershipType"), source: "Cursor", manageURL: URL(string: "https://cursor.com/dashboard"))
    }

    private static func value(_ store: URL, _ key: String) -> String? {
        Sqlite.column(store, "SELECT value FROM ItemTable WHERE key = ?", parameter: key)?.first.flatMap { $0.isEmpty ? nil : $0 }
    }
}

public final class CursorProvider: UsageProvider {
    private static let endpoint = URL(string: "https://cursor.com/api/usage-summary")!
    private let transport: Transport
    private let store: URL

    public init(transport: Transport = SessionTransport(), store: URL = CursorCredential.defaultStore) {
        self.transport = transport
        self.store = store
    }

    public var id: String { "cursor" }
    public var displayName: String { "Cursor" }
    public var signIn: SignInRoute { .openApp(appKey: "cursor", name: "Cursor") }
    public func account() -> ProviderAccount? { CursorCredential.account(store: store) }

    public func read() async throws -> ProviderReading {
        let credential = try CursorCredential.read(store: store)
        let (data, response) = try await transport.send(HTTP.request(Self.endpoint, headers: ["Cookie": credential.cookie]))
        Log.usage.debug("cursor: usage \(response.statusCode)")
        try HTTP.throwUnlessOK(response)
        let parsed = try CursorUsage.parse(data)
        return ProviderReading(providerId: id, displayName: displayName, fidelity: .official, status: .live, windows: parsed.windows, headlineId: parsed.headline)
    }
}

/// Antigravity's language server: found through the process table, spoken to over loopback HTTPS with its CSRF token.
public struct Bridge {
    public static let path = "/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary"
    public var ports: [Int]
    public var csrf: String

    public static func discover() -> Bridge? {
        let processes = shell("/bin/ps", ["-axo", "pid=,command="])
        for line in processes.split(separator: "\n") {
            let text = line.trimmingCharacters(in: .whitespaces)
            guard text.contains("language_server"), text.contains("--csrf_token"), let csrf = csrfToken(text) else { continue }
            let pid = text.prefix { $0.isNumber }
            let ports = listeningPorts(pid: String(pid))
            if !ports.isEmpty { return Bridge(ports: ports, csrf: csrf) }
        }
        return nil
    }

    public static func csrfToken(_ commandLine: String) -> String? {
        let parts = commandLine.split(separator: " ").map(String.init)
        guard let index = parts.firstIndex(of: "--csrf_token"), index + 1 < parts.count else { return nil }
        return parts[index + 1].trimmingCharacters(in: CharacterSet(charactersIn: "\""))
    }

    private static func listeningPorts(pid: String) -> [Int] {
        let out = shell("/usr/sbin/lsof", ["-nP", "-a", "-p", pid, "-iTCP", "-sTCP:LISTEN"])
        var ports = Set<Int>()
        for line in out.split(separator: "\n").dropFirst() {
            if let range = line.range(of: ":", options: .backwards) {
                let tail = line[range.upperBound...].prefix { $0.isNumber }
                if let port = Int(tail) { ports.insert(port) }
            }
        }
        return ports.sorted()
    }

    private static func shell(_ launchPath: String, _ arguments: [String]) -> String {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: launchPath)
        process.arguments = arguments
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = FileHandle.nullDevice
        do { try process.run() } catch { return "" }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        return String(decoding: data, as: UTF8.self)
    }

    /// Two ports open and only one serves the RPC: try each; forceRefresh or the server answers from its cache.
    public func quota(_ transport: Transport) async throws -> [UsageWindow] {
        var last: Error?
        for port in ports {
            guard let url = URL(string: "https://127.0.0.1:\(port)\(Self.path)"), SessionTransport.isLoopback(url.host ?? "") else { continue }
            do {
                let request = HTTP.request(url, method: "POST", headers: ["x-codeium-csrf-token": csrf], body: Data("{\"forceRefresh\":true}".utf8))
                let (data, response) = try await transport.send(request)
                guard (200..<300).contains(response.statusCode) else { throw UsageError.badResponse(response.statusCode) }
                let windows = AntigravityUsage.parseBridge(data)
                if !windows.isEmpty { return windows }
            } catch { last = error }
        }
        if let last { throw last }
        return []
    }
}

/// A plain count of model requests today from Antigravity's transcripts, for accounts whose quota nothing publishes.
public enum AntigravityTranscripts {
    public static var defaultRoot: URL { Paths.under(".gemini", "antigravity", "brain") }
    public static func transcript(of trajectory: URL) -> URL { trajectory.appendingPathComponent(".system_generated/logs/transcript.jsonl") }

    public static func requestsToday(root: URL = defaultRoot, now: Date, zone: TimeZone = .current) -> Int {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = zone
        let today = calendar.startOfDay(for: now)
        var count = 0
        for name in (try? FileManager.default.contentsOfDirectory(atPath: root.path)) ?? [] {
            let file = transcript(of: root.appendingPathComponent(name))
            guard let text = try? String(contentsOf: file, encoding: .utf8) else { continue }
            for line in text.split(separator: "\n") {
                guard let root = JSON.parse(String(line)) as? JSONObject, root.str("source") == "MODEL", let at = root.date("created_at") else { continue }
                if calendar.startOfDay(for: at) == today { count += 1 }
            }
        }
        return count
    }
}

/// Language server first, Google's quota endpoint second, a request count last. Id stays "antigravity".
public final class AntigravityProvider: UsageProvider {
    private static let gate = URL(string: "https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist")!
    private static let quota = URL(string: "https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuotaSummary")!
    private let google: Transport
    private let local: Transport
    private let readCredential: () throws -> AntigravityUsage.Credential
    private let discover: () -> Bridge?
    private let requestsToday: () -> Int
    private let now: () -> Date
    private var held: AntigravityUsage.Credential?
    private var bridge: Bridge?
    private var everBridged = false

    public init(google: Transport = SessionTransport(), local: Transport = SessionTransport(timeout: 10, allowLoopbackSelfSigned: true), readCredential: @escaping () throws -> AntigravityUsage.Credential = AntigravityProvider.keychainCredential, discover: @escaping () -> Bridge? = Bridge.discover, requestsToday: (() -> Int)? = nil, now: @escaping () -> Date = Date.init) {
        self.google = google
        self.local = local
        self.readCredential = readCredential
        self.discover = discover
        self.now = now
        self.requestsToday = requestsToday ?? { AntigravityTranscripts.requestsToday(now: now()) }
    }

    /// Go's keyring on macOS: a generic password with service "gemini" and account "antigravity".
    public static func keychainCredential() throws -> AntigravityUsage.Credential {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "gemini", kSecAttrAccount as String: "antigravity", kSecMatchLimit as String: kSecMatchLimitOne, kSecReturnData as String: true]
        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess, let data = item as? Data, let text = String(data: data, encoding: .utf8) else { throw UsageError.needsSignIn() }
        guard let credential = AntigravityUsage.Credential.decode(text) else { throw UsageError.needsSignIn() }
        return credential
    }

    public var id: String { "antigravity" }
    public var displayName: String { "Antigravity" }
    public var signIn: SignInRoute { .openApp(appKey: "antigravity", name: "Antigravity") }
    public func forgetCredential() { held = nil }

    public func account() -> ProviderAccount? {
        guard let c = held ?? (try? readCredential()) else { return nil }
        return ProviderAccount(label: nil, plan: c.authMethod == "consumer" ? "Personal" : (c.authMethod.isEmpty ? nil : c.authMethod), source: "Antigravity", manageURL: URL(string: "https://antigravity.google"))
    }

    public func read() async throws -> ProviderReading {
        let credential = try held ?? readCredential()
        held = credential
        if credential.expiresAt <= now() { held = nil; throw UsageError.credentialExpired() }
        try await passGate(credential)
        let bridged = await localQuota()
        if !bridged.isEmpty {
            everBridged = true
            return ProviderReading(providerId: id, displayName: displayName, fidelity: .official, status: .live, windows: bridged, headlineId: "gemini-weekly")
        }
        if everBridged { throw UsageError.credentialExpired() }
        let quota = await googleQuota(credential)
        if !quota.isEmpty { return ProviderReading(providerId: id, displayName: displayName, fidelity: .official, status: .live, windows: quota) }
        return ProviderReading(providerId: id, displayName: displayName, fidelity: .derived, status: .live, windows: [UsageWindow(id: "requests", label: "Requests today · no limit published", count: requestsToday())])
    }

    private func passGate(_ credential: AntigravityUsage.Credential) async throws {
        let request = HTTP.request(Self.gate, method: "POST", headers: ["Authorization": "Bearer \(credential.accessToken)"], body: Data("{\"metadata\":{\"pluginType\":\"GEMINI\"}}".utf8))
        let (_, response) = try await google.send(request)
        Log.usage.debug("antigravity: gate \(response.statusCode)")
        switch response.statusCode {
        case 200: return
        case 401: held = nil; throw UsageError.needsSignIn()
        case 403: throw UsageError.needsSignIn()
        case 429: throw UsageError.rateLimited(RetryAfterHeader.from(response, now: now()) ?? 0)
        default: throw UsageError.badResponse(response.statusCode)
        }
    }

    private func localQuota() async -> [UsageWindow] {
        for _ in 0..<2 {
            if bridge == nil { bridge = discover() }
            guard let current = bridge else { return [] }
            do {
                let windows = try await current.quota(local)
                if !windows.isEmpty { return windows }
            } catch { Log.usage.debug("antigravity: bridge \(error)") }
            bridge = nil
        }
        return []
    }

    private func googleQuota(_ credential: AntigravityUsage.Credential) async -> [UsageWindow] {
        let request = HTTP.request(Self.quota, method: "POST", headers: ["Authorization": "Bearer \(credential.accessToken)"], body: Data("{}".utf8))
        guard let (data, response) = try? await google.send(request) else { return [] }
        Log.usage.debug("antigravity: quota \(response.statusCode)")
        return (200..<300).contains(response.statusCode) ? AntigravityUsage.parseGoogleQuota(data) : []
    }
}
