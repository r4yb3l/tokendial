import Foundation

/// The OAuth token Gemini CLI caches after "Login with Google". Read only; the CLI refreshes it when it runs.
public struct GeminiCredential: Equatable, Sendable {
    public var accessToken: String
    public var expiresAt: Date
    public var email: String?

    public static var directory: URL {
        let home = ProcessInfo.processInfo.environment["GEMINI_CLI_HOME"]
        if let home, !home.isEmpty { return URL(fileURLWithPath: home).appendingPathComponent(".gemini") }
        return Paths.under(".gemini")
    }

    public static var defaultFile: URL { directory.appendingPathComponent("oauth_creds.json") }
    public static var defaultSettings: URL { directory.appendingPathComponent("settings.json") }
    public static var defaultAccounts: URL { directory.appendingPathComponent("google_accounts.json") }

    public func expired(_ now: Date) -> Bool { expiresAt <= now }

    /// Throws nothingMetered when the CLI signs in with an API key or Vertex, which publish no quota.
    public static func read(file: URL? = nil, settings: URL? = nil, accounts: URL? = nil) throws -> GeminiCredential {
        let file = file ?? defaultFile
        if let auth = authType(settings ?? defaultSettings), auth != "oauth-personal" {
            throw UsageError.nothingMetered("Gemini CLI is signed in with \(auth); no quota is published for it")
        }
        guard let data = try? Data(contentsOf: file), let root = JSON.object(data),
              let token = root.str("access_token") else { throw UsageError.needsSignIn() }
        return GeminiCredential(accessToken: token,
                                expiresAt: root.epochMillis("expiry_date") ?? .distantPast,
                                email: activeAccount(accounts ?? defaultAccounts))
    }

    private static func authType(_ file: URL) -> String? {
        guard let data = try? Data(contentsOf: file), let root = JSON.object(data) else { return nil }
        return root.obj("security")?.obj("auth")?.str("selectedType") ?? root.str("selectedAuthType")
    }

    private static func activeAccount(_ file: URL) -> String? {
        guard let data = try? Data(contentsOf: file), let root = JSON.object(data) else { return nil }
        return root.str("active")
    }
}

/// The two Code Assist answers Gemini CLI itself reads: the gate for the project and tier, the quota for the
/// per-model buckets.
public enum GeminiUsage {
    public static let gateBody = "{\"metadata\":{\"ideType\":\"IDE_UNSPECIFIED\",\"platform\":\"PLATFORM_UNSPECIFIED\",\"pluginType\":\"GEMINI\"}}"

    /// The gate as a parse: an ineligible account is nothing metered, an eligible one yields its tier as the plan.
    public static func parseGate(_ body: Data) throws -> Parsed {
        let gate = CodeAssist.parseGate(body)
        guard gate.eligible else {
            throw UsageError.nothingMetered(gate.reason ?? "Gemini CLI no longer serves this account; Google points it to Antigravity")
        }
        return Parsed(windows: [], headline: nil, plan: gate.tier)
    }

    /// One window per model; the dial shows the tightest.
    public static func parse(_ body: Data) throws -> Parsed {
        let windows = CodeAssist.parseQuotaBuckets(body)
        guard !windows.isEmpty else { throw UsageError.nothingMetered("Gemini publishes no quota for this account") }
        let headline = windows.max { ($0.usedFraction ?? 0) < ($1.usedFraction ?? 0) }?.id
        return Parsed(windows: windows, headline: headline)
    }
}

/// Gemini CLI through the same Code Assist endpoints the CLI calls. No session monitor: the CLI keeps no
/// session registry.
public final class GeminiProvider: UsageProvider {
    private let transport: Transport
    private let readCredential: () throws -> GeminiCredential
    private let archive: ReadingArchive?
    private let now: () -> Date
    private var plan: String?

    public init(transport: Transport = SessionTransport(),
                readCredential: @escaping () throws -> GeminiCredential = { try GeminiCredential.read() },
                archive: ReadingArchive? = nil,
                now: @escaping () -> Date = Date.init) {
        self.transport = transport
        self.readCredential = readCredential
        self.archive = archive
        self.now = now
    }

    public var id: String { "gemini" }
    public var displayName: String { "Gemini CLI" }
    public var signIn: SignInRoute { .guidance("signin.gemini") }

    public func account() -> ProviderAccount? {
        guard let credential = try? readCredential() else { return nil }
        return ProviderAccount(label: credential.email, plan: plan, source: "Gemini CLI", manageURL: URL(string: "https://codeassist.google/"))
    }

    public func read() async throws -> ProviderReading {
        if let until = archive?.backoffUntil(id), until > now() { throw UsageError.rateLimited(until.timeIntervalSince(now())) }
        let credential = try readCredential()
        if credential.expired(now()) { throw UsageError.credentialExpired() }

        let (gateBody, gateResponse) = try await CodeAssist.post(transport, CodeAssist.gate, token: credential.accessToken, body: GeminiUsage.gateBody)
        Log.usage.debug("gemini: gate \(gateResponse.statusCode)")
        try check(gateResponse)
        plan = try GeminiUsage.parseGate(gateBody).plan

        // The environment wins over the gate: a user who pins a project expects that one to be read.
        let environment = ProcessInfo.processInfo.environment
        guard let project = environment["GOOGLE_CLOUD_PROJECT"] ?? environment["GOOGLE_CLOUD_PROJECT_ID"] ?? CodeAssist.parseGate(gateBody).project else {
            throw UsageError.nothingMetered("Gemini CLI has no Code Assist project for this account")
        }

        let quotaBody = String(data: try JSONSerialization.data(withJSONObject: ["project": project]), encoding: .utf8) ?? "{}"
        let (body, response) = try await CodeAssist.post(transport, CodeAssist.quota, token: credential.accessToken, body: quotaBody)
        Log.usage.debug("gemini: quota \(response.statusCode)")
        try check(response)
        let parsed = try GeminiUsage.parse(body)
        return ProviderReading(providerId: id, displayName: displayName, fidelity: .official, status: .live,
                               windows: parsed.windows, headlineId: parsed.headline)
    }

    private func check(_ response: HTTPURLResponse) throws {
        switch response.statusCode {
        case 200: return
        case 401, 403: throw UsageError.needsSignIn()
        case 429:
            let wait = RetryAfterHeader.from(response, now: now()) ?? Backoff.floor
            archive?.setBackoff(id, until: now().addingTimeInterval(wait))
            throw UsageError.rateLimited(wait)
        default: throw UsageError.badResponse(response.statusCode)
        }
    }
}
