import AppKit
import TokendialCore

/// The detail card under a hovered cell: every window with its reset copy, a bar, both ends of the number, then the live sessions.
enum HoverCard {
    static func build(_ tile: Tile, now: Date) -> NSView {
        let body = NSStackView()
        body.orientation = .vertical
        body.alignment = .leading
        body.spacing = 0
        body.translatesAutoresizingMaskIntoConstraints = false

        let header = row(left: Label.make(tile.name, size: 14, color: Theme.textPrimary, weight: .semibold), right: tile.account.map { Label.make($0.summary, size: 11, color: Theme.textSecondary, alignment: .right) })
        add(header, to: body, top: 0)

        switch tile.reading.status {
        case .needsSignIn: add(wrap(Label.make("Not signed in.", size: 12, color: Theme.textSecondary)), to: body, top: 6)
        case .unsupported(let why): add(wrap(Label.make(why, size: 12, color: Theme.textSecondary)), to: body, top: 6)
        case .failed(let why): add(wrap(Label.make("Could not read usage (\(why)). Showing nothing rather than a guess.", size: 12, color: Theme.textSecondary)), to: body, top: 6)
        default: break
        }

        for window in tile.reading.windows {
            let reset = window.resetsAt.map { Copy.reset($0, now: now) } ?? ""
            add(row(left: Label.make(window.label, size: 12, color: Theme.textPrimary), right: Label.make(reset, size: 11, color: Theme.textSecondary, alignment: .right)), to: body, top: 8)
            if let fraction = window.usedFraction {
                let bar = BarView(height: 4)
                bar.set(fraction, color: Theme.color(tile.reading.block != nil ? .critical : Band.of(fraction)))
                add(bar, to: body, top: 5)
            }
            add(Label.make(window.summary(tile.reading.fidelity), size: 11, color: Theme.textSecondary), to: body, top: 4)
        }

        if let block = tile.reading.block {
            add(Label.make(Copy.until(block.reason, block.until, now: now), size: 11, color: Theme.critical), to: body, top: 8)
        }
        if case .stale(let since) = tile.reading.status, since > .distantPast {
            add(Label.make("Last read \(Copy.ago(since, now: now))", size: 11, color: Theme.textDisabled), to: body, top: 8)
        }
        if tile.reading.fidelity == .derived {
            add(wrap(Label.make("Derived from local activity, not published by the provider", size: 11, color: Theme.textDisabled)), to: body, top: 6)
        }
        if let activity = tile.activity, !activity.sessions.isEmpty {
            let line = NSView()
            line.wantsLayer = true
            line.layer?.backgroundColor = Theme.hairline.cgColor
            line.translatesAutoresizingMaskIntoConstraints = false
            line.heightAnchor.constraint(equalToConstant: 1).isActive = true
            add(line, to: body, top: 10)
            for session in activity.ordered.prefix(4) { add(sessionRow(session, now: now), to: body, top: 6) }
        }

        let card = NSView()
        card.wantsLayer = true
        card.layer?.backgroundColor = Theme.cardSurface.cgColor
        card.layer?.cornerRadius = Theme.cardRadius
        card.layer?.borderWidth = 1
        card.layer?.borderColor = Theme.surfaceEdge.cgColor
        card.translatesAutoresizingMaskIntoConstraints = false
        card.addSubview(body)
        NSLayoutConstraint.activate([
            card.widthAnchor.constraint(equalToConstant: Theme.cardWidth),
            body.leadingAnchor.constraint(equalTo: card.leadingAnchor, constant: Theme.cardPadding),
            body.trailingAnchor.constraint(equalTo: card.trailingAnchor, constant: -(Theme.cardPadding + 2)),
            body.topAnchor.constraint(equalTo: card.topAnchor, constant: Theme.cardPadding),
            body.bottomAnchor.constraint(equalTo: card.bottomAnchor, constant: -Theme.cardPadding)
        ])
        return card
    }

    private static func add(_ view: NSView, to stack: NSStackView, top: CGFloat) {
        stack.addView(view, in: .bottom)
        view.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
        if top > 0 { stack.setCustomSpacing(top, after: stack.views[max(0, stack.views.count - 2)]) }
    }

    private static func row(left: NSView, right: NSView?) -> NSView {
        let row = NSStackView()
        row.orientation = .horizontal
        row.distribution = .fill
        row.spacing = 8
        row.translatesAutoresizingMaskIntoConstraints = false
        row.addView(left, in: .leading)
        if let right {
            right.setContentCompressionResistancePriority(.required, for: .horizontal)
            right.setContentHuggingPriority(.required, for: .horizontal)
            if let field = right as? NSTextField { field.preferredMaxLayoutWidth = 130 }
            row.addView(right, in: .trailing)
        }
        return row
    }

    private static func wrap(_ field: NSTextField) -> NSTextField {
        field.lineBreakMode = .byWordWrapping
        field.maximumNumberOfLines = 0
        field.preferredMaxLayoutWidth = Theme.cardWidth - 2 * Theme.cardPadding - 2
        return field
    }

    static func sessionRow(_ session: AgentSession, now: Date) -> NSView {
        let row = NSStackView()
        row.orientation = .horizontal
        row.spacing = 8
        row.alignment = .centerY
        row.translatesAutoresizingMaskIntoConstraints = false
        let dot = NSView()
        dot.wantsLayer = true
        dot.layer?.cornerRadius = 3
        dot.layer?.backgroundColor = (session.state == .waiting ? Theme.waiting : session.state == .working ? Theme.working : Theme.textDisabled).cgColor
        dot.translatesAutoresizingMaskIntoConstraints = false
        dot.widthAnchor.constraint(equalToConstant: 6).isActive = true
        dot.heightAnchor.constraint(equalToConstant: 6).isActive = true
        row.addView(dot, in: .leading)
        let label = session.state == .waiting && session.waitingFor != nil ? "\(session.name) · \(session.waitingFor!)" : "\(session.name) · \(session.location)"
        row.addView(Label.make(label, size: 12, color: Theme.textPrimary), in: .leading)
        let when = Label.make(session.state == .waiting ? "waiting \(Copy.elapsed(session.since, now: now))" : Copy.elapsed(session.since, now: now), size: 11, color: Theme.textSecondary, alignment: .right)
        when.setContentCompressionResistancePriority(.required, for: .horizontal)
        row.addView(when, in: .trailing)
        return row
    }
}
