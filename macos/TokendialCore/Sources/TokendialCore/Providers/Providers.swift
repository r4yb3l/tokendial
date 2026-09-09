import Foundation
import Security

/// One Claude Code configuration directory: ~/.claude, or ~/.claude-<slug> for a second account.
public struct ClaudeProfile: Equatable, Sendable {
    public var slug: String?
    public var directory: URL

    public init(slug: String?, directory: URL) {
        self.slug = slug
        self.directory = directory
    }

    public var id: String { slug.map { "claude-\($0)" } ?? "claude" }
    public var displayName: String { slug.map { "Claude Code (\($0))" } ?? "Claude Code" }
    public var credentialsFile: URL { directory.appendingPathComponent(".credentials.json") }
    public var sessionsDirectory: URL { directory.appendingPathComponent("sessions") }
    public var signInCommand: String { slug.map { "CLAUDE_CONFIG_DIR=~/.claude-\($0) claude" } ?? "claude" }

    public static func `default`(home: URL = Paths.home) -> ClaudeProfile { ClaudeProfile(slug: nil, directory: home.appendingPathComponent(".claude")) }

    private static let markers = ["sessions", "projects", "settings.json", "history.jsonl", ".claude.json"]

    /// The default first, then every ~/.claude-<slug> Claude Code has actually used, slugs in ordinal order.
    public static func discover(home: URL = Paths.home) -> [ClaudeProfile] {
        var extras: [ClaudeProfile] = []
        let fm = FileManager.default
        for name in (try? fm.contentsOfDirectory(atPath: home.path)) ?? [] where name.hasPrefix(".claude-") {
            let slug = String(name.dropFirst(".claude-".count))
            let dir = home.appendingPathComponent(name)
            guard !slug.isEmpty, markers.contains(where: { fm.fileExists(atPath: dir.appendingPathComponent($0).path) }) else { continue }
            extras.append(ClaudeProfile(slug: slug, directory: dir))
        }
        extras.sort { $0.slug!.utf8.lexicographicallyPrecedes($1.slug!.utf8) }
        return [ClaudeProfile.default(home: home)] + extras
    }
}

/// The OAuth token Claude Code stores: in the login keychain on macOS, in .credentials.json elsewhere. Read, never written.
public struct ClaudeCredential: Equatable {
    public var accessToken: String
    public var expiresAt: Date
    public var plan: String?

    public func expired(_ now: Date) -> Bool { expiresAt <= now }

    public static func parse(_ data: Data) throws -> ClaudeCredential {
        guard let root = JSON.object(data), let oauth = root.obj("claudeAiOauth"), let token = oauth.str("accessToken") else { throw UsageError.needsSignIn() }
        return ClaudeCredential(accessToken: token, expiresAt: oauth.epochMillis("expiresAt") ?? .distantPast, plan: oauth.str("subscriptionType"))
    }

    /// Keychain first (service "Claude Code-credentials", one item per profile), then the file.
    public static func read(profile: ClaudeProfile) throws -> ClaudeCredential {
        if let data = try Keychain.genericPassword(service: profile.slug.map { "Claude Code-credentials-\($0)" } ?? "Claude Code-credentials") {
            return try parse(data)
        }
        if let data = try? Data(contentsOf: profile.credentialsFile) { return try parse(data) }
        throw UsageError.needsSignIn()
    }
}

public enum Keychain {
    /// The read did not go through: macOS asks the user before one app may read an item another app owns,
    /// and the answer was no, or the dialog could not be shown. Retrying straight away only asks again.
    public struct Refused: Error {}

    /// The first generic password for a service, whatever the account; nil when there is no such item.
    public static func genericPassword(service: String) throws -> Data? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecMatchLimit as String: kSecMatchLimitOne,
            kSecReturnData as String: true
        ]
        var item: CFTypeRef?
        switch SecItemCopyMatching(query as CFDictionary, &item) {
        case errSecSuccess: return item as? Data
        case errSecItemNotFound: return nil
        default: throw Refused()
        }
    }
}

