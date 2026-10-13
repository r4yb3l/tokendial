import AppKit
import Darwin
import TokendialCore

/// The floating face's own colours: always the dark palette, like a device's screen, so it reads the same over
/// any window and in either theme. The ground is the dark card's hue at full opacity.
private enum Screen {
    static let palette = Theme.darkPalette
    static let ground = NSColor(srgbRed: 16 / 255, green: 17 / 255, blue: 20 / 255, alpha: 1)

    static func color(_ band: Band?) -> NSColor {
        switch band { case .ample?: return palette.ample; case .watch?: return palette.watch; case .critical?: return palette.critical; case nil: return palette.textDisabled }
    }
}

/// Tokendial's face: the dial's own 240° arc around a dark disc, with the face typed in glyphs inside it.
/// docs/design/tokens.md, "Floating face", carries every number used here.
final class FaceView: NSView {
    static let diameter: CGFloat = 72
    private static let stroke = diameter * 0.085

    private let dial = DialView(diameter: FaceView.diameter - 1.2 * FaceView.stroke, stroke: FaceView.stroke, track: Screen.palette.track)
    private let eyes = FaceView.glyphs(FaceView.diameter * 0.25)
    private let mouth = FaceView.glyphs(FaceView.diameter * 0.22)
    private var gaze: NSLayoutConstraint!
    private var eyesLine: NSLayoutConstraint!
    private var mouthLine: NSLayoutConstraint!
    private var face = Face.empty
    private var blinking = false
    private var motion: Timer?
    private var blink: Timer?
    private var moving: SessionState?

    init() {
        super.init(frame: NSRect(x: 0, y: 0, width: FaceView.diameter, height: FaceView.diameter))
        wantsLayer = true
        layer?.backgroundColor = Screen.ground.cgColor
        layer?.cornerRadius = Self.diameter / 2
        layer?.borderWidth = 1
        layer?.borderColor = Screen.palette.surfaceEdge.cgColor
        translatesAutoresizingMaskIntoConstraints = false
        for view in [dial, eyes, mouth] as [NSView] { addSubview(view) }
        gaze = eyes.centerXAnchor.constraint(equalTo: centerXAnchor)
        eyesLine = eyes.centerYAnchor.constraint(equalTo: topAnchor, constant: Self.diameter * 0.43)
        mouthLine = mouth.centerYAnchor.constraint(equalTo: topAnchor, constant: Self.diameter * 0.65)
        NSLayoutConstraint.activate([
            widthAnchor.constraint(equalToConstant: Self.diameter),
            heightAnchor.constraint(equalToConstant: Self.diameter),
            dial.centerXAnchor.constraint(equalTo: centerXAnchor),
            dial.centerYAnchor.constraint(equalTo: centerYAnchor),
            gaze, eyesLine, mouthLine,
            mouth.centerXAnchor.constraint(equalTo: centerXAnchor)
        ])
        startBlinking()
    }

    required init?(coder: NSCoder) { fatalError() }

    deinit {
        motion?.invalidate()
        blink?.invalidate()
    }

    func show(_ next: Face, animated: Bool) {
        face = next
        dial.color = Screen.color(next.band)
        dial.set(next.fraction, animated: animated)
        paint()
        move(next.spent ? nil : next.mood)
    }

    private func paint() {
        let ink = face.spent ? Screen.palette.textDisabled : Screen.palette.textPrimary
        eyes.stringValue = blinking && !face.spent ? Face.blink : face.eyes
        eyes.textColor = ink
        mouth.stringValue = face.mouth
        mouth.textColor = face.mood == .waiting && !face.spent ? Screen.palette.watch : ink
    }

    /// Waiting nods, working looks around. Both stop under reduce motion, which leaves the glyphs saying the same thing.
    private func move(_ state: SessionState?) {
        if state == moving { return }
        moving = state
        motion?.invalidate()
        motion = nil
        place(gaze: 0, bob: 0)
        guard !Theme.reduceMotion, state == .waiting || state == .working else { return }
        let start = Date()
        motion = Timer.scheduledTimer(withTimeInterval: 1.0 / 30, repeats: true) { [weak self] _ in
            let t = Date().timeIntervalSince(start)
            if state == .waiting { self?.place(gaze: 0, bob: FaceView.bobOffset(t)) } else { self?.place(gaze: FaceView.glance(t), bob: 0) }
        }
    }

