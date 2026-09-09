import AppKit
import TokendialCore

/// A provider's mark, from the same normalised assets the Windows build embeds. AppKit reads SVG natively,
/// so the marks need no path parsing here; OpenCode ships as a raster because it has no one-colour vector.
enum Marks {
    private static let tints: [String: NSColor] = [
        "claude": rgb(0xD9, 0x77, 0x57),
        "codex": rgb(0x38, 0xBD, 0xF8),
        "copilot": rgb(0xA7, 0x8B, 0xFA),
        "cursor": rgb(0xE2, 0xE8, 0xF0),
        "antigravity": rgb(0x60, 0xA5, 0xFA),
        "gemini": rgb(0x4E, 0x8D, 0xF5),
        "glm": rgb(0x3B, 0x82, 0xF6),
        "grok": rgb(0xF1, 0xF5, 0xF9),
        "opencode": rgb(0xFB, 0xBF, 0x24)
    ]

    private static var cache: [String: NSImage?] = [:]
    private static let lock = NSLock()

    /// The brand colour a provider's tile takes once it is connected; a profile shares its tool's.
    static func tint(_ providerId: String) -> NSColor { tints[ProviderFamily.of(providerId)] ?? rgb(0xCB, 0xD5, 0xE1) }

    /// The mark as a template image, or nil when the bundle carries none for this provider.
    static func image(_ providerId: String) -> NSImage? {
        let family = ProviderFamily.of(providerId)
        lock.lock()
        defer { lock.unlock() }
        if let cached = cache[family] { return cached }
        let image = load(family)
        cache[family] = image
        return image
    }

    private static func load(_ family: String) -> NSImage? {
        let url = Bundle.main.url(forResource: family, withExtension: "svg")
            ?? Bundle.main.url(forResource: family, withExtension: "png")
        guard let url, let image = NSImage(contentsOf: url) else { return nil }
        image.isTemplate = true
        return image
    }

    private static func rgb(_ r: Int, _ g: Int, _ b: Int) -> NSColor {
        NSColor(srgbRed: CGFloat(r) / 255, green: CGFloat(g) / 255, blue: CGFloat(b) / 255, alpha: 1)
    }
}

/// Draws a mark in one colour, scaled into its bounds, keeping its aspect. The image is the layer's mask and
/// the colour is the layer's background, so a colour change is animatable the way the dial's arcs are.
final class MarkView: NSView {
    private let maskLayer = CALayer()
    private var pulsing = false

    let hasMark: Bool

    init(providerId: String, size: CGFloat) {
        let image = Marks.image(providerId)
        hasMark = image != nil
        super.init(frame: NSRect(x: 0, y: 0, width: size, height: size))
        translatesAutoresizingMaskIntoConstraints = false
        widthAnchor.constraint(equalToConstant: size).isActive = true
        heightAnchor.constraint(equalToConstant: size).isActive = true
        wantsLayer = true
        layer?.backgroundColor = Theme.textSecondary.cgColor
        guard let image else { return }
        maskLayer.contents = image.cgImage
        maskLayer.contentsGravity = .resizeAspect
        layer?.mask = maskLayer
    }

    required init?(coder: NSCoder) { fatalError() }

    override func layout() {
        super.layout()
        maskLayer.frame = bounds
    }

    /// A heartbeat between two colours while an agent waits on the user; a steady fill otherwise.
    func pulse(_ on: Bool, bright: NSColor, dim: NSColor, steady: NSColor) {
        guard hasMark else { return }
        if on {
            if pulsing { return }
            pulsing = true
            layer?.backgroundColor = bright.cgColor
            guard !Theme.reduceMotion else { return }
            let beat = CABasicAnimation(keyPath: "backgroundColor")
            beat.fromValue = bright.cgColor
            beat.toValue = dim.cgColor
            beat.duration = 0.55
            beat.autoreverses = true
            beat.repeatCount = .infinity
            layer?.add(beat, forKey: "pulse")
        } else {
            pulsing = false
            layer?.removeAnimation(forKey: "pulse")
            layer?.backgroundColor = steady.cgColor
        }
    }

    /// Repaint after a theme switch when the mark is not pulsing.
    func retint(_ colour: NSColor) {
        guard !pulsing else { return }
        layer?.backgroundColor = colour.cgColor
    }
}

private extension NSImage {
    /// The mask needs a CGImage; SVG-backed images rasterise at their natural size, which is plenty for a glyph.
    var cgImage: CGImage? {
        var rect = CGRect(x: 0, y: 0, width: size.width, height: size.height)
        return cgImage(forProposedRect: &rect, context: nil, hints: nil)
    }
}
