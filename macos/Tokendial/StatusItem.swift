import AppKit
import ServiceManagement
import TokendialCore

/// The menu bar item: a small dial coloured by the worst band, and the menu that reaches settings and quit.
final class StatusItemController: NSObject {
    private let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
    private let launchItem = NSMenuItem(title: "Launch at login", action: #selector(toggleLaunch), keyEquivalent: "")

    var onShow: (() -> Void)?
    var onRefresh: (() -> Void)?
    var onSettings: (() -> Void)?
    var onTestAlert: (() -> Void)?
    var onQuit: (() -> Void)?
    var onLaunchAtLogin: ((Bool) -> Void)?

    override init() {
        super.init()
        let menu = NSMenu()
        menu.addItem(withTitle: "Show panel", action: #selector(show), keyEquivalent: "").target = self
        menu.addItem(withTitle: "Refresh now", action: #selector(refresh), keyEquivalent: "r").target = self
        menu.addItem(.separator())
        menu.addItem(withTitle: "Settings…", action: #selector(settings), keyEquivalent: ",").target = self
        menu.addItem(withTitle: "Send a test alert", action: #selector(testAlert), keyEquivalent: "").target = self
        launchItem.target = self
        menu.addItem(launchItem)
        menu.addItem(.separator())
        menu.addItem(withTitle: "Quit Tokendial", action: #selector(quit), keyEquivalent: "q").target = self
        item.menu = menu
        update(worst: nil, tooltip: "Tokendial", launchAtLogin: false)
    }

    func update(worst: Double?, tooltip: String, launchAtLogin: Bool) {
        item.button?.image = Self.render(worst)
        item.button?.toolTip = tooltip
        launchItem.state = launchAtLogin ? .on : .off
    }

    private static func render(_ fraction: Double?) -> NSImage {
        let size = NSSize(width: 18, height: 18)
        let image = NSImage(size: size, flipped: false) { rect in
            let inset = rect.insetBy(dx: 2.5, dy: 2.5)
            let radius = inset.width / 2
            let centre = NSPoint(x: rect.midX, y: rect.midY)
            let track = NSBezierPath()
            track.appendArc(withCenter: centre, radius: radius, startAngle: 210, endAngle: -30, clockwise: true)
            track.lineWidth = 2.6
            track.lineCapStyle = .round
            NSColor.secondaryLabelColor.withAlphaComponent(0.55).setStroke()
            track.stroke()
            if let fraction, fraction > 0.01 {
                let fill = NSBezierPath()
                fill.appendArc(withCenter: centre, radius: radius, startAngle: 210, endAngle: 210 - 240 * min(1, max(0, fraction)), clockwise: true)
                fill.lineWidth = 2.6
                fill.lineCapStyle = .round
                Theme.color(Band.of(fraction)).setStroke()
                fill.stroke()
            }
            return true
        }
        image.isTemplate = false
        return image
    }

    @objc private func show() { onShow?() }
    @objc private func refresh() { onRefresh?() }
    @objc private func settings() { onSettings?() }
    @objc private func testAlert() { onTestAlert?() }
    @objc private func quit() { onQuit?() }
    @objc private func toggleLaunch() { onLaunchAtLogin?(launchItem.state != .on) }
}

/// Launch at login through SMAppService; the system shows it in Login Items.
enum LaunchAtLogin {
    static var isEnabled: Bool { SMAppService.mainApp.status == .enabled }

    static func set(_ on: Bool) {
        do {
            if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
        } catch { Log.ui.error("launch at login: \(error.localizedDescription)") }
    }
}