/// Claude Code's own usage endpoint, one provider per profile. Exponential back-off on 429, persisted.
public final class ClaudeProvider: UsageProvider {
    private static let endpoint = URL(string: "https://api.anthropic.com/api/oauth/usage")!
    public let profile: ClaudeProfile
    private let transport: Transport
    private let archive: ReadingArchive
    private let now: () -> Date
    private let credentialReader: (ClaudeProfile) throws -> ClaudeCredential
    private static let quietAfterRefusal: TimeInterval = 15 * 60
    private var held: ClaudeCredential?
    private var refusedAt: Date?
    private var retryAt: Date?
    private var consecutive429 = 0

    public init(profile: ClaudeProfile = .default(), transport: Transport = SessionTransport(), archive: ReadingArchive = ReadingArchive(), now: @escaping () -> Date = Date.init, credentialReader: @escaping (ClaudeProfile) throws -> ClaudeCredential = ClaudeCredential.read) {
        self.profile = profile
        self.transport = transport
        self.archive = archive
        self.now = now
        self.credentialReader = credentialReader
        retryAt = archive.backoffUntil(profile.id)
    }

    public var id: String { profile.id }
    public var displayName: String { profile.displayName }
    public var signIn: SignInRoute { .guidance("signin.claude", ["command": profile.signInCommand]) }

    public func account() -> ProviderAccount? {
        guard let credential = try? load() else { return nil }
        return ProviderAccount(label: nil, plan: credential.plan, source: profile.slug.map { "Claude Code in ~/.claude-\($0)" } ?? "Claude Code", manageURL: URL(string: "https://claude.ai/settings/usage"))
    }

    public func forgetCredential() {
        held = nil
        refusedAt = nil
    }

    /// The credential is held until it ages out, so the keychain is read once per run rather than once per
    /// poll. A refusal is held too: without that, one "Deny" would raise the same dialog every poll.
    private func load() throws -> ClaudeCredential {
        if let held, !held.expired(now()) { return held }
        if let refusedAt, now().timeIntervalSince(refusedAt) < Self.quietAfterRefusal { throw UsageError.needsSignIn() }
        do {
            let fresh = try credentialReader(profile)
            refusedAt = nil
            held = fresh
            return fresh
        } catch is Keychain.Refused {
            refusedAt = now()
            Log.usage.info("\(id): the keychain read was refused, asking again in \(Int(Self.quietAfterRefusal / 60)) min")
            throw UsageError.needsSignIn()
        }
    }

    public func read() async throws -> ProviderReading {
        if let at = retryAt, at > now() { throw UsageError.rateLimited(at.timeIntervalSince(now())) }
        do {
            let parsed = try await fetch(retry: true)
            retryAt = nil
            consecutive429 = 0
            archive.setBackoff(id, until: nil)
            return ProviderReading(providerId: id, displayName: displayName, fidelity: .official, status: .live, windows: parsed.windows, headlineId: parsed.headline)
        } catch let error as UsageError where error.kind == .rateLimited {
            consecutive429 += 1
            retryAt = now().addingTimeInterval(error.retryAfter)
            archive.setBackoff(id, until: retryAt)
            throw error
        }
    }

    private func fetch(retry: Bool) async throws -> Parsed {
        let credential = try load()
        if credential.expired(now()) { throw UsageError.credentialExpired() }
        let request = HTTP.request(Self.endpoint, headers: ["Authorization": "Bearer \(credential.accessToken)", "anthropic-beta": "oauth-2025-04-20"])
        let (data, response) = try await transport.send(request)
        Log.usage.debug("\(id): usage \(response.statusCode)")
        if response.statusCode == 401 || response.statusCode == 403 {
            held = nil
            if retry { return try await fetch(retry: false) }
            throw UsageError.needsSignIn()
        }
        if response.statusCode == 429 { throw UsageError.rateLimited(Backoff.exponential(consecutive429, hint: RetryAfterHeader.from(response, now: now()))) }
        try HTTP.throwUnlessOK(response)
        return try ClaudeUsage.parse(data)
    }
}

/// The ChatGPT sign-in the Codex CLI keeps in ~/.codex/auth.json.
public struct CodexCredential: Equatable {
    public var accessToken: String
    public var accountId: String
    public var idToken: String?

    public static var defaultFile: URL { Paths.under(".codex", "auth.json") }

