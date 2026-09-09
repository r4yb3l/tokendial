import Foundation

public enum InstallAction: Sendable {
    case install
    case signIn
}

/// Composes the script the terminal runs, and writes it where the terminal can open it.
///
/// Two rules this file keeps. The words are deliberately English: a console cannot shape Arabic, and the user
/// has already read the sentence in their own language on the sheet before pressing Run. And nothing is
/// fetched: every command comes from the provider's own spec, the only exceptions being the two package
/// managers below, which are part of this fixed template and are pinned by a test.
public enum InstallScript {
    /// Homebrew's own published line, and how a fresh install is put on PATH for the rest of the script.
    public static let brewInstall = "/bin/bash -c \"$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)\""
    public static let brewShellEnv = "eval \"$(/opt/homebrew/bin/brew shellenv)\""
    public static let nodeInstall = "brew install node"

    public static func compose(_ name: String, _ recipe: InstallRecipe, action: InstallAction, marker: URL) -> String {
        var lines = ["#!/bin/sh"]
        lines.append(say("\u{1B}]0;%s\u{7}", "Tokendial · \(name)"))
        lines.append(say("\u{1B}[36m%s\u{1B}[0m\\n", name))
        lines.append(say("%s\\n\\n", "Installer from \(recipe.vendor) · \(recipe.docsUrl)"))
        // The app cannot be told when the terminal closes, so the script says so itself, on any way out.
        lines.append("finish() { : > \(quote(marker.path)) ; }")
        lines.append("trap finish EXIT")

        if action == .install {
            lines += managers(for: recipe)
            lines.append(run(recipe.install))
            lines.append("if [ $? -ne 0 ]; then")
            lines.append("  " + say("\u{1B}[31m%s\u{1B}[0m\\n", "That did not finish. The message above is the installer's."))
            lines.append("  exit 1")
            lines.append("fi")
            lines.append(say("\u{1B}[32m%s\u{1B}[0m\\n", "Installed."))
        }

        if recipe.kind == .app {
            lines.append(say("%s\\n", english("install.hint.app", ["name": name])))
        } else if let step = recipe.signIn {
            lines.append(say("%s\\n", english(step.hint)))
            lines.append(run(step.command))
        }

        lines.append(say("\\n%s\\n", "Done. You can close this window."))
        return lines.joined(separator: "\n") + "\n"
    }

    /// The package manager a line needs, offered before the line itself. Node is installed with Homebrew, so
    /// a Mac without either gets both, in that order.
    private static func managers(for recipe: InstallRecipe) -> [String] {
        var lines: [String] = []
        if recipe.needsBrew || recipe.needsNpm {
            lines.append("if ! command -v brew >/dev/null 2>&1; then")
            lines.append("  " + run(brewInstall))
            lines.append("  [ -x /opt/homebrew/bin/brew ] && \(brewShellEnv)")
            lines.append("  if ! command -v brew >/dev/null 2>&1; then")
            lines.append("    " + say("\u{1B}[31m%s\u{1B}[0m\\n", "Homebrew is still missing, so the install cannot go on."))
            lines.append("    exit 1")
            lines.append("  fi")
            lines.append("fi")
        }
        if recipe.needsNpm {
            lines.append("if ! command -v npm >/dev/null 2>&1; then")
            lines.append("  " + run(nodeInstall))
            lines.append("  if ! command -v npm >/dev/null 2>&1; then")
            lines.append("    " + say("\u{1B}[31m%s\u{1B}[0m\\n", "Node is still missing, so the install cannot go on."))
            lines.append("    exit 1")
            lines.append("  fi")
            lines.append("fi")
        }
        return lines
    }

    /// Echo the command, then hand it to a child shell so that a command ending in `exit` cannot take the
    /// script down with it.
    private static func run(_ command: String) -> String {
        say("\u{1B}[33m> %s\u{1B}[0m\\n", command) + "\n/bin/sh -c \(quote(command))"
    }

    /// printf with the text as an argument, never inside the format, so a `%` in a command cannot be read as
    /// a placeholder.
    private static func say(_ format: String, _ text: String) -> String {
        "printf \(quote(format)) \(quote(text))"
    }

    /// A POSIX single-quoted literal: the quote is the one character that has to leave and come back.
    public static func quote(_ text: String) -> String {
        "'" + text.replacingOccurrences(of: "'", with: "'\\''") + "'"
    }

    private static func english(_ key: String, _ args: [String: String] = [:]) -> String {
        var text = Strings.keys("en")[key] as? String ?? key
        for (name, value) in args { text = text.replacingOccurrences(of: "{\(name)}", with: value) }
        return text
    }

    /// The script and the marker it writes, side by side under Application Support.
    public static func paths(_ directory: URL, providerId: String) -> (script: URL, marker: URL) {
        (directory.appendingPathComponent("\(providerId).command"),
         directory.appendingPathComponent("\(providerId).done"))
    }

    @discardableResult
    public static func write(_ directory: URL, providerId: String, content: String) throws -> URL {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let script = paths(directory, providerId: providerId).script
        try content.write(to: script, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: script.path)
        return script
    }
}
