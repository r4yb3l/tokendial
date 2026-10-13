import Foundation

/// Finds what a session's process lives inside: a terminal, an editor or a desktop app, by walking up its parents.
public enum Ancestry {
    /// The first process, starting with `pid` itself, for which `pick` answers.
    /// Process ids are recycled, so a parent can point back down the chain; a visited set ends the walk there.
    public static func firstAnswer<T>(_ pid: Int32, parentOf: (Int32) -> Int32?, pick: (Int32) -> T?) -> T? {
        var seen = Set<Int32>()
        var current: Int32? = pid
        while let id = current, id > 0, seen.insert(id).inserted {
            if let found = pick(id) { return found }
            current = parentOf(id)
        }
        return nil
    }
}