    public static func read(file: URL = defaultFile, now: Date) throws -> CodexCredential {
        guard let data = try? Data(contentsOf: file), let root = JSON.object(data), let tokens = root.obj("tokens"),
              let access = tokens.str("access_token")?.trimmingCharacters(in: .whitespaces), let account = tokens.str("account_id")?.trimmingCharacters(in: .whitespaces) else { throw UsageError.needsSignIn() }
        let credential = CodexCredential(accessToken: access, accountId: account, idToken: tokens.str("id_token"))
        if credential.expired(now) { throw UsageError.credentialExpired() }
        return credential
    }

    public func expired(_ now: Date) -> Bool {
        guard let exp = Self.claims(accessToken)?.epochSeconds("exp") else { return false }
        return exp <= now
    }

    public func account() -> ProviderAccount {
        let claims = idToken.flatMap(Self.claims)
        return ProviderAccount(label: claims?.str("email"), plan: claims?.obj("https://api.openai.com/auth")?.str("chatgpt_plan_type"), source: "Codex", manageURL: URL(string: "https://chatgpt.com/#settings/Account"))
    }

    /// JWT claims, for labels and the expiry hint only; malformed tokens are refused rather than crashing.
    public static func claims(_ jwt: String) -> JSONObject? {
        let parts = jwt.split(separator: ".")
        guard parts.count >= 2 else { return nil }
        var payload = String(parts[1]).replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        while payload.count % 4 != 0 { payload.append("=") }
        guard let data = Data(base64Encoded: payload) else { return nil }
        return JSON.object(data)
    }
}

public final class CodexProvider: UsageProvider {
    private static let endpoint = URL(string: "https://chatgpt.com/backend-api/wham/usage")!
    private let transport: Transport
    private let archive: ReadingArchive
    private let file: URL
    private let now: () -> Date
    private var retryAt: Date?

    public init(transport: Transport = SessionTransport(), archive: ReadingArchive = ReadingArchive(), file: URL = CodexCredential.defaultFile, now: @escaping () -> Date = Date.init) {
        self.transport = transport
        self.archive = archive
        self.file = file
        self.now = now
        retryAt = archive.backoffUntil(id)
    }

    public var id: String { "codex" }
    public var displayName: String { "Codex" }
    public var signIn: SignInRoute { .openApp(appKey: "codex", name: "Codex") }

    public func account() -> ProviderAccount? {
        do { return try CodexCredential.read(file: file, now: now()).account() }
        catch let error as UsageError where error.kind == .credentialExpired { return ProviderAccount(label: nil, plan: nil, source: "Codex", manageURL: nil) }
        catch { return nil }
    }

    public func read() async throws -> ProviderReading {
        if let at = retryAt, at > now() { throw UsageError.rateLimited(at.timeIntervalSince(now())) }
        let credential = try CodexCredential.read(file: file, now: now())
        let request = HTTP.request(Self.endpoint, headers: ["Authorization": "Bearer \(credential.accessToken)", "ChatGPT-Account-Id": credential.accountId, "Cache-Control": "no-cache, no-store"])
        let (data, response) = try await transport.send(request)
        Log.usage.debug("codex: usage \(response.statusCode)")
        if response.statusCode == 429 {
            let delay = Backoff.flat(RetryAfterHeader.from(response, now: now()))
            retryAt = now().addingTimeInterval(delay)
            archive.setBackoff(id, until: retryAt)
            throw UsageError.rateLimited(delay)
        }
        try HTTP.throwUnlessOK(response)
        let parsed = try CodexUsage.parse(data, now: now())
        retryAt = nil
        archive.setBackoff(id, until: nil)
        return ProviderReading(providerId: id, displayName: displayName, fidelity: .official, status: .live, windows: parsed.windows, headlineId: parsed.headline)
    }
}

/// The Grok CLI session. Only entries issued by auth.x.ai are trusted; a customer IdP's token is meant for a private proxy.
public struct GrokCredential: Equatable {
    private static let issuer = "https://auth.x.ai"
    public static var defaultFile: URL { Paths.under(".grok", "auth.json") }
    public var key: String
    public var expiresAt: Date
    public var email: String?

