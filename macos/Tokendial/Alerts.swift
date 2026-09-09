import AppKit
import UserNotifications
import TokendialCore

/// Titles and bodies shared by the notification centre and the banners.
enum AlertCopy {
    static func compose(_ alert: Alert, reading: ProviderReading?, activity: Activity?, now: Date) -> (title: String, body: String) {
        let name = reading?.displayName ?? humanize(alert.provider)
        let window = alert.window.flatMap { id in reading?.windows.first { $0.id == id } }
        let windowLabel = window.map { Strings.label($0.label) } ?? Strings.t("alert.usage")
        let reset = (window?.resetsAt ?? alert.resetsAt).map { Copy.reset($0, now: now) }
        switch alert.kind {
        case .threshold:
            return (Strings.t("alert.threshold.title", ["name": name, "pct": alert.pct ?? 0]), join(windowLabel, reset))
        case .limit:
            return (Strings.t("alert.limit.title", ["name": name]), join(windowLabel, reset ?? Strings.t("alert.limit.waiting")))
        case .resetSoon:
            return (Strings.t("alert.resetSoon.title", ["name": name]),
                    join(windowLabel, reset, alert.pct.map { Strings.t("alert.resetSoon.used", ["pct": $0]) }))
        case .resetDone:
            return (Strings.t("alert.resetDone.title", ["name": name]), Strings.t("alert.resetDone.body", ["window": windowLabel]))
        case .waiting:
            let session = activity?.sessions.first { $0.id == alert.sessionId }
            return (Strings.t("alert.waiting.title", ["name": name]),
                    session.map { join($0.location, $0.waitingFor) } ?? Strings.t("alert.waiting.body"))
        }
    }

    private static func join(_ parts: String?...) -> String { parts.compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · ") }

    private static func humanize(_ id: String) -> String {
        switch ProviderFamily.of(id) {
        case "claude": return "Claude Code"
        case "codex": return "Codex"
        case "copilot": return "GitHub Copilot"
        case "cursor": return "Cursor"
        case "antigravity": return "Antigravity"
        case "gemini": return "Gemini CLI"
        case "glm": return "GLM"
        case "grok": return "Grok"
        case "opencode": return "OpenCode"
        default: return id
        }
    }
}

/// UNUserNotificationCenter. Copy only; the decision to alert was made upstream.
final class NotificationSink: NSObject, AlertSink, UNUserNotificationCenterDelegate {
    private let reading: (String) -> ProviderReading?
    private let activity: (String) -> Activity?
    var onOpened: ((String?) -> Void)?

    init(reading: @escaping (String) -> ProviderReading?, activity: @escaping (String) -> Activity?) {
        self.reading = reading
        self.activity = activity
        super.init()
        let centre = UNUserNotificationCenter.current()
        centre.delegate = self
        centre.requestAuthorization(options: [.alert, .sound]) { granted, error in
            if let error { Log.alerts.error("notifications: \(error.localizedDescription)") }
            Log.alerts.info("notifications \(granted ? "allowed" : "not allowed")")
        }
    }

    func deliver(_ alert: Alert) {
        let (title, body) = AlertCopy.compose(alert, reading: reading(alert.provider), activity: activity(alert.provider), now: Date())
        Log.alerts.info("notification: \(title) — \(body)")
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        content.userInfo = ["provider": alert.provider]
        content.threadIdentifier = alert.provider
        let request = UNNotificationRequest(identifier: "\(alert.provider)-\(alert.kind.rawValue)-\(alert.window ?? alert.sessionId ?? "")", content: content, trigger: nil)
        UNUserNotificationCenter.current().add(request) { error in
            if let error { Log.alerts.error("notification: \(error.localizedDescription)") }
        }
    }

    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification, withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .sound])
    }

    func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse, withCompletionHandler completionHandler: @escaping () -> Void) {
        DispatchQueue.main.async { self.onOpened?(response.notification.request.content.userInfo["provider"] as? String) }
        completionHandler()
    }
}