    private func place(gaze offset: CGFloat, bob: CGFloat) {
        gaze.constant = offset * Self.diameter * 0.05
        eyesLine.constant = Self.diameter * 0.43 - bob
        mouthLine.constant = Self.diameter * 0.65 - bob
    }

    /// 0.04 of the diameter up and back on a 0.78 s sine.
    private static func bobOffset(_ t: TimeInterval) -> CGFloat {
        CGFloat((1 - cos(t / 0.78 * .pi)) / 2) * diameter * 0.04
    }

    /// Left, pause, right, pause, on a 2.6 s cycle: reads as thinking rather than as a metronome.
    private static func glance(_ t: TimeInterval) -> CGFloat {
        let p = t.truncatingRemainder(dividingBy: 2.6) / 2.6
        func ease(_ x: Double) -> Double { (1 - cos(x * .pi)) / 2 }
        switch p {
        case ..<0.22: return -1
        case ..<0.44: return CGFloat(-1 + 2 * ease((p - 0.22) / 0.22))
        case ..<0.70: return 1
        default: return CGFloat(1 - 2 * ease((p - 0.70) / 0.30))
        }
    }

    /// Eyes shut for the last 3 % of every 5.2 s, in every state but spent.
    private func startBlinking() {
        guard !Theme.reduceMotion else { return }
        blink = Timer.scheduledTimer(withTimeInterval: 5.2, repeats: true) { [weak self] _ in
            guard let self else { return }
            self.blinking = true
            self.paint()
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.16) { [weak self] in
                self?.blinking = false
                self?.paint()
            }
        }
    }

    private static func glyphs(_ size: CGFloat) -> NSTextField {
        let label = NSTextField(labelWithString: "")
        label.font = NSFont.monospacedSystemFont(ofSize: size, weight: .bold)
        label.alignment = .center
        label.translatesAutoresizingMaskIntoConstraints = false
        return label
    }
}

/// The face's window content. It moves the panel itself rather than through isMovableByWindowBackground, so a
/// click that never travelled can be told apart from a drag and does not count as one.
private final class FaceGrip: NSView {
    var onDrag: ((NSPoint) -> Void)?
    var onDrop: (() -> Void)?
    var onRightClick: (() -> Void)?
    private var grab: (cursor: NSPoint, origin: NSPoint)?
    private var moved = false

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override var acceptsFirstResponder: Bool { false }

    override func mouseDown(with event: NSEvent) {
        grab = (NSEvent.mouseLocation, window?.frame.origin ?? .zero)
        moved = false
    }

    override func mouseDragged(with event: NSEvent) {
        guard let grab else { return }
        let cursor = NSEvent.mouseLocation
        let dx = cursor.x - grab.cursor.x
        let dy = cursor.y - grab.cursor.y
        if !moved && abs(dx) < 3 && abs(dy) < 3 { return }
        moved = true
        onDrag?(NSPoint(x: grab.origin.x + dx, y: grab.origin.y + dy))
    }

    /// A click that never moved the face reports nothing, so it does not rewrite the settings file.
    override func mouseUp(with event: NSEvent) {
        if grab != nil && moved { onDrop?() }
        grab = nil
    }

    override func rightMouseUp(with event: NSEvent) { onRightClick?() }
}

/// The floating face and its speech bubble: a small panel the user drags anywhere, which never takes focus
/// from what they are typing in.
final class MascotController {
    private static let inset: CGFloat = 10
    private static let size = FaceView.diameter + 2 * inset

    var onSettingsRequested: (() -> Void)?
    /// Where the face ended up after a drag: its window's origin in screen points.
    var onMoved: ((NSPoint) -> Void)?