    public func expired(_ now: Date) -> Bool { expiresAt <= now }

    public static func read(file: URL = defaultFile, now: Date) throws -> GrokCredential {
        guard let data = try? Data(contentsOf: file), let root = JSON.object(data) else { throw UsageError.needsSignIn() }
        guard let picked = pick(root, now: now) else { throw UsageError.needsSignIn() }
        return picked
    }

    public static func pick(_ root: JSONObject, now: Date) -> GrokCredential? {
        let trusted = root.keys.sorted().compactMap { key -> JSONObject? in
            guard let entry = root[key] as? JSONObject else { return nil }
            return key.hasPrefix(issuer) || entry.str("oidc_issuer") == issuer ? entry : nil
        }
        guard !trusted.isEmpty else { return nil }
        let entry = trusted.first { $0.date("expires_at").map { $0 > now } ?? true } ?? trusted[0]
        guard let key = entry.str("key") else { return nil }
        return GrokCredential(key: key, expiresAt: entry.date("expires_at") ?? now.addingTimeInterval(30 * 86400), email: entry.str("email"))
    }
}

public final class GrokProvider: UsageProvider {
    private static let endpoint = URL(string: "https://cli-chat-proxy.grok.com/v1/billing?format=credits")!
    private let transport: Transport
    private let file: URL
    private let now: () -> Date

    public init(transport: Transport = SessionTransport(), file: URL = GrokCredential.defaultFile, now: @escaping () -> Date = Date.init) {
        self.transport = transport
        self.file = file
        self.now = now
    }

    public var id: String { "grok" }
    public var displayName: String { "Grok" }
    public var signIn: SignInRoute { .guidance("signin.grok") }

    public func account() -> ProviderAccount? {
        guard let credential = try? GrokCredential.read(file: file, now: now()) else { return nil }
        return ProviderAccount(label: credential.email, plan: nil, source: "Grok", manageURL: URL(string: "https://grok.com/?_s=usage"))
    }

    public func read() async throws -> ProviderReading {
        let credential = try GrokCredential.read(file: file, now: now())
        if credential.expired(now()) { throw UsageError.credentialExpired() }
        let request = HTTP.request(Self.endpoint, headers: ["Authorization": "Bearer \(credential.key)", "X-XAI-Token-Auth": "xai-grok-cli"])
        let (data, response) = try await transport.send(request)
        Log.usage.debug("grok: billing \(response.statusCode)")
        if response.statusCode == 429 { throw UsageError.rateLimited(Backoff.floor) }
        try HTTP.throwUnlessOK(response)
        let parsed = try GrokUsage.parse(data)
        return ProviderReading(providerId: id, displayName: displayName, fidelity: .official, status: .live, windows: parsed.windows, headlineId: parsed.headline)
    }
}

/// Only the opencode-go entry; every other entry is another vendor's key.
public enum OpenCodeCredential {
    private static let keys = ["key", "apiKey", "api_key", "token", "accessToken"]
    public static var defaultFile: URL { Paths.under(".local", "share", "opencode", "auth.json") }

    public static func read(file: URL = defaultFile) -> String? {
        guard let data = try? Data(contentsOf: file), let root = JSON.object(data) else { return nil }
        return pick(root)
    }

    public static func pick(_ root: JSONObject) -> String? {
        guard let entry = root["opencode-go"] else { return nil }
        if let s = entry as? String { return s.isEmpty ? nil : s }
        guard let object = entry as? JSONObject else { return nil }
        return keys.lazy.compactMap { object.str($0) }.first
    }
}

public final class OpenCodeProvider: UsageProvider {
    private static let endpoint = URL(string: "https://opencode.ai/zen/go/v1/usage")!
    private let transport: Transport
    private let archive: ReadingArchive
    private let file: URL
    private let now: () -> Date
    private var retryAt: Date?
    private var consecutive429 = 0

    public init(transport: Transport = SessionTransport(), archive: ReadingArchive = ReadingArchive(), file: URL = OpenCodeCredential.defaultFile, now: @escaping () -> Date = Date.init) {
        self.transport = transport
        self.archive = archive
        self.file = file
        self.now = now
        retryAt = archive.backoffUntil(id)
    }

