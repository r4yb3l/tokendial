import AppKit
import TokendialCore

/// A 240° arc opening downward. The arc's end is the reading; there is no needle. Drawn with two shape layers so the reading glides.
final class DialView: NSView {
    private let track = CAShapeLayer()
    private let progress = CAShapeLayer()
    private let stroke: CGFloat
    private(set) var fraction: Double = 0

    init(diameter: CGFloat, stroke: CGFloat) {
        self.stroke = stroke
        super.init(frame: NSRect(x: 0, y: 0, width: diameter, height: diameter))
        wantsLayer = true
        layer?.masksToBounds = false
        for shape in [track, progress] {
            shape.fillColor = nil
            shape.lineWidth = stroke
            shape.lineCap = .round
            shape.frame = bounds
            shape.path = Self.arc(in: bounds, stroke: stroke)
            layer?.addSublayer(shape)
        }
        track.strokeColor = Theme.track.cgColor
        progress.strokeColor = Theme.textDisabled.cgColor
        progress.strokeEnd = 0
        translatesAutoresizingMaskIntoConstraints = false
        widthAnchor.constraint(equalToConstant: diameter).isActive = true
        heightAnchor.constraint(equalToConstant: diameter).isActive = true
    }

    required init?(coder: NSCoder) { fatalError() }

    /// No reading: the track alone, dimmed.
    var hollow = false {
        didSet {
            track.strokeColor = (hollow ? Theme.textDisabled : Theme.track).cgColor
            progress.isHidden = hollow
        }
    }

    var color: NSColor = Theme.textDisabled {
        didSet { progress.strokeColor = color.cgColor }
    }

    func set(_ value: Double, animated: Bool) {
        let target = min(1, max(0, value))
        let from = progress.presentation()?.strokeEnd ?? progress.strokeEnd
        fraction = target
        progress.removeAllAnimations()
        progress.strokeEnd = target
        if animated && !Theme.reduceMotion {
            progress.add(Spring.reading.animation("strokeEnd", from: from, to: target), forKey: "reading")
        }
    }

    /// Screen angles run clockwise from 3 o'clock; the layer is y-up, so 150° visual is 210° here and the sweep goes clockwise to −30°.
    static func arc(in bounds: CGRect, stroke: CGFloat, start: CGFloat = 210, sweep: CGFloat = 240) -> CGPath {
        let path = CGMutablePath()
        let radius = (min(bounds.width, bounds.height) - stroke) / 2
        let centre = CGPoint(x: bounds.midX, y: bounds.midY)
        path.addArc(center: centre, radius: radius, startAngle: start * .pi / 180, endAngle: (start - sweep) * .pi / 180, clockwise: true)
        return path
    }
}

/// The activity indicator inside an expanded dial: a short arc that spins while an agent works and holds still while it waits on you.
final class ActivityArcView: NSView {
    private let shape = CAShapeLayer()
    private var state: SessionState?

    init() {
        super.init(frame: NSRect(x: 0, y: 0, width: Theme.activityDial, height: Theme.activityDial))
        wantsLayer = true
        shape.fillColor = nil
        shape.lineWidth = Theme.activityStroke
        shape.lineCap = .round
        shape.frame = bounds
        layer?.addSublayer(shape)
        isHidden = true
        translatesAutoresizingMaskIntoConstraints = false
        widthAnchor.constraint(equalToConstant: Theme.activityDial).isActive = true
        heightAnchor.constraint(equalToConstant: Theme.activityDial).isActive = true
    }

    required init?(coder: NSCoder) { fatalError() }

    func show(_ next: SessionState?) {
        if next == state { return }
        state = next
        shape.removeAllAnimations()
        guard let next, next != .idle else { isHidden = true; return }
        isHidden = false
        shape.strokeColor = (next == .waiting ? Theme.waiting : Theme.working).cgColor
        if next == .waiting {
            shape.path = DialView.arc(in: bounds, stroke: Theme.activityStroke)
            return
        }
        shape.path = DialView.arc(in: bounds, stroke: Theme.activityStroke, start: 90, sweep: 100)
        guard !Theme.reduceMotion else { return }
        let spin = CABasicAnimation(keyPath: "transform.rotation.z")
        spin.fromValue = 0
        spin.toValue = -2 * Double.pi
        spin.duration = 1.4
        spin.repeatCount = .infinity
        shape.anchorPoint = CGPoint(x: 0.5, y: 0.5)
        shape.frame = bounds
        shape.add(spin, forKey: "spin")
    }
}