    private let window = UnobtrusivePanel(contentRect: NSRect(x: 0, y: 0, width: MascotController.size, height: MascotController.size), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
    private let face = FaceView()
    private let bubble = SpeechBubble()

    init() {
        MascotController.float(window)
        let grip = FaceGrip(frame: NSRect(x: 0, y: 0, width: Self.size, height: Self.size))
        grip.addSubview(face)
        NSLayoutConstraint.activate([
            face.centerXAnchor.constraint(equalTo: grip.centerXAnchor),
            face.centerYAnchor.constraint(equalTo: grip.centerYAnchor)
        ])
        grip.onDrag = { [weak self] origin in
            self?.window.setFrameOrigin(origin)
            self?.placeBubble()
        }
        grip.onDrop = { [weak self] in
            guard let self else { return }
            self.onMoved?(self.window.frame.origin)
        }
        grip.onRightClick = { [weak self] in self?.onSettingsRequested?() }
        window.contentView = grip
        bubble.onChanged = { [weak self] in self?.placeBubble() }
    }

    /// The shared look of every panel the face owns: clear, borderless, above other windows, on every space.
    static func float(_ panel: NSPanel) {
        panel.level = .statusBar
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]
        panel.animationBehavior = .none
    }

    func apply(_ model: PanelModel, animated: Bool) {
        face.show(Face.of(model.tiles.map { $0.reading }, sessions: model.sessions), animated: animated)
        bubble.say(model.sessions.filter { $0.state == .waiting }.map { session in
            (session, model.tiles.first { $0.activity?.sessions.contains(session) == true }?.name ?? session.name)
        })
        placeBubble()
    }

    /// Puts the face where it was last left, kept on the screen it is on, or near the top-left of the main screen.
    func place(x: Double?, y: Double?) {
        let saved = x.flatMap { x in y.map { NSPoint(x: x, y: $0) } }
        let centre = saved.map { NSPoint(x: $0.x + Self.size / 2, y: $0.y + Self.size / 2) }
        let screen = centre.flatMap { c in NSScreen.screens.first { $0.frame.contains(c) } } ?? NSScreen.main ?? NSScreen.screens.first
        guard let visible = screen?.visibleFrame else { return }
        let origin = saved ?? NSPoint(x: visible.minX + 48, y: visible.maxY - 48 - Self.size)
        window.setFrameOrigin(NSPoint(
            x: min(max(origin.x, visible.minX), max(visible.minX, visible.maxX - Self.size)),
            y: min(max(origin.y, visible.minY), max(visible.minY, visible.maxY - Self.size))))
        window.orderFrontRegardless()
        placeBubble()
    }

    func close() {
        bubble.orderOut(nil)
        window.orderOut(nil)
    }

    /// Beside the face on whichever side has room, its top level with the face's top.
    private func placeBubble() {
        guard window.isVisible, bubble.hasSomethingToSay else { bubble.orderOut(nil); return }
        let frame = window.frame
        let size = bubble.frame.size
        let visible = (NSScreen.screens.first { $0.frame.contains(NSPoint(x: frame.midX, y: frame.midY)) } ?? NSScreen.main)?.visibleFrame ?? frame
        let gap = 2 - Self.inset
        let right = frame.maxX + gap + size.width <= visible.maxX
        bubble.faceOnLeft = right
        let x = right ? frame.maxX + gap : frame.minX - gap - size.width
        let y = min(max(frame.maxY - size.height, visible.minY), max(visible.minY, visible.maxY - size.height))
        bubble.setFrameOrigin(NSPoint(x: x, y: y))
        bubble.orderFrontRegardless()
    }
}

/// What the face says while an agent waits on you: who is waiting, where, and a way to get there. It replaces the
/// waiting alert while the face is on, and goes away by itself once nobody is waiting.
final class SpeechBubble: UnobtrusivePanel {
    private static let maxRows = 3
    private static let tail: CGFloat = 8
    private static let width: CGFloat = 300

    var onChanged: (() -> Void)?
    private let card = BubbleView()
    private let rows = NSStackView()
    private var settled = Set<String>()
    private var heard: [(AgentSession, String)] = []
    private var said: [String] = []

