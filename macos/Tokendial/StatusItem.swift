import AppKit
import ServiceManagement
import TokendialCore

/// The menu bar item: a small dial coloured by the worst band, and the menu that reaches settings and quit.
final class StatusItemController: NSObject {
    private let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
    private let menu = NSMenu()
    private let showItem = NSMenuItem(title: "", action: #selector(show), keyEquivalent: "")
    private let refreshItem = NSMenuItem(title: "", action: #selector(refresh), keyEquivalent: "r")
    private let settingsItem = NSMenuItem(title: "", action: #selector(settings), keyEquivalent: ",")
    private let testItem = NSMenuItem(title: "", action: #selector(testAlert), keyEquivalent: "")
    private let quitItem = NSMenuItem(title: "", action: #selector(quit), keyEquivalent: "q")

    var onShow: (() -> Void)?
    var onRefresh: (() -> Void)?
    var onSettings: (() -> Void)?
    var onTestAlert: (() -> Void)?
    var onQuit: (() -> Void)?

    override init() {
        super.init()
        for entry in [showItem, refreshItem] { entry.target = self; menu.addItem(entry) }
        menu.addItem(.separator())
        for entry in [settingsItem, testItem] { entry.target = self; menu.addItem(entry) }
        menu.addItem(.separator())
        quitItem.target = self
        menu.addItem(quitItem)
        menu.delegate = self
        item.button?.image = Self.render(nil)
        item.button?.target = self
        item.button?.action = #selector(clicked)
        item.button?.sendAction(on: [.leftMouseUp, .rightMouseUp])
    }

    func update(worst: Double?, tooltip: String) {
        item.button?.image = Self.render(worst)
        item.button?.toolTip = tooltip
    }

    /// The same gestures as the Windows notification icon: a click shows the panel, a double click opens
    /// settings, and a right or control click opens the menu.
    @objc private func clicked() {
        guard let event = NSApp.currentEvent else { return }
        if event.type == .rightMouseUp || event.modifierFlags.contains(.control) { popUpMenu(); return }
        if event.clickCount > 1 { onSettings?() } else { onShow?() }
    }

    /// The menu is popped by hand rather than handed to the status item: the item's own menu would swallow
    /// every click, including the left one that shows the panel.
    private func popUpMenu() {
        guard let button = item.button else { return }
        menu.popUp(positioning: nil, at: NSPoint(x: 0, y: -4), in: button)
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
}

extension StatusItemController: NSMenuDelegate {
    /// Titles are read when the menu opens, not when it is built: the item exists before the catalogue is
    /// loaded, so titling it in the initialiser left every entry showing its own key.
    func menuNeedsUpdate(_ menu: NSMenu) {
        showItem.title = Strings.t("tray.show")
        refreshItem.title = Strings.t("tray.refresh")
        settingsItem.title = Strings.t("tray.settings")
        testItem.title = Strings.t("tray.testAlert")
        quitItem.title = Strings.t("tray.quit")
    }
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