/// Tokendial's own notification: a card in the top-right corner painted with the system accent colour at 80 %,
/// white or near-black text following the appearance. A plain panel, so Focus never swallows it.
final class BannerSink: AlertSink {
    private let reading: (String) -> ProviderReading?
    private let activity: (String) -> Activity?
    private var banners: [BannerPanel] = []
    var onOpened: ((Alert) -> Void)?

    init(reading: @escaping (String) -> ProviderReading?, activity: @escaping (String) -> Activity?) {
        self.reading = reading
        self.activity = activity
    }

    func deliver(_ alert: Alert) {
        let current = reading(alert.provider)
        let (title, body) = AlertCopy.compose(alert, reading: current, activity: activity(alert.provider), now: Date())
        let fraction = alert.pct.map { Double($0) / 100 } ?? current?.headlineFraction
        Log.alerts.info("banner: \(title) — \(body)")
        DispatchQueue.main.async {
            let banner = BannerPanel(title: title, body: body, fraction: alert.kind == .waiting ? 1 : fraction, hollow: fraction == nil && alert.kind != .waiting)
            banner.onOpen = { [weak self] in self?.onOpened?(alert) }
            banner.onDismissed = { [weak self, weak banner] in
                guard let self, let banner else { return }
                self.banners.removeAll { $0 === banner }
                self.restack()
            }
            self.banners.insert(banner, at: 0)
            self.restack()
            banner.enter()
        }
    }

    private func restack() {
        guard let screen = NSScreen.screens.first else { return }
        var top = screen.visibleFrame.maxY - 12
        for banner in banners {
            let size = banner.frame.size
            banner.setFrameOrigin(NSPoint(x: screen.visibleFrame.maxX - 16 - size.width, y: top - size.height))
            top -= size.height + 10
        }
    }

    func closeAll() { banners.forEach { $0.orderOut(nil) }; banners = [] }
}

/// One notification card. Lives eight seconds, longer while the cursor is on it.
final class BannerPanel: UnobtrusivePanel {
    private var timer: Timer?
    private var leaving = false
    var onOpen: (() -> Void)?
    var onDismissed: (() -> Void)?

