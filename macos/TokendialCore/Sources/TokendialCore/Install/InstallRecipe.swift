import Foundation

/// How signing in works once the tool is there: a cli takes its sign-in command in the same terminal, an app
/// is signed into in its own window.
public enum InstallKind: String, Sendable {
    case cli
    case app
}

/// Where a tool can be found once it is installed.
public struct Detect: Equatable, Sendable {
    public var commands: [String]
    public var paths: [String]

    public init(commands: [String] = [], paths: [String] = []) {
        self.commands = commands
        self.paths = paths
    }

    public static let none = Detect()
}

public struct SignInStep: Equatable, Sendable {
    public var command: String
    /// Catalogue key of the sentence shown before the command runs.
    public var hint: String

    public init(command: String, hint: String) {
        self.command = command
        self.hint = hint
    }
}

/// One provider's recipe. The shared spec carries a Windows half as well; this core reads only its own, so
/// there is nothing here to pick between at runtime.
public struct InstallRecipe: Equatable, Sendable {
    public var providerId: String
    public var vendor: String
    public var docsUrl: String
    public var kind: InstallKind
    public var detect: Detect
    public var requires: [String]
    public var install: String
    public var signIn: SignInStep?
    public var downloadUrl: String?

    public init(providerId: String, vendor: String, docsUrl: String, kind: InstallKind, detect: Detect,
                requires: [String], install: String, signIn: SignInStep?, downloadUrl: String?) {
        self.providerId = providerId
        self.vendor = vendor
        self.docsUrl = docsUrl
        self.kind = kind
        self.detect = detect
        self.requires = requires
        self.install = install
        self.signIn = signIn
        self.downloadUrl = downloadUrl
    }

    public var needsBrew: Bool { requires.contains("brew") }
    public var needsNpm: Bool { requires.contains("npm") }
}
