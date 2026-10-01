import Foundation

/// The GitHub OAuth token the Copilot plugin keeps, or failing that the GitHub CLI's.
public struct CopilotCredential: Equatable {
    public var token: String
    public var user: String?
    public var source: String

    public struct Files {
        public var apps: URL
        public var hosts: URL
        public var ghHosts: URL
        /// Both tools put $XDG_CONFIG_HOME ahead of ~/.config, and gh puts $GH_CONFIG_DIR ahead of that: the order gh documents.
        public static var `default`: Files {
            let plugin = Paths.xdg("XDG_CONFIG_HOME", orUnder: ".config").appendingPathComponent("github-copilot")
            let configured = ProcessInfo.processInfo.environment["GH_CONFIG_DIR"] ?? ""
            let gh = configured.isEmpty ? Paths.xdg("XDG_CONFIG_HOME", orUnder: ".config").appendingPathComponent("gh") : URL(fileURLWithPath: configured)
            return Files(apps: plugin.appendingPathComponent("apps.json"), hosts: plugin.appendingPathComponent("hosts.json"), ghHosts: gh.appendingPathComponent("hosts.yml"))
        }
    }

    public static func read(_ files: Files = .default) throws -> CopilotCredential {
        if let found = try fromPluginFile(files.apps) { return found }
        if let found = try fromPluginFile(files.hosts) { return found }
        if let found = try fromGhHosts(files.ghHosts) { return found }
        throw UsageError.needsSignIn()
    }

    /// apps.json keys entries "github.com:<client id>"; hosts.json keys them by host. Either way: oauth_token and user,
    /// github.com's only - a bare prefix also matched github.company.com, an Enterprise host whose token must never
    /// reach api.github.com. One editor per OAuth app signs in separately, so several github.com entries can hold
    /// tokens for different people, and nothing in the file says which one the user is looking at: the lowest client
    /// id used to win, which is someone else's quota as often as the right one. When they disagree no entry is
    /// returned at all, and hosts.yml - which does name the active account - answers instead.
    public static func fromPluginFile(_ path: URL) throws -> CopilotCredential? {
        guard let data = try JSON.credentialData(path), let root = JSON.object(data) else { return nil }
        var accounts = Set<String>()
        var first: CopilotCredential?
        for key in root.keys.sorted() where key == "github.com" || key.hasPrefix("github.com:") {
            guard let entry = root[key] as? JSONObject, let token = entry.str("oauth_token") else { continue }
            let user = entry.str("user")
            accounts.insert(user ?? token)
            if first == nil { first = CopilotCredential(token: token, user: user, source: "GitHub Copilot") }
        }
        return accounts.count == 1 ? first : nil
    }

    /// gh writes one block per host, and since 2.40 a users: map inside it holding every signed-in account's token;
    /// the active account's is the host's own oauth_token. Only that one is read, and only github.com's: the first
    /// oauth_token anywhere in the file used to win, which sent an Enterprise host's token, or another account's,
    /// to api.github.com.
    public static func fromGhHosts(_ path: URL) throws -> CopilotCredential? {
        guard let data = try JSON.credentialData(path), let text = String(data: data, encoding: .utf8) else { return nil }
        let host = ghHost(text, "github.com")
        guard let token = host["oauth_token"] else { return nil }
        return CopilotCredential(token: token, user: host["user"], source: "GitHub CLI")
    }

    /// The scalar keys directly under one top-level host of gh's hosts.yml; anything nested deeper is ignored.
    public static func ghHost(_ yaml: String, _ host: String) -> [String: String] {
        var values: [String: String] = [:]
        var inside = false
        var depth = -1
        for raw in yaml.split(whereSeparator: { $0.isNewline }) {
            let line = String(raw)
            let content = line.drop(while: { $0 == " " || $0 == "\t" })
            guard let first = content.first, first != "#" else { continue }
            let indent = line.count - content.count
            let (key, value) = yamlPair(String(content))
            if indent == 0 {
                if inside { break }
                inside = key == host
                continue
            }
            guard inside else { continue }
            if depth < 0 { depth = indent }
            if indent == depth, !value.isEmpty, values[key] == nil { values[key] = value }
        }
        return values
    }

    private static func yamlPair(_ content: String) -> (key: String, value: String) {
        let quotes = CharacterSet(charactersIn: "\"'")
        guard let colon = content.firstIndex(of: ":") else { return (content, "") }
        let key = content[..<colon].trimmingCharacters(in: .whitespaces).trimmingCharacters(in: quotes)
        let rest = content[content.index(after: colon)...].trimmingCharacters(in: .whitespaces)
        let value = String(rest.prefix(while: { !$0.isWhitespace })).trimmingCharacters(in: quotes)
        return (key, value)
    }
}

/// GET /copilot_internal/user: three quota snapshots, each reporting what remains. Unlimited snapshots are not windows.
public enum CopilotUsage {
    private static let table: [(String, String, String)] = [("premium_interactions", "premium", "Premium requests"), ("chat", "chat", "Chat"), ("completions", "completions", "Completions")]

