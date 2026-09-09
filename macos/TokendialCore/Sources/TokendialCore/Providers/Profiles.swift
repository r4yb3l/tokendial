import Foundation

/// Which provider an id belongs to: claude-work is a Claude profile, codex-team a Codex one; any other id
/// is its own family. Marks, tints and copy collapse a profile onto the tool it is an account of.
public enum ProviderFamily {
    /// The provider ids the app knows, in the order the dock shows them.
    public static let order = ["claude", "codex", "copilot", "cursor", "antigravity", "gemini", "glm", "grok", "opencode"]

    public static func of(_ providerId: String) -> String {
        guard let dash = providerId.firstIndex(of: "-"), dash != providerId.startIndex else { return providerId }
        let head = String(providerId[providerId.startIndex..<dash])
        return order.contains(head) ? head : providerId
    }

    public static func isProfile(_ providerId: String) -> Bool { of(providerId) != providerId }
}
