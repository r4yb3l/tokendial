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
        public static var `default`: Files {
            Files(apps: Paths.under(".config", "github-copilot", "apps.json"), hosts: Paths.under(".config", "github-copilot", "hosts.json"), ghHosts: Paths.under(".config", "gh", "hosts.yml"))
        }
    }

    public static func read(_ files: Files = .default) throws -> CopilotCredential {
        guard let found = fromPluginFile(files.apps) ?? fromPluginFile(files.hosts) ?? fromGhHosts(files.ghHosts) else { throw UsageError.needsSignIn() }
        return found
    }

    /// apps.json keys entries "github.com:<client id>"; hosts.json keys them by host. Either way: oauth_token and user.
    public static func fromPluginFile(_ path: URL) -> CopilotCredential? {
        guard let data = try? Data(contentsOf: path), let root = JSON.object(data) else { return nil }
        for key in root.keys.sorted() where key.hasPrefix("github.com") {
            guard let entry = root[key] as? JSONObject, let token = entry.str("oauth_token") else { continue }
            return CopilotCredential(token: token, user: entry.str("user"), source: "GitHub Copilot")
        }
        return nil
    }

    public static func fromGhHosts(_ path: URL) -> CopilotCredential? {
        guard let text = try? String(contentsOf: path, encoding: .utf8) else { return nil }
        func value(_ name: String) -> String? {
            guard let range = text.range(of: "\(name):", options: []) else { return nil }
            let rest = text[range.upperBound...].trimmingCharacters(in: .whitespaces)
            let token = rest.prefix { !$0.isWhitespace }
            return token.isEmpty ? nil : String(token)
        }
        guard let token = value("oauth_token") else { return nil }
        return CopilotCredential(token: token, user: value("user"), source: "GitHub CLI")
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
        if response.statusCode == 403 { throw UsageError.nothingMetered("No Copilot seat on this GitHub account") }
        if response.statusCode == 429 {
            let delay = Backoff.exponential(consecutive429, hint: RetryAfterHeader.from(response, now: now()))
            consecutive429 += 1
            retryAt = now().addingTimeInterval(delay)
            archive.setBackoff(id, until: retryAt)
            throw UsageError.rateLimited(delay)
        }
        try HTTP.throwUnlessOK(response)
        let parsed = try CopilotUsage.parse(data)
        consecutive429 = 0
        retryAt = nil
        archive.setBackoff(id, until: nil)
        plan = parsed.plan
        return ProviderReading(providerId: id, displayName: displayName, fidelity: .official, status: .live, windows: parsed.windows, headlineId: parsed.headline)
    }
}