    public static func parse(_ body: Data) throws -> Parsed {
        guard let root = JSON.object(body) else { throw UsageError.badResponse(0) }
        let plan = self.plan(root.str("access_type_sku") ?? root.str("copilot_plan"))
        let resetsAt = resetDate(root.str("quota_reset_date"))
        let snapshots = root.obj("quota_snapshots")
        var windows: [UsageWindow] = []
        for (key, id, label) in table {
            guard let snapshot = snapshots?.obj(key), snapshot.bool("unlimited") != true, let remaining = snapshot.num("percent_remaining") else { continue }
            windows.append(UsageWindow(id: id, label: label, usedFraction: min(1, max(0, 1 - remaining / 100)), resetsAt: resetsAt))
        }
        if windows.isEmpty { throw UsageError.nothingMetered("Unlimited on the \(plan ?? "current") plan — nothing to meter") }
        return Parsed(windows: windows, headline: windows[0].id, plan: plan)
    }

    private static let day: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(identifier: "UTC")
        f.dateFormat = "yyyy-MM-dd"
        return f
    }()

    /// GitHub answers an exhausted rate limit with 403 as often as with 429, so a 403 is a missing seat only when it
    /// carries neither x-ratelimit-remaining: 0 nor Retry-After. Read as a missing seat, a rate limit erased the last
    /// reading and told someone with a seat that nothing was metered.
    public static func rateLimited(_ response: HTTPURLResponse) -> Bool {
        switch response.statusCode {
        case 429: return true
        case 403:
            if response.value(forHTTPHeaderField: "Retry-After") != nil { return true }
            return response.value(forHTTPHeaderField: "x-ratelimit-remaining")?.trimmingCharacters(in: .whitespaces) == "0"
        default: return false
        }
    }

    /// quota_reset_date is a calendar date; the quota rolls at midnight UTC.
    public static func resetDate(_ text: String?) -> Date? {
        guard let text else { return nil }
        return day.date(from: text) ?? JSON.iso(text)
    }

    public static func plan(_ raw: String?) -> String? {
        switch raw {
        case nil: return nil
        case "free", "free_limited_copilot": return "Free"
        case "individual", "copilot_pro": return "Pro"
        case "individual_pro", "copilot_pro_plus": return "Pro+"
        case "business", "copilot_business_seat": return "Business"
        case "enterprise", "copilot_enterprise_seat": return "Enterprise"
        case .some(let other): return other.replacingOccurrences(of: "_", with: " ").capitalized
        }
    }
}

public final class CopilotProvider: UsageProvider {
    private static let endpoint = URL(string: "https://api.github.com/copilot_internal/user")!
    private let transport: Transport
    private let archive: ReadingArchive
    private let files: CopilotCredential.Files
    private let now: () -> Date
    private var retryAt: Date?
    private var consecutive429 = 0
    private var plan: String?

    public init(transport: Transport = SessionTransport(), archive: ReadingArchive = ReadingArchive(), files: CopilotCredential.Files = .default, now: @escaping () -> Date = Date.init) {
        self.transport = transport
        self.archive = archive
        self.files = files
        self.now = now
        retryAt = archive.backoffUntil(id)
    }

    public var id: String { "copilot" }
    public var displayName: String { "GitHub Copilot" }
    public var signIn: SignInRoute { .guidance("signin.copilot") }

    public func account() -> ProviderAccount? {
        guard let credential = try? CopilotCredential.read(files) else { return nil }
        return ProviderAccount(label: credential.user, plan: plan, source: credential.source, manageURL: URL(string: "https://github.com/settings/copilot/features"))
    }

    public func read() async throws -> ProviderReading {
        if let at = retryAt, at > now() { throw UsageError.rateLimited(at.timeIntervalSince(now())) }
        let credential = try CopilotCredential.read(files)
        let request = HTTP.request(Self.endpoint, headers: ["Authorization": "token \(credential.token)", "Editor-Version": "vscode/1.104.0", "Editor-Plugin-Version": "copilot-chat/0.30.0", "User-Agent": "GitHubCopilotChat/0.30.0"])
        let (data, response) = try await transport.send(request)
        Log.usage.debug("copilot: user \(response.statusCode)")
        if CopilotUsage.rateLimited(response) {
            let delay = Backoff.exponential(consecutive429, hint: RetryAfterHeader.from(response, now: now()))
            consecutive429 += 1
            retryAt = now().addingTimeInterval(delay)
            archive.setBackoff(id, until: retryAt)
            throw UsageError.rateLimited(delay)
        }
        if response.statusCode == 403 { throw UsageError.nothingMetered("No Copilot seat on this GitHub account") }
        try HTTP.throwUnlessOK(response)
        let parsed = try CopilotUsage.parse(data)
        consecutive429 = 0
        retryAt = nil
        archive.setBackoff(id, until: nil)
        plan = parsed.plan
        return ProviderReading(providerId: id, displayName: displayName, fidelity: .official, status: .live, windows: parsed.windows, headlineId: parsed.headline)
    }
}
