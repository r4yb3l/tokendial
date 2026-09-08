import Foundation
import XCTest

/// Finds the shared docs/ directory above this source file: macos/TokendialCore/Tests/TokendialCoreTests/Docs.swift → repo root.
enum Docs {
    static func path(_ parts: String...) throws -> URL {
        var dir = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        for _ in 0..<8 {
            let candidate = dir.appendingPathComponent("docs/providers")
            if FileManager.default.fileExists(atPath: candidate.path) {
                return parts.reduce(dir.appendingPathComponent("docs")) { $0.appendingPathComponent($1) }
            }
            dir = dir.deletingLastPathComponent()
        }
        throw XCTSkip("docs/ not found above \(#filePath)")
    }

    static func files(_ directory: URL, recursive: Bool = false) throws -> [URL] {
        let fm = FileManager.default
        if recursive {
            guard let enumerator = fm.enumerator(at: directory, includingPropertiesForKeys: nil) else { return [] }
            return enumerator.compactMap { $0 as? URL }.filter { $0.pathExtension == "json" }.sorted { $0.path < $1.path }
        }
        return try fm.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil).filter { $0.pathExtension == "json" }.sorted { $0.path < $1.path }
    }
}
