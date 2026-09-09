import AppKit
import SwiftUI
import TokendialCore

/// docs/design/tokens.md as AppKit values. Every number in points; every colour comes from the palette in use.
/// The surfaces are more opaque than the Windows ones on purpose: the dock paints a flat translucent colour
/// with no backdrop blur behind it, and a Retina screen shows every pixel of the wallpaper through it.
enum Theme {
    /// One look for the dock and the card: surfaces, text inks and the band colours.
    struct Palette {
        let surface: NSColor
        let surfaceEdge: NSColor
        let cardSurface: NSColor
        let track: NSColor
        let ample: NSColor
        let watch: NSColor
        let critical: NSColor
        let working: NSColor
        let textPrimary: NSColor
        let textSecondary: NSColor
        let textDisabled: NSColor
        let hairline: NSColor
    }

    static let darkPalette = Palette(
        surface: rgba(16, 17, 20, 0.86), surfaceEdge: white(1, 0.14), cardSurface: rgba(16, 17, 20, 0.97), track: white(1, 0.16),
        ample: rgb(0x34, 0xD3, 0x99), watch: rgb(0xFB, 0xBF, 0x24), critical: rgb(0xFB, 0x71, 0x85), working: rgb(0xF5, 0xF5, 0xF7),
        textPrimary: rgb(0xF5, 0xF5, 0xF7), textSecondary: rgb(0x9A, 0x9D, 0xA6), textDisabled: rgb(0x5C, 0x5F, 0x68), hairline: white(1, 0.11))

    static let lightPalette = Palette(
        surface: rgba(248, 249, 251, 0.95), surfaceEdge: rgba(15, 23, 42, 0.16), cardSurface: rgba(252, 252, 253, 0.99), track: rgba(15, 23, 42, 0.12),
        ample: rgb(0x10, 0xB9, 0x81), watch: rgb(0xF5, 0x9E, 0x0B), critical: rgb(0xF4, 0x3F, 0x5E), working: rgb(0x1B, 0x1F, 0x27),
        textPrimary: rgb(0x1B, 0x1F, 0x27), textSecondary: rgb(0x5B, 0x62, 0x70), textDisabled: rgb(0xA3, 0xA9, 0xB4), hairline: rgba(15, 23, 42, 0.10))

    private(set) static var dark = true
    private static var palette = darkPalette

    /// Switch the look. Views built afterwards pick it up; live ones are repainted by their windows.
    static func use(dark isDark: Bool) {
        dark = isDark
        palette = isDark ? darkPalette : lightPalette
    }

    static var surface: NSColor { palette.surface }
    static var cardSurface: NSColor { palette.cardSurface }
    static var surfaceEdge: NSColor { palette.surfaceEdge }
    static var track: NSColor { palette.track }
    static var ample: NSColor { palette.ample }
    static var watch: NSColor { palette.watch }
    static var critical: NSColor { palette.critical }
    static var working: NSColor { palette.working }
    static var waiting: NSColor { palette.watch }
    static var textPrimary: NSColor { palette.textPrimary }
    static var textSecondary: NSColor { palette.textSecondary }
    static var textDisabled: NSColor { palette.textDisabled }
    static var hairline: NSColor { palette.hairline }

    static let compactHeight: CGFloat = 28
    static let compactPadding: CGFloat = 14
    static let compactSpacing: CGFloat = 10
    static let compactDial: CGFloat = 18
    static let compactStroke: CGFloat = 3
    static let expandedHeight: CGFloat = 132
    static let expandedPadding: CGFloat = 20
    static let cellWidth: CGFloat = 96
    static let cellHeight: CGFloat = 96
    static let cellGap: CGFloat = 14
    static let expandedDial: CGFloat = 56
    static let expandedStroke: CGFloat = 6
    static let activityDial: CGFloat = 40
    static let activityStroke: CGFloat = 2
    static let slant: CGFloat = 14
    /// The inset above the cells in a dock along the top or bottom edge.
    static let expandedLead: CGFloat = 10
    /// The inset at both ends of a dock down a side, where the cells run along the long axis.
    static let sideLead: CGFloat = 18
    /// The one line of session copy under the cells, plus the gap above it.
    static let sessionsLine: CGFloat = 18
    static let compactMark: CGFloat = 11
    static let expandedMark: CGFloat = 13
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

