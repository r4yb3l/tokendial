import AppKit
import TokendialCore

/// A panel that never takes key or main status.
class UnobtrusivePanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

/// Where the capsule sits: under the notch when there is one, below the menu bar otherwise. Pure so it can be tested against fixture screens.
struct PanelGeometry {
    let screenFrame: NSRect
    let visibleFrame: NSRect
    let notchHeight: CGFloat

    init(screen: NSScreen) {
        screenFrame = screen.frame
        visibleFrame = screen.visibleFrame
        notchHeight = screen.safeAreaInsets.top
    }

    init(screenFrame: NSRect, visibleFrame: NSRect, notchHeight: CGFloat) {
        self.screenFrame = screenFrame
        self.visibleFrame = visibleFrame
        self.notchHeight = notchHeight
    }

    /// The y of the capsule's top edge. With a notch the capsule shares the notch's bottom edge; without one it hangs below the menu bar.
    var top: CGFloat {
        if notchHeight > 0 { return screenFrame.maxY - notchHeight + Theme.compactHeight }
        let menuBar = min(40, screenFrame.maxY - visibleFrame.maxY)
        return screenFrame.maxY - menuBar
    }

    func frame(width: CGFloat, height: CGFloat) -> NSRect {
        let clampedTop = min(top, screenFrame.maxY)
        return NSRect(x: screenFrame.midX - width / 2, y: clampedTop - height, width: width, height: height)
    }
}

/// The capsule at the top edge. The window is exactly the capsule plus a hot zone below it, so hover comes from a tracking area
/// and clicks outside the capsule fall through to whatever is underneath.
final class PanelController: NSObject {
    private let window: UnobtrusivePanel
    private let capsule = NSView()
    private let content = PanelContentView(frame: .zero)
    private var card: NSPanel?
    private var cardFor: String?
    private var model = PanelModel.empty
    private var mode = PanelMode.expandOnHover
    private var hovering = false
    private var expanded = false
    private var pinned = false
    private var pinTimer: Timer?
    private var collapseTimer: Timer?
    private var tracking: NSTrackingArea?

    var onHoverChanged: ((Bool) -> Void)?
    var onProviderClicked: ((String) -> Void)?
    var onSettingsRequested: (() -> Void)?
    var onExpandedShown: (() -> Void)?
    var now: () -> Date = Date.init

    override init() {
        window = UnobtrusivePanel(contentRect: NSRect(x: 0, y: 0, width: 200, height: Theme.compactHeight + Theme.hotZone), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        super.init()
        window.level = .statusBar
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = false
        window.hidesOnDeactivate = false
        window.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]
        window.isMovableByWindowBackground = false
        window.animationBehavior = .none

        let root = HoverView()
        root.onEnter = { [weak self] in self?.setHover(true) }
        root.onExit = { [weak self] in self?.scheduleCollapse() }
        root.onMove = { [weak self] point in self?.mouseMoved(point) }
        root.onRightClick = { [weak self] in self?.onSettingsRequested?() }
        window.contentView = root

        capsule.wantsLayer = true
        capsule.layer?.backgroundColor = Theme.surface.cgColor
        capsule.layer?.borderColor = Theme.surfaceEdge.cgColor
        capsule.layer?.borderWidth = 1
        capsule.layer?.cornerRadius = Theme.compactRadius
        capsule.layer?.cornerCurve = .continuous
        root.addSubview(capsule)
        capsule.addSubview(content)
        NSLayoutConstraint.activate([
            content.leadingAnchor.constraint(equalTo: capsule.leadingAnchor),
            content.trailingAnchor.constraint(equalTo: capsule.trailingAnchor),
            content.topAnchor.constraint(equalTo: capsule.topAnchor),
            content.bottomAnchor.constraint(equalTo: capsule.bottomAnchor)
        ])
        content.onCellClick = { [weak self] id in self?.onProviderClicked?(id) }
        layout(animated: false)
    }

    var isExpanded: Bool { expanded }

    func setMode(_ next: PanelMode) {
        mode = next
        if mode == .hidden { window.orderOut(nil); hideCard(); return }
        if !window.isVisible { window.orderFrontRegardless() }
        reconcile()
    }

    /// A notification was clicked or a second instance launched: expand for a moment even without the cursor.
    func flash(_ duration: TimeInterval = 5, showing provider: String? = nil) {
        if mode == .hidden { setMode(.expandOnHover) }
        pinned = true
        pinTimer?.invalidate()
        pinTimer = Timer.scheduledTimer(withTimeInterval: duration, repeats: false) { [weak self] _ in self?.pinned = false; self?.reconcile() }
        reconcile()
        if let provider {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { [weak self] in self?.showCard(provider) }
        }
    }

    func update(_ next: PanelModel) {
        let countChanged = next.tiles.count != model.tiles.count
        model = next
        content.apply(model, now: now(), animated: !countChanged)
        if countChanged { layout(animated: window.isVisible) }
        if let id = cardFor { showCard(id) }
    }

    func reposition() { layout(animated: false) }

    private func setHover(_ on: Bool) {
        collapseTimer?.invalidate()
        if on == hovering { return }
        hovering = on
        onHoverChanged?(on)
        reconcile()
    }