    init() {
        super.init(contentRect: NSRect(x: 0, y: 0, width: SpeechBubble.width, height: 80), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        MascotController.float(self)
        rows.orientation = .vertical
        rows.alignment = .leading
        rows.spacing = 12
        rows.translatesAutoresizingMaskIntoConstraints = false
        card.addSubview(rows)
        card.rowsLeading = rows.leadingAnchor.constraint(equalTo: card.leadingAnchor, constant: Self.tail + 14)
        card.rowsTrailing = rows.trailingAnchor.constraint(equalTo: card.trailingAnchor, constant: -14)
        NSLayoutConstraint.activate([
            card.rowsLeading!, card.rowsTrailing!,
            rows.topAnchor.constraint(equalTo: card.topAnchor, constant: 12),
            rows.bottomAnchor.constraint(equalTo: card.bottomAnchor, constant: -12),
            rows.widthAnchor.constraint(lessThanOrEqualToConstant: Self.width - 28)
        ])
        contentView = card
    }

    var hasSomethingToSay: Bool { !said.isEmpty }

    /// Points the tail at the face, which sits on the left of the bubble or on its right.
    var faceOnLeft = true {
        didSet {
            guard faceOnLeft != oldValue else { return }
            card.faceOnLeft = faceOnLeft
            card.rowsLeading?.constant = faceOnLeft ? Self.tail + 14 : 14
            card.rowsTrailing?.constant = faceOnLeft ? -14 : -(Self.tail + 14)
            card.needsDisplay = true
        }
    }

    /// Says who is waiting, leaving out a session the user was already taken to until it stops waiting and starts
    /// again. Rebuilds only when that list changes, so a refresh never resets a "not found" the user is reading.
    func say(_ waiting: [(AgentSession, String)]) {
        heard = waiting
        settled.formIntersection(waiting.map { $0.0.id })
        let saying = waiting.filter { !settled.contains($0.0.id) }
        guard saying.map({ $0.0.id }) != said else { return }
        said = saying.map { $0.0.id }
        rows.arrangedSubviews.forEach { $0.removeFromSuperview() }
        for (session, name) in saying.prefix(Self.maxRows) { rows.addArrangedSubview(row(session, name)) }
        card.layoutSubtreeIfNeeded()
        setContentSize(card.fittingSize)
        onChanged?()
    }

    private func row(_ session: AgentSession, _ name: String) -> NSView {
        let title = NSTextField(wrappingLabelWithString: Strings.t("alert.waiting.title", ["name": name]))
        title.font = NSFont.systemFont(ofSize: 13, weight: .semibold)
        title.textColor = Screen.palette.textPrimary
        title.preferredMaxLayoutWidth = Self.width - 28 - Self.tail
        let place = NSTextField(labelWithString: [session.location, session.waitingFor].compactMap { $0 }.joined(separator: " · "))
        place.font = NSFont.systemFont(ofSize: 11.5)
        place.textColor = Screen.palette.textSecondary
        place.lineBreakMode = .byTruncatingTail
        let stack = NSStackView(views: [title, place])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 2
        if let pid = session.pid {
            let button = PillButton(title: Strings.t("mascot.takeMeThere"))
            button.onClick = { [weak self, weak button] in
                if TakeMeThere.bringForward(pid) {
                    guard let self else { return }
                    self.settled.insert(session.id)
                    self.say(self.heard)
                } else {
                    button?.title = Strings.t("mascot.notFound")
                }
            }
            stack.setCustomSpacing(8, after: place)
            stack.addArrangedSubview(button)
        }
        return stack
    }
}

/// The bubble's card and its tail, drawn as one path so they share a fill and an edge.
private final class BubbleView: NSView {
    var faceOnLeft = true
    var rowsLeading: NSLayoutConstraint?
    var rowsTrailing: NSLayoutConstraint?

