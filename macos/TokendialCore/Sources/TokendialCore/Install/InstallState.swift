import Foundation

/// How far a tool has got. The order carries meaning: the step strip asks whether the state has reached a
/// step, so the cases must stay in this sequence.
public enum InstallState: Int, Comparable, Sendable {
    case notInstalled
    case installed
    case signedIn
    case connected

    public static func < (a: InstallState, b: InstallState) -> Bool { a.rawValue < b.rawValue }

    /// Connected is only read once the tool is signed in: ticking a provider that cannot be read yet is not
    /// progress, it is a preference waiting for a credential.
    public static func of(installed: Bool, signedIn: Bool, connected: Bool) -> InstallState {
        guard signedIn else { return installed ? .installed : .notInstalled }
        return connected ? .connected : .signedIn
    }
}
