import AppKit
import TokendialCore

/// docs/design/tokens.md as AppKit values. Every number in points.
enum Theme {
    static let surface = NSColor(srgbRed: 16 / 255, green: 17 / 255, blue: 20 / 255, alpha: 0.50)
    static let cardSurface = NSColor(srgbRed: 16 / 255, green: 17 / 255, blue: 20 / 255, alpha: 0.92)
    static let surfaceEdge = NSColor(white: 1, alpha: 0.08)
    static let track = NSColor(white: 1, alpha: 0.16)
    static let ample = NSColor(srgbRed: 0x34 / 255, green: 0xD3 / 255, blue: 0x99 / 255, alpha: 1)
    static let watch = NSColor(srgbRed: 0xFB / 255, green: 0xBF / 255, blue: 0x24 / 255, alpha: 1)
    static let critical = NSColor(srgbRed: 0xFB / 255, green: 0x71 / 255, blue: 0x85 / 255, alpha: 1)
    static let working = NSColor(srgbRed: 0xF5 / 255, green: 0xF5 / 255, blue: 0xF7 / 255, alpha: 1)
    static let waiting = watch
    static let textPrimary = NSColor(srgbRed: 0xF5 / 255, green: 0xF5 / 255, blue: 0xF7 / 255, alpha: 1)
    static let textSecondary = NSColor(srgbRed: 0x9A / 255, green: 0x9D / 255, blue: 0xA6 / 255, alpha: 1)
    static let textDisabled = NSColor(srgbRed: 0x5C / 255, green: 0x5F / 255, blue: 0x68 / 255, alpha: 1)
    static let hairline = NSColor(white: 1, alpha: 0.11)

    static let compactHeight: CGFloat = 28
    static let compactPadding: CGFloat = 14
    static let compactSpacing: CGFloat = 10
    static let compactDial: CGFloat = 18
    static let compactStroke: CGFloat = 3
    static let expandedHeight: CGFloat = 132
    static let expandedPadding: CGFloat = 20
    static let cellWidth: CGFloat = 96
    static let expandedDial: CGFloat = 56
    static let expandedStroke: CGFloat = 6
    static let activityDial: CGFloat = 40
    static let activityStroke: CGFloat = 2
    static let compactRadius: CGFloat = 14
    static let expandedRadius: CGFloat = 20
    static let hotZone: CGFloat = 24
    static let cardWidth: CGFloat = 280
    static let cardRadius: CGFloat = 16
    static let cardPadding: CGFloat = 14

    static func color(_ band: Band?) -> NSColor {
        switch band { case .ample?: return ample; case .watch?: return watch; case .critical?: return critical; case nil: return textDisabled }
    }

    static func color(fraction: Double?) -> NSColor { fraction.map { color(Band.of($0)) } ?? textDisabled }

    static func font(_ size: CGFloat, weight: NSFont.Weight = .regular) -> NSFont {
        NSFont.monospacedDigitSystemFont(ofSize: size, weight: weight)
    }

    static var reduceMotion: Bool { NSWorkspace.shared.accessibilityDisplayShouldReduceMotion }

    static func compactWidth(_ n: Int) -> CGFloat { n == 0 ? 2 * compactPadding + 40 : 2 * compactPadding + CGFloat(n) * compactDial + CGFloat(n - 1) * compactSpacing }
    static func expandedWidth(_ n: Int) -> CGFloat { max(2 * expandedPadding + CGFloat(max(n, 1)) * cellWidth, cardWidth + 2 * expandedPadding) }
}

/// Springs from the tokens as Core Animation springs.
struct Spring {
    let response: Double
    let damping: Double

    static let expand = Spring(response: 0.38, damping: 0.80)
    static let contents = Spring(response: 0.32, damping: 0.84)
    static let reading = Spring(response: 0.80, damping: 0.90)
    static let glide = Spring(response: 0.45, damping: 0.86)

    var omega: Double { 2 * .pi / response }
    var settle: TimeInterval { log(1000) / (damping * omega) }

    func animation(_ keyPath: String, from: Any?, to: Any) -> CAAnimation {
        let spring = CASpringAnimation(keyPath: keyPath)
        spring.mass = 1
        spring.stiffness = omega * omega
        spring.damping = 2 * damping * omega
        spring.fromValue = from
        spring.toValue = to
        spring.duration = spring.settlingDuration
        spring.fillMode = .forwards
        spring.isRemovedOnCompletion = false
        return spring
    }
}