    private func scheduleCollapse() {
        collapseTimer?.invalidate()
        collapseTimer = Timer.scheduledTimer(withTimeInterval: 0.18, repeats: false) { [weak self] _ in
            guard let self else { return }
            if let card = self.card, card.frame.contains(NSEvent.mouseLocation) { self.scheduleCollapse(); return }
            if self.window.frame.contains(NSEvent.mouseLocation) { return }
            self.setHover(false)
        }
    }

    private func mouseMoved(_ point: NSPoint) {
        guard expanded else { return }
        for cell in content.cellViews {
            let frame = cell.convert(cell.bounds, to: nil)
            if frame.contains(point) {
                if cell.id != cardFor { showCard(cell.id) }
                return
            }
        }
    }

    private func reconcile() {
        let want: Bool
        switch mode {
        case .alwaysExpanded: want = true
        case .hidden: want = false
        case .expandOnHover: want = hovering || pinned
        }
        if want == expanded { return }
        expanded = want
        layout(animated: true)
        crossfade()
        if expanded { onExpandedShown?() } else { hideCard() }
    }

    private func layout(animated: Bool) {
        guard let screen = NSScreen.screens.first else { return }
        let geometry = PanelGeometry(screen: screen)
        let n = model.tiles.count
        let width = expanded ? Theme.expandedWidth(n) : Theme.compactWidth(n)
        let height = expanded ? Theme.expandedHeight : Theme.compactHeight
        let frame = geometry.frame(width: width, height: height + Theme.hotZone)
        capsule.layer?.cornerRadius = expanded ? Theme.expandedRadius : Theme.compactRadius
        let capsuleFrame = NSRect(x: 0, y: Theme.hotZone, width: width, height: height)
        let duration = animated && !Theme.reduceMotion ? Spring.expand.settle * 0.6 : 0
        NSAnimationContext.runAnimationGroup { context in
            context.duration = duration
            context.timingFunction = CAMediaTimingFunction(controlPoints: 0.2, 0.9, 0.3, 1.0)
            context.allowsImplicitAnimation = true
            window.setFrame(frame, display: true)
            capsule.animator().frame = capsuleFrame
        }
        if let tracking { window.contentView?.removeTrackingArea(tracking) }
        let area = NSTrackingArea(rect: NSRect(x: 0, y: 0, width: width, height: height + Theme.hotZone), options: [.mouseEnteredAndExited, .mouseMoved, .activeAlways], owner: window.contentView, userInfo: nil)
        window.contentView?.addTrackingArea(area)
        tracking = area
    }

    private func crossfade() {
        let incoming: NSView = expanded ? content.expanded : content.compactRow
        let outgoing: NSView = expanded ? content.compactRow : content.expanded
        incoming.isHidden = false
        NSAnimationContext.runAnimationGroup({ context in
            context.duration = Theme.reduceMotion ? 0 : 0.15
            outgoing.animator().alphaValue = 0
            incoming.animator().alphaValue = 1
        }, completionHandler: { [weak outgoing] in
            if outgoing?.alphaValue == 0 { outgoing?.isHidden = true }
        })
    }

    private func showCard(_ id: String) {
        guard let tile = model.tiles.first(where: { $0.id == id }), let cell = content.cellViews.first(where: { $0.id == id }) else { hideCard(); return }
        let view = HoverCard.build(tile, now: now())
        let panel = card ?? {
            let p = UnobtrusivePanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
            p.level = .statusBar
            p.isOpaque = false
            p.backgroundColor = .clear
            p.hasShadow = true
            p.hidesOnDeactivate = false
            p.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]
            return p
        }()
        panel.contentView = view
        view.layoutSubtreeIfNeeded()
        let size = view.fittingSize
        let cellOnScreen = cell.window!.convertToScreen(cell.convert(cell.bounds, to: nil))
        let screen = NSScreen.screens.first?.frame ?? window.frame
        var x = cellOnScreen.midX - Theme.cardWidth / 2
        x = min(max(x, screen.minX + 8), screen.maxX - Theme.cardWidth - 8)
        let y = window.frame.maxY - Theme.expandedHeight - 6 - size.height
        panel.setFrame(NSRect(x: x, y: y, width: Theme.cardWidth, height: size.height), display: true)
        if card == nil {
            panel.alphaValue = 0
            panel.orderFrontRegardless()
            NSAnimationContext.runAnimationGroup { context in
                context.duration = Theme.reduceMotion ? 0 : 0.15
                panel.animator().alphaValue = 1
            }
        }
        card = panel
        cardFor = id
    }

    private func hideCard() {
        card?.orderOut(nil)
        card = nil
        cardFor = nil
    }
}

/// The content view of the capsule window: relays tracking events to the controller.
final class HoverView: NSView {
    var onEnter: (() -> Void)?
    var onExit: (() -> Void)?
    var onMove: ((NSPoint) -> Void)?
    var onRightClick: (() -> Void)?

    override func mouseEntered(with event: NSEvent) { onEnter?() }
    override func mouseExited(with event: NSEvent) { onExit?() }
    override func mouseMoved(with event: NSEvent) { onMove?(event.locationInWindow) }
    override func rightMouseUp(with event: NSEvent) { onRightClick?() }
    override var acceptsFirstResponder: Bool { false }
}
