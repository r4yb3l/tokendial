import Foundation

/// Finds an installed tool from its recipe: the locations the spec declares first, then the directories on
/// PATH. The declared paths lead on purpose. An app launched by launchd carries a PATH that usually holds
/// neither `/opt/homebrew/bin` nor `~/.local/bin`, so on macOS the spec's list is the reliable half and the
/// PATH scan is the fallback — the opposite weighting to the registry PATHs Windows has to consult.
public struct ToolLocator: Sendable {
    private let path: String
    private let home: URL

    public init(path: String? = nil, home: URL? = nil) {
        self.path = path ?? ProcessInfo.processInfo.environment["PATH"] ?? ""
        self.home = home ?? Paths.home
    }

    public func isInstalled(_ detect: Detect) -> Bool { resolve(detect) != nil }

    /// The first location where the tool exists, or nil. A `.app` is a directory rather than a file, so a
    /// declared path counts either way; a name on PATH has to be a file.
    public func resolve(_ detect: Detect) -> String? {
        for pattern in detect.paths {
            for candidate in expand(pattern) where FileManager.default.fileExists(atPath: candidate) {
                return candidate
            }
        }
        guard !detect.commands.isEmpty else { return nil }
        for directory in directories() {
            for name in detect.commands {
                let candidate = (directory as NSString).appendingPathComponent(name)
                var isDirectory: ObjCBool = false
                if FileManager.default.fileExists(atPath: candidate, isDirectory: &isDirectory), !isDirectory.boolValue {
                    return candidate
                }
            }
        }
        return nil
    }

    private func directories() -> [String] {
        var seen = Set<String>()
        var ordered: [String] = []
        for raw in path.split(separator: ":") {
            let directory = raw.trimmingCharacters(in: .whitespaces)
            guard !directory.isEmpty, seen.insert(directory).inserted else { continue }
            ordered.append(directory)
        }
        return ordered
    }

    /// `~` expanded, and a segment holding `*` standing for every entry that matches it, so a versioned
    /// directory still resolves.
    public func expand(_ pattern: String) -> [String] {
        var text = pattern
        if text == "~" {
            text = home.path
        } else if text.hasPrefix("~/") {
            text = home.path + text.dropFirst(1)
        }
        guard text.contains("*") else { return [text] }
        let segments = text.split(separator: "/", omittingEmptySubsequences: false).map(String.init)
        guard let star = segments.firstIndex(where: { $0.contains("*") }) else { return [text] }
        let parent = segments[..<star].joined(separator: "/")
        guard !parent.isEmpty else { return [] }
        let rest = segments[(star + 1)...].joined(separator: "/")
        let entries = (try? FileManager.default.contentsOfDirectory(atPath: parent)) ?? []
        return entries.filter { fnmatch(segments[star], $0, 0) == 0 }.sorted().flatMap { match -> [String] in
            let matched = parent + "/" + match
            return rest.isEmpty ? [matched] : expand(matched + "/" + rest)
        }
    }
}