    /// The dock's size along the edge it hangs from, and its depth across it.
    static func compactLength(_ n: Int) -> CGFloat { n == 0 ? 2 * compactPadding + 40 : 2 * compactPadding + CGFloat(n) * compactDial + CGFloat(n - 1) * compactSpacing }

    private static func rgb(_ r: Int, _ g: Int, _ b: Int) -> NSColor {
        NSColor(srgbRed: CGFloat(r) / 255, green: CGFloat(g) / 255, blue: CGFloat(b) / 255, alpha: 1)
    }

    private static func rgba(_ r: Int, _ g: Int, _ b: Int, _ alpha: CGFloat) -> NSColor {
        NSColor(srgbRed: CGFloat(r) / 255, green: CGFloat(g) / 255, blue: CGFloat(b) / 255, alpha: alpha)
    }

    private static func white(_ level: CGFloat, _ alpha: CGFloat) -> NSColor { NSColor(white: level, alpha: alpha) }
}

/// The same tokens as SwiftUI colours, for the settings and welcome windows.
extension Theme {
    enum UI {
        static var surface: Color { Color(nsColor: Theme.surface) }
        static var cardSurface: Color { Color(nsColor: Theme.cardSurface) }
        static var surfaceEdge: Color { Color(nsColor: Theme.surfaceEdge) }
        static var track: Color { Color(nsColor: Theme.track) }
        static var ample: Color { Color(nsColor: Theme.ample) }
        static var watch: Color { Color(nsColor: Theme.watch) }
        static var critical: Color { Color(nsColor: Theme.critical) }
        static var textPrimary: Color { Color(nsColor: Theme.textPrimary) }
        static var textSecondary: Color { Color(nsColor: Theme.textSecondary) }
        static var textDisabled: Color { Color(nsColor: Theme.textDisabled) }
        static var hairline: Color { Color(nsColor: Theme.hairline) }
    }
}

/// The expanded dock's box: how the cells are arranged and how big that makes it. The length is exactly
/// what the cells and the session line need, so nothing is left over at the far end. A dock down a side can
/// ask for more length than the screen has, and then it wraps into as many columns as it takes and grows
/// across instead: nothing is pushed off screen and nothing has to be scrolled to.
struct DockLayout {
    let cellsPerColumn: Int
    let columns: Int
    let along: CGFloat
    let across: CGFloat

    /// `available` is the room along the edge the dock hangs from.
    init(count: Int, vertical: Bool, available: CGFloat) {
        let cells = max(count, 1)
        let ends = 2 * Theme.sideLead + Theme.sessionsLine
        guard vertical else {
            cellsPerColumn = cells
            columns = 1
            along = max(2 * Theme.expandedPadding + CGFloat(cells) * Theme.cellWidth, Theme.cardWidth + 2 * Theme.expandedPadding)
            across = Theme.expandedHeight
            return
        }
        let room = max(Theme.cellHeight, available - 24 - ends)
        let fit = max(1, Int((room + Theme.cellGap) / (Theme.cellHeight + Theme.cellGap)))
        cellsPerColumn = min(cells, fit)
        columns = Int(ceil(Double(cells) / Double(cellsPerColumn)))
        along = ends + CGFloat(cellsPerColumn) * Theme.cellHeight + CGFloat(cellsPerColumn - 1) * Theme.cellGap
        across = CGFloat(columns) * Theme.cellWidth + 2 * Theme.expandedPadding
    }
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
