import AppKit


/// The dock hanging from a screen edge: a trapezoid whose base spans the full length along the edge,
/// whose sides slant inward, and whose far corners are rounded. The base has no outline, so the shape
/// reads as part of the edge rather than a floating pill. It is built as if the edge were the top, then
/// mapped onto the edge it actually hangs from.
enum DockShape {
    /// Closed fill for a dock of the given length along the edge and depth across it.
    static func fill(along: CGFloat, across: CGFloat, slant: CGFloat, radius: CGFloat, edge: DockEdge = .top) -> CGPath {
        build(along: along, across: across, slant: slant, radius: radius, edge: edge, closed: true)
    }

    /// Open hairline along the sides and the far side only; the base stays open against the edge.
    static func edge(along: CGFloat, across: CGFloat, slant: CGFloat, radius: CGFloat, edge: DockEdge = .top) -> CGPath {
        build(along: along, across: across, slant: slant, radius: radius, edge: edge, closed: false)
    }

    private static func build(along: CGFloat, across: CGFloat, slant: CGFloat, radius: CGFloat, edge: DockEdge, closed: Bool) -> CGPath {
        // AppKit's y grows upward, so the top edge sits at y = across and the far side at y = 0.
        func map(_ x: CGFloat, _ y: CGFloat) -> CGPoint {
            switch edge {
            case .top: return CGPoint(x: x, y: across - y)
            case .bottom: return CGPoint(x: x, y: y)
            case .left: return CGPoint(x: y, y: x)
            case .right: return CGPoint(x: across - y, y: x)
            }
        }
        let r = max(0, min(radius, min(across / 2, (along - 2 * slant) / 2)))
        let length = (slant * slant + across * across).squareRoot()
        let ux = slant / length
        let uy = across / length
        let path = CGMutablePath()
        if closed {
            path.move(to: map(0, 0))
            path.addLine(to: map(along, 0))
        } else {
            path.move(to: map(along, 0))
        }
        let farRight = CGPoint(x: along - slant, y: across)
        path.addLine(to: map(farRight.x + ux * r, farRight.y - uy * r))
        path.addQuadCurve(to: map(farRight.x - r, across), control: map(farRight.x, farRight.y))
        path.addLine(to: map(slant + r, across))
        let farLeft = CGPoint(x: slant, y: across)
        path.addQuadCurve(to: map(farLeft.x - ux * r, farLeft.y - uy * r), control: map(farLeft.x, farLeft.y))
        path.addLine(to: map(0, 0))
        if closed { path.closeSubpath() }
        return path
    }
}