    init(title: String, body: String, fraction: Double?, hollow: Bool) {
        super.init(contentRect: NSRect(x: 0, y: 0, width: 360, height: 60), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        level = .statusBar
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        hidesOnDeactivate = false
        collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]

        let dark = NSApp.effectiveAppearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
        let accent = NSColor.controlAccentColor.usingColorSpace(.sRGB) ?? .systemBlue
        let ink = BannerPanel.ink(for: accent, darkApps: dark)
        let faint = ink.withAlphaComponent(ink == .white ? 0.35 : 0.27)

        let root = BannerView()
        root.wantsLayer = true
        root.layer?.backgroundColor = accent.withAlphaComponent(0.8).cgColor
        root.layer?.cornerRadius = 10
        root.layer?.cornerCurve = .continuous
        root.layer?.borderWidth = 1
        root.layer?.borderColor = faint.cgColor
        root.onClick = { [weak self] in self?.onOpen?(); self?.leave() }
        root.onEnter = { [weak self] in self?.timer?.invalidate() }
        root.onExit = { [weak self] in self?.arm(3) }
        contentView = root

        let dial = DialView(diameter: 30, stroke: 3.5)
        dial.color = ink
        dial.hollow = hollow
        if let fraction { dial.set(fraction, animated: false) }
        let titleField = Label.make(title, size: 14, color: ink, weight: .semibold)
        titleField.lineBreakMode = .byWordWrapping
        titleField.maximumNumberOfLines = 2
        let bodyField = Label.make(body, size: 12, color: ink.withAlphaComponent(0.85))
        bodyField.lineBreakMode = .byWordWrapping
        bodyField.maximumNumberOfLines = 3
        let close = NSButton(title: "✕", target: self, action: #selector(closeTapped))
        close.isBordered = false
        close.font = Theme.font(12)
        close.contentTintColor = ink.withAlphaComponent(0.75)
        close.translatesAutoresizingMaskIntoConstraints = false
        let text = NSStackView(views: [titleField, bodyField])
        text.orientation = .vertical
        text.alignment = .leading
        text.spacing = 2
        text.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(dial)
        root.addSubview(text)
        root.addSubview(close)
        NSLayoutConstraint.activate([
            dial.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 14),
            dial.topAnchor.constraint(equalTo: root.topAnchor, constant: 13),
            text.leadingAnchor.constraint(equalTo: dial.trailingAnchor, constant: 12),
            text.topAnchor.constraint(equalTo: root.topAnchor, constant: 12),
            text.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -12),
            text.trailingAnchor.constraint(equalTo: close.leadingAnchor, constant: -8),
            close.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -10),
            close.topAnchor.constraint(equalTo: root.topAnchor, constant: 10),
            close.widthAnchor.constraint(equalToConstant: 22),
            close.heightAnchor.constraint(equalToConstant: 22)
        ])
        titleField.preferredMaxLayoutWidth = 360 - 14 - 30 - 12 - 8 - 22 - 10
        bodyField.preferredMaxLayoutWidth = titleField.preferredMaxLayoutWidth
        root.layoutSubtreeIfNeeded()
        let height = max(60, text.fittingSize.height + 24)
        setContentSize(NSSize(width: 360, height: height))
    }

    /// The appearance picks the ink; the fill can veto it below 3:1 contrast.
    static func ink(for fill: NSColor, darkApps: Bool) -> NSColor {
        let white = NSColor.white
        let black = NSColor(srgbRed: 0x1A / 255, green: 0x1B / 255, blue: 0x1E / 255, alpha: 1)
        let preferred = darkApps ? white : black
        return contrast(preferred, fill) >= 3 ? preferred : (preferred == white ? black : white)
    }

    static func contrast(_ a: NSColor, _ b: NSColor) -> Double {
        let la = luminance(a) + 0.05, lb = luminance(b) + 0.05
        return la > lb ? la / lb : lb / la
    }

    private static func luminance(_ colour: NSColor) -> Double {
        let c = colour.usingColorSpace(.sRGB) ?? colour
        func channel(_ v: CGFloat) -> Double { let s = Double(v); return s <= 0.03928 ? s / 12.92 : pow((s + 0.055) / 1.055, 2.4) }
        return 0.2126 * channel(c.redComponent) + 0.7152 * channel(c.greenComponent) + 0.0722 * channel(c.blueComponent)
    }

    func enter() {
        alphaValue = 0
        orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { context in
            context.duration = Theme.reduceMotion ? 0 : 0.18
            animator().alphaValue = 1
        }
        arm(8)
    }

    private func arm(_ seconds: TimeInterval) {
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: seconds, repeats: false) { [weak self] _ in self?.leave() }
    }

    @objc private func closeTapped() { leave() }

    private func leave() {
        if leaving { return }
        leaving = true
        timer?.invalidate()
        NSAnimationContext.runAnimationGroup({ context in
            context.duration = Theme.reduceMotion ? 0 : 0.15
            self.animator().alphaValue = 0
        }, completionHandler: { [weak self] in
            self?.orderOut(nil)
            self?.onDismissed?()
        })
    }
}

final class BannerView: NSView {
    var onClick: (() -> Void)?
    var onEnter: (() -> Void)?
    var onExit: (() -> Void)?

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeAlways], owner: self, userInfo: nil))
    }

    override func mouseUp(with event: NSEvent) { onClick?() }
    override func mouseEntered(with event: NSEvent) { onEnter?() }
    override func mouseExited(with event: NSEvent) { onExit?() }
}

/// Routes each alert to the notification centre, to Tokendial's banners, or to both when the system is silent.
final class AlertRouter: AlertSink {
    private let notifications: NotificationSink
    private let banners: BannerSink
    private let mode: () -> AlertDelivery

    init(notifications: NotificationSink, banners: BannerSink, mode: @escaping () -> AlertDelivery) {
        self.notifications = notifications
        self.banners = banners
        self.mode = mode
    }

    func deliver(_ alert: Alert) {
        switch mode() {
        case .systemNotifications: notifications.deliver(alert)
        case .tokendialBanners: banners.deliver(alert)
        case .systemThenBanners:
            notifications.deliver(alert)
            UNUserNotificationCenter.current().getNotificationSettings { settings in
                if settings.authorizationStatus != .authorized { self.banners.deliver(alert) }
            }
        }
    }
}