    public var id: String { "opencode" }
    public var displayName: String { "OpenCode" }
    public var signIn: SignInRoute { .guidance("signin.opencode") }
    public func account() -> ProviderAccount? { OpenCodeCredential.read(file: file) == nil ? nil : ProviderAccount(label: nil, plan: "Go", source: "OpenCode", manageURL: URL(string: "https://opencode.ai")) }

    public func read() async throws -> ProviderReading {
        if let at = retryAt, at > now() { throw UsageError.rateLimited(at.timeIntervalSince(now())) }
        guard let token = OpenCodeCredential.read(file: file) else { throw UsageError.needsSignIn() }
        let (data, response) = try await transport.send(HTTP.request(Self.endpoint, headers: ["Authorization": "Bearer \(token)"]))
        Log.usage.debug("opencode: usage \(response.statusCode)")
        if response.statusCode == 403 { throw UsageError.nothingMetered("No OpenCode Go subscription on this key") }
        if response.statusCode == 429 {
            let delay = Backoff.exponential(consecutive429, hint: RetryAfterHeader.from(response, now: now()))
            consecutive429 += 1
            retryAt = now().addingTimeInterval(delay)
            archive.setBackoff(id, until: retryAt)
            throw UsageError.rateLimited(delay)
        }
        try HTTP.throwUnlessOK(response)
        let parsed = try OpenCodeUsage.parse(data)
        consecutive429 = 0
        retryAt = nil
        archive.setBackoff(id, until: nil)
        return ProviderReading(providerId: id, displayName: displayName, fidelity: .official, status: .live, windows: parsed.windows, headlineId: parsed.headline)
    }
}

/// A Z.ai Coding Plan key borrowed from whichever tool holds one, in a fixed order. Only the console host survives.
public struct GlmCredential: Equatable {
    public var token: String
    public var console: URL
    public var source: String

    public struct Files {
        public var claudeSettings: URL
        public var zcodeConfig: URL
        public var zcodeCredentials: URL
        public var opencodeAuth: URL
        public static var `default`: Files {
            Files(claudeSettings: Paths.under(".claude", "settings.json"), zcodeConfig: Paths.under(".zcode", "v2", "config.json"), zcodeCredentials: Paths.under(".zcode", "v2", "credentials.json"), opencodeAuth: Paths.under(".local", "share", "opencode", "auth.json"))
        }
    }

    private static let openCodeIds = ["zai-coding-plan", "zai", "z-ai", "z.ai", "zhipu", "zhipuai"]
    private static let openCodeKeys = ["apiKey", "api_key", "token", "key", "accessToken", "auth_token"]

    public var china: Bool { console.host?.hasSuffix("bigmodel.cn") ?? false }

    public static func find(_ files: Files = .default) -> GlmCredential? {
        fromClaude(root(files.claudeSettings)) ?? fromZcodePlan(root(files.zcodeConfig)) ?? fromZcodeOAuth(root(files.zcodeCredentials)) ?? fromOpenCode(root(files.opencodeAuth))
    }

    /// Claude Code's key counts only when its base URL points at Z.ai; otherwise it is an Anthropic key.
    public static func fromClaude(_ root: JSONObject?) -> GlmCredential? {
        guard let env = root?.obj("env"), let token = env.str("ANTHROPIC_AUTH_TOKEN") ?? env.str("ANTHROPIC_API_KEY"),
              let base = env.str("ANTHROPIC_BASE_URL"), let url = URL(string: base), let host = url.host, isZai(host) else { return nil }
        return GlmCredential(token: token, console: consoleFor(host), source: "Claude Code")
    }

    public static func fromZcodePlan(_ root: JSONObject?) -> GlmCredential? {
        guard let providers = root?.obj("provider") else { return nil }
        for name in providers.keys.sorted() where name.contains("coding-plan") {
            guard let p = providers[name] as? JSONObject, p.bool("enabled") != false, let key = p.obj("options")?.str("apiKey") else { continue }
            let console = p.obj("options")?.str("baseURL").flatMap(URL.init(string:))?.host.map(consoleFor) ?? URL(string: "https://api.z.ai")!
            return GlmCredential(token: key, console: console, source: "ZCode")
        }
        return nil
    }