    override func draw(_ dirtyRect: NSRect) {
        let tail: CGFloat = 8
        let card = faceOnLeft ? bounds.insetBy(dx: 0, dy: 0.5).divided(atDistance: tail, from: .minXEdge).remainder.insetBy(dx: 0.5, dy: 0)
            : bounds.insetBy(dx: 0, dy: 0.5).divided(atDistance: tail, from: .maxXEdge).remainder.insetBy(dx: 0.5, dy: 0)
        let path = NSBezierPath(roundedRect: card, xRadius: 12, yRadius: 12)
        let tip = faceOnLeft ? card.minX - tail : card.maxX + tail
        let base = faceOnLeft ? card.minX + 1 : card.maxX - 1
        let middle = bounds.maxY - 40
        let point = NSBezierPath()
        point.move(to: NSPoint(x: base, y: middle + tail))
        point.line(to: NSPoint(x: tip, y: middle))
        point.line(to: NSPoint(x: base, y: middle - tail))
        point.close()
        path.append(point)
        Screen.ground.setFill()
        path.fill()
        Screen.palette.surfaceEdge.setStroke()
        NSBezierPath(roundedRect: card, xRadius: 12, yRadius: 12).stroke()
    }
}

/// "Take me there": a small pill in the face's dark palette.
private final class PillButton: NSView {
    var onClick: (() -> Void)?
    private let label: NSTextField

    var title: String {
        get { label.stringValue }
        set { label.stringValue = newValue; label.textColor = Screen.palette.textSecondary }
    }

    init(title: String) {
        label = NSTextField(labelWithString: title)
        super.init(frame: .zero)
        label.font = NSFont.systemFont(ofSize: 12, weight: .medium)
        label.textColor = Screen.palette.textPrimary
        label.translatesAutoresizingMaskIntoConstraints = false
        wantsLayer = true
        layer?.backgroundColor = Screen.palette.track.cgColor
        layer?.cornerRadius = 8
        addSubview(label)
        NSLayoutConstraint.activate([
            label.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 12),
            label.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -12),
            label.topAnchor.constraint(equalTo: topAnchor, constant: 5),
            label.bottomAnchor.constraint(equalTo: bottomAnchor, constant: -6)
        ])
    }

    required init?(coder: NSCoder) { fatalError() }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeAlways], owner: self, userInfo: nil))
    }

    override func mouseEntered(with event: NSEvent) { layer?.backgroundColor = Screen.palette.textDisabled.cgColor }
    override func mouseExited(with event: NSEvent) { layer?.backgroundColor = Screen.palette.track.cgColor }
    override func mouseUp(with event: NSEvent) { onClick?() }
}

/// Brings forward the app a session runs inside: the first ancestor of its process that is a regular app,
/// which is the terminal, editor or desktop app that started it. Finder and launchd end the walk.
enum TakeMeThere {
    static func bringForward(_ pid: Int32) -> Bool {
        let apps = Dictionary(NSWorkspace.shared.runningApplications.filter { $0.activationPolicy == .regular }.map { ($0.processIdentifier, $0) },
                              uniquingKeysWith: { first, _ in first })
        let found = Ancestry.firstAnswer(pid, parentOf: parent, pick: { id -> NSRunningApplication? in
            guard let app = apps[id], app.bundleIdentifier != "com.apple.finder" else { return nil }
            return app
        })
        guard let app = found else { return false }
        // A background app's activate() is only a request under macOS 14's cooperative activation and is often
        // declined; asking Launch Services to open the running app brings it forward reliably.
        guard let url = app.bundleURL else { return app.activate() }
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.activates = true
        NSWorkspace.shared.openApplication(at: url, configuration: configuration)
        return true
    }

    /// The parent process, or nil at launchd, which is every process's root and never the answer.
    private static func parent(_ pid: Int32) -> Int32? {
        var info = proc_bsdinfo()
        let size = Int32(MemoryLayout<proc_bsdinfo>.size)
        guard proc_pidinfo(pid, PROC_PIDTBSDINFO, 0, &info, size) == size else { return nil }
        let parent = Int32(info.pbi_ppid)
        return parent > 1 ? parent : nil
    }
}
