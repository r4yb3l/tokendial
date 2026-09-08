import Foundation

public enum UsageErrorKind: Sendable {
    /// No usable credential: the owning tool has to sign in.
    case needsSignIn
    /// The credential aged out; the owning tool refreshes it next time it runs. The last reading stays, dimmed.
    case credentialExpired
    /// Told to slow down.
    case rateLimited
    /// The account is readable and meters nothing.
    case nothingMetered
    /// The endpoint answered with something we do not understand.
    case badResponse
}

public struct UsageError: Error, Equatable, Sendable {
    public let kind: UsageErrorKind
    public let message: String
    public var status: Int = 0
    public var retryAfter: TimeInterval = 0

    public static func needsSignIn() -> UsageError { UsageError(kind: .needsSignIn, message: "needs sign-in") }
    public static func credentialExpired() -> UsageError { UsageError(kind: .credentialExpired, message: "credential expired") }
    public static func rateLimited(_ retryAfter: TimeInterval) -> UsageError { UsageError(kind: .rateLimited, message: "rate limited for \(Int(retryAfter))s", retryAfter: retryAfter) }
    public static func nothingMetered(_ why: String) -> UsageError { UsageError(kind: .nothingMetered, message: why) }
    public static func badResponse(_ status: Int) -> UsageError { UsageError(kind: .badResponse, message: "bad response (\(status))", status: status) }
}

/// Whose account a provider is reading, for the settings row.
public struct ProviderAccount: Equatable, Sendable {
    public var label: String?
    public var plan: String?
    public var source: String
    public var manageURL: URL?

    public init(label: String?, plan: String?, source: String, manageURL: URL?) {
        self.label = label
        self.plan = plan
        self.source = source
        self.manageURL = manageURL
    }

    public var summary: String {
        var parts: [String] = []
        if let label { parts.append(label) }
        if let plan { parts.append(plan.lowercased().capitalized) }
        parts.append(Strings.t("copy.via", ["source": source]))
        return parts.joined(separator: " · ")
    }
}

/// What the user can do when a provider has no credential: open the owning app, or read a sentence.
public enum SignInRoute: Equatable, Sendable {
    case openApp(appKey: String, name: String)
    /// A catalogue key and its placeholders; resolved in the current language when shown.
    case guidance(String, [String: String] = [:])

    public var explanation: String {
        switch self {
        case .openApp(_, let name): return Strings.t("signin.openApp", ["name": name])
        case .guidance(let key, let args): return Strings.t(key, args)
        }
    }
}

/// One assistant's usage source. Adapters read a credential another tool holds and call that tool's own endpoint.
public protocol UsageProvider: AnyObject {
    var id: String { get }
    var displayName: String { get }
    var signIn: SignInRoute { get }
    func read() async throws -> ProviderReading
    func account() -> ProviderAccount?
    func forgetCredential()
}

public extension UsageProvider {
    func forgetCredential() {}
}

/// Launches the desktop app that owns a credential, when it is installed.
public protocol AppLauncher {
    func isInstalled(_ appKey: String) -> Bool
    func open(_ appKey: String) -> Bool
}

public struct ProviderSummary: Sendable {
    public var id: String
    public var name: String
    public var account: ProviderAccount?
    public var signIn: SignInRoute
    public var connected: Bool
}

/// A parsed response: the windows and which one the dial means.
public struct Parsed: Equatable, Sendable {
    public var windows: [UsageWindow]
    public var headline: String?
    public var plan: String?

    public init(windows: [UsageWindow], headline: String?, plan: String? = nil) {
        self.windows = windows
        self.headline = headline
        self.plan = plan
    }
}

public enum Paths {
    public static var home: URL { FileManager.default.homeDirectoryForCurrentUser }
    public static func under(_ parts: String...) -> URL { parts.reduce(home) { $0.appendingPathComponent($1) } }
    public static var appSupport: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("Tokendial")
    }
}
