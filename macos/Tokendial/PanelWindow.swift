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

    /// The room the dock has along the edge it hangs from.
    func available(edge: DockEdge) -> CGFloat {
        switch edge {
        case .top, .bottom: return visibleFrame.width
        case .left, .right: return visibleFrame.height
        }
    }

    /// The window for a dock of the given length along its edge and depth across it, centred on that edge.
    /// The other three edges follow the visible frame, so the Dock never sits under the panel.
    func frame(along: CGFloat, across: CGFloat, edge: DockEdge) -> NSRect {
        switch edge {
        case .top:
            return NSRect(x: screenFrame.midX - along / 2, y: min(top, screenFrame.maxY) - across, width: along, height: across)
        case .bottom:
            return NSRect(x: screenFrame.midX - along / 2, y: visibleFrame.minY, width: along, height: across)
        case .left:
            return NSRect(x: visibleFrame.minX, y: screenFrame.midY - along / 2, width: across, height: along)
        case .right:
            return NSRect(x: visibleFrame.maxX - across, y: screenFrame.midY - along / 2, width: across, height: along)
        }
    }
}

/// The capsule at the top edge. The window is exactly the capsule plus a hot zone below it, so hover comes from a tracking area
/// and clicks outside the capsule fall through to whatever is underneath.
final class PanelController: NSObject {
    private let window: UnobtrusivePanel
    private let capsule = NSView()
    private let fillLayer = CAShapeLayer()
    private let hairlineLayer = CAShapeLayer()
    private let content = PanelContentView(frame: .zero)
    private var card: NSPanel?
    private var cardFor: String?
    private var model = PanelModel.empty
    private var mode = PanelMode.expandOnHover
    private var edge = DockEdge.top
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
        fillLayer.fillColor = Theme.surface.cgColor
        hairlineLayer.fillColor = nil
        hairlineLayer.strokeColor = Theme.surfaceEdge.cgColor
        hairlineLayer.lineWidth = 1
        capsule.layer?.addSublayer(fillLayer)
        capsule.layer?.addSublayer(hairlineLayer)
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

    /// Repaint after a theme switch: the capsule's layer colours, the contents, and the card if one is open.
    func retheme() {
        fillLayer.fillColor = Theme.surface.cgColor
        hairlineLayer.strokeColor = Theme.surfaceEdge.cgColor
        content.retheme()
        content.apply(model, now: now(), animated: false)
        layout(animated: false)
        if let id = cardFor { showCard(id) }
    }

    func setMode(_ next: PanelMode) {
        mode = next
        if mode == .hidden { window.orderOut(nil); hideCard(); return }
        if !window.isVisible { window.orderFrontRegardless() }
        reconcile()
    }

    /// The edge the dock hangs from: the shape, the layout and the card all turn with it.
    func setEdge(_ next: DockEdge) {
        guard next != edge else { return }
        edge = next
        layout(animated: false)
        if let id = cardFor { showCard(id) }
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
        let vertical = edge == .left || edge == .right
        let box = DockLayout(count: n, vertical: vertical, available: geometry.available(edge: edge))
        content.setShape(vertical: vertical, cellsPerColumn: box.cellsPerColumn)
        let along = expanded ? box.along : Theme.compactLength(n)
        let across = expanded ? box.across : Theme.compactHeight
        let frame = geometry.frame(along: along, across: across + Theme.hotZone, edge: edge)
        let capsuleFrame = switch edge {
        case .top: NSRect(x: 0, y: Theme.hotZone, width: along, height: across)
        case .bottom: NSRect(x: 0, y: 0, width: along, height: across)
        case .left: NSRect(x: 0, y: 0, width: across, height: along)
        case .right: NSRect(x: Theme.hotZone, y: 0, width: across, height: along)
        }
        reshape(along: along, across: across)
        let duration = animated && !Theme.reduceMotion ? Spring.expand.settle * 0.6 : 0
        NSAnimationContext.runAnimationGroup { context in
            context.duration = duration
            context.timingFunction = CAMediaTimingFunction(controlPoints: 0.2, 0.9, 0.3, 1.0)
            context.allowsImplicitAnimation = true
            window.setFrame(frame, display: true)
            capsule.animator().frame = capsuleFrame
        }
        if let tracking { window.contentView?.removeTrackingArea(tracking) }
        let area = NSTrackingArea(rect: NSRect(origin: .zero, size: frame.size), options: [.mouseEnteredAndExited, .mouseMoved, .activeAlways], owner: window.contentView, userInfo: nil)
        window.contentView?.addTrackingArea(area)
        tracking = area
    }

    /// The trapezoid for the current size: filled, plus the hairline that leaves the base against the edge open.
    private func reshape(along: CGFloat, across: CGFloat) {
        let radius = expanded ? Theme.expandedRadius : Theme.compactRadius
        let vertical = edge == .left || edge == .right
        let bounds = CGRect(x: 0, y: 0, width: vertical ? across : along, height: vertical ? along : across)
        for layer in [fillLayer, hairlineLayer] { layer.frame = bounds }
        fillLayer.path = DockShape.fill(along: along, across: across, slant: Theme.slant, radius: radius, edge: edge)
        hairlineLayer.path = DockShape.edge(along: along, across: across, slant: Theme.slant, radius: radius, edge: edge)
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
        var x: CGFloat
        var y: CGFloat
        switch edge {
        case .top:
            x = cellOnScreen.midX - Theme.cardWidth / 2
            y = window.frame.minY + Theme.hotZone - 6 - size.height
        case .bottom:
            x = cellOnScreen.midX - Theme.cardWidth / 2
            y = window.frame.maxY - Theme.hotZone + 6
        case .left:
            x = window.frame.maxX - Theme.hotZone + 6
            y = cellOnScreen.midY - size.height / 2
        case .right:
            x = window.frame.minX + Theme.hotZone - 6 - Theme.cardWidth
            y = cellOnScreen.midY - size.height / 2
        }
        x = min(max(x, screen.minX + 8), screen.maxX - Theme.cardWidth - 8)
        y = min(max(y, screen.minY + 8), screen.maxY - size.height - 8)
        panel.setFrame(NSRect(x: x, y: y, width: Theme.cardWidth, height: size.height), display: true)
        if card == nil {
            panel.alphaValue = 0
            panel.orderFrontRegardless()
            NSAnimationContext.runAnimationGroup({ context in
                context.duration = Theme.reduceMotion ? 0 : 0.15
                panel.animator().alphaValue = 1
            }, completionHandler: { panel.alphaValue = 1 })
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
