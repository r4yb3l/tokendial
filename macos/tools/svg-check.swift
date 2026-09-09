import AppKit

// Does AppKit load the normalised marks as SVG, and can they be tinted as templates?
let directory = URL(fileURLWithPath: CommandLine.arguments[1])
for name in ["claude", "codex", "copilot", "cursor", "antigravity", "glm", "grok", "gemini"] {
    let url = directory.appendingPathComponent("\(name).svg")
    if let image = NSImage(contentsOf: url) {
        image.isTemplate = true
        print(name, "loaded", image.size, "template:", image.isTemplate, "reps:", image.representations.count)
    } else {
        print(name, "FAILED to load")
    }
}