    /// An encrypted token cannot be read here and would only yield a 401 that reads as signed-out.
    public static func fromZcodeOAuth(_ root: JSONObject?) -> GlmCredential? {
        guard let token = root?.str("oauth:zai:access_token"), !token.hasPrefix("enc:v1:") else { return nil }
        return GlmCredential(token: token, console: URL(string: "https://api.z.ai")!, source: "ZCode")
    }

    public static func fromOpenCode(_ root: JSONObject?) -> GlmCredential? {
        guard let root else { return nil }
        for id in openCodeIds {
            guard let entry = root[id] else { continue }
            let key = (entry as? String) ?? (entry as? JSONObject).flatMap { object in openCodeKeys.lazy.compactMap { object.str($0) }.first }
            guard let key, !key.isEmpty else { continue }
            return GlmCredential(token: key, console: URL(string: id.hasPrefix("zhipu") ? "https://open.bigmodel.cn" : "https://api.z.ai")!, source: "OpenCode")
        }
        return nil
    }

    public static func isZai(_ host: String) -> Bool { host == "api.z.ai" || host.hasSuffix(".z.ai") || host == "open.bigmodel.cn" || host.hasSuffix(".bigmodel.cn") }
    public static func consoleFor(_ host: String) -> URL { URL(string: host.hasSuffix("bigmodel.cn") ? "https://open.bigmodel.cn" : "https://api.z.ai")! }

    public static func root(_ file: URL) -> JSONObject? {
        guard let data = try? Data(contentsOf: file) else { return nil }
        return JSON.object(data)
    }
}

public final class GlmProvider: UsageProvider {
    private let transport: Transport
    private let archive: ReadingArchive
    private let find: () -> GlmCredential?
    private let now: () -> Date
    private var retryAt: Date?
    private var consecutive429 = 0
    private var plan: String?

    public init(transport: Transport = SessionTransport(), archive: ReadingArchive = ReadingArchive(), find: @escaping () -> GlmCredential? = { GlmCredential.find() }, now: @escaping () -> Date = Date.init) {
        self.transport = transport
        self.archive = archive
        self.find = find
        self.now = now
        retryAt = archive.backoffUntil(id)
    }

    public var id: String { "glm" }
    public var displayName: String { "GLM" }
    public var signIn: SignInRoute { .guidance("signin.glm") }

    public func account() -> ProviderAccount? {
        guard let credential = find() else { return nil }
        return ProviderAccount(label: nil, plan: plan, source: credential.source, manageURL: URL(string: credential.china ? "https://open.bigmodel.cn/usage" : "https://z.ai/manage-apikey/apikey-list"))
    }

    public func read() async throws -> ProviderReading {
        if let at = retryAt, at > now() { throw UsageError.rateLimited(at.timeIntervalSince(now())) }
        guard let credential = find() else { throw UsageError.needsSignIn() }
        do {
            let url = credential.console.appendingPathComponent("api/monitor/usage/quota/limit")
            let (data, response) = try await transport.send(HTTP.request(url, headers: ["Authorization": credential.token]))
            Log.usage.debug("glm: quota \(response.statusCode)")
            if response.statusCode == 429 { throw UsageError.rateLimited(Backoff.exponential(consecutive429, hint: RetryAfterHeader.from(response, now: now()))) }
            try HTTP.throwUnlessOK(response)
            let parsed: Parsed
            do { parsed = try GlmUsage.parse(data) }
            catch let error as UsageError where error.kind == .rateLimited { throw UsageError.rateLimited(Backoff.exponential(consecutive429, hint: nil)) }
            consecutive429 = 0
            retryAt = nil
            archive.setBackoff(id, until: nil)
            plan = parsed.plan
            return ProviderReading(providerId: id, displayName: displayName, fidelity: .official, status: .live, windows: parsed.windows, headlineId: parsed.headline)
        } catch let error as UsageError where error.kind == .rateLimited {
            consecutive429 += 1
            retryAt = now().addingTimeInterval(error.retryAfter)
            archive.setBackoff(id, until: retryAt)
            throw error
        }
    }
}
