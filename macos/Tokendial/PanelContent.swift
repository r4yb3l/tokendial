import AppKit
import TokendialCore

/// Labels in the panel's type scale, with tabular figures so numbers do not jitter.
enum Label {
    static func make(_ text: String, size: CGFloat, color: NSColor, weight: NSFont.Weight = .regular, alignment: NSTextAlignment = .left) -> NSTextField {
        let field = NSTextField(labelWithString: text)
        field.font = Theme.font(size, weight: weight)
        field.textColor = color
        field.alignment = alignment
        field.lineBreakMode = .byTruncatingTail
        field.maximumNumberOfLines = 1
        field.translatesAutoresizingMaskIntoConstraints = false
        field.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        return field
    }
}

/// A thin bar: track and fill, fill width set by fraction.
final class BarView: NSView {
    private let fill = NSView()
    private let height: CGFloat
    private var fraction: Double = 0

    init(height: CGFloat) {
        self.height = height
        super.init(frame: .zero)
        wantsLayer = true
        layer?.backgroundColor = Theme.track.cgColor
        layer?.cornerRadius = height / 2
        fill.wantsLayer = true
        fill.layer?.cornerRadius = height / 2
        addSubview(fill)
        translatesAutoresizingMaskIntoConstraints = false
        heightAnchor.constraint(equalToConstant: height).isActive = true
    }

    required init?(coder: NSCoder) { fatalError() }

    func set(_ fraction: Double, color: NSColor) {
        self.fraction = min(1, max(0, fraction))
        fill.layer?.backgroundColor = color.cgColor
        needsLayout = true
    }

    override func layout() {
        super.layout()
        fill.frame = NSRect(x: 0, y: 0, width: bounds.width * CGFloat(fraction), height: height)
    }
}

/// One provider in the expanded capsule: dial with mark and percent, name, headline label, thin bars for the other windows.
final class CellView: NSView {
    let id: String
    private let dial = DialView(diameter: Theme.expandedDial, stroke: Theme.expandedStroke)
    private let activity = ActivityArcView()
    private let mark: NSTextField
    private let percent = Label.make("", size: 15, color: Theme.textPrimary, weight: .semibold, alignment: .center)
    private let name = Label.make("", size: 11, color: Theme.textPrimary, weight: .semibold, alignment: .center)
    private let label = Label.make("", size: 10, color: Theme.textSecondary, alignment: .center)
    private let bars = NSStackView()
    var onClick: (() -> Void)?

    init(id: String, mark markText: String) {
        self.id = id
        mark = Label.make(markText, size: 9, color: Theme.textSecondary, weight: .semibold, alignment: .center)
        super.init(frame: .zero)
        translatesAutoresizingMaskIntoConstraints = false
        widthAnchor.constraint(equalToConstant: Theme.cellWidth).isActive = true

        let dialHost = NSView()
        dialHost.translatesAutoresizingMaskIntoConstraints = false
        dialHost.addSubview(dial)
        dialHost.addSubview(activity)
        dialHost.addSubview(mark)
        dialHost.addSubview(percent)
        addSubview(dialHost)
        for field in [name, label] {
            field.setContentCompressionResistancePriority(.defaultHigh, for: .horizontal)
            field.lineBreakMode = .byClipping
        }
        addSubview(name)
        addSubview(label)
        bars.orientation = .vertical
        bars.spacing = 2
        bars.translatesAutoresizingMaskIntoConstraints = false
        addSubview(bars)

        NSLayoutConstraint.activate([
            dialHost.topAnchor.constraint(equalTo: topAnchor),
            dialHost.centerXAnchor.constraint(equalTo: centerXAnchor),
            dialHost.widthAnchor.constraint(equalToConstant: Theme.expandedDial),
            dialHost.heightAnchor.constraint(equalToConstant: Theme.expandedDial),
            dial.centerXAnchor.constraint(equalTo: dialHost.centerXAnchor),
            dial.centerYAnchor.constraint(equalTo: dialHost.centerYAnchor),
            activity.centerXAnchor.constraint(equalTo: dialHost.centerXAnchor),
            activity.centerYAnchor.constraint(equalTo: dialHost.centerYAnchor),
            mark.centerXAnchor.constraint(equalTo: dialHost.centerXAnchor),
            mark.topAnchor.constraint(equalTo: dialHost.topAnchor, constant: 13),
            percent.centerXAnchor.constraint(equalTo: dialHost.centerXAnchor),
            percent.topAnchor.constraint(equalTo: dialHost.topAnchor, constant: 24),
            percent.widthAnchor.constraint(equalToConstant: 48),
            name.topAnchor.constraint(equalTo: dialHost.bottomAnchor, constant: 1),
            name.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 2),
            name.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -2),
            label.topAnchor.constraint(equalTo: name.bottomAnchor),
            label.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 2),
            label.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -2),
            bars.topAnchor.constraint(equalTo: label.bottomAnchor, constant: 3),
            bars.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 18),
            bars.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -18),
            bars.bottomAnchor.constraint(lessThanOrEqualTo: bottomAnchor)
        ])
    }

    required init?(coder: NSCoder) { fatalError() }

    override func mouseUp(with event: NSEvent) { onClick?() }

    func apply(_ tile: Tile, animated: Bool) {
        dial.hollow = !tile.hasReading
        dial.color = Theme.color(tile.band)
        dial.set(tile.hasReading ? (tile.fraction ?? 0) : 0, animated: animated)
        if tile.hasReading, let f = tile.fraction { percent.stringValue = "\(Int((f * 100).rounded()))%" }
        else if tile.hasReading, let c = tile.headline?.count { percent.stringValue = String(c) }
        else { percent.stringValue = "–" }
        percent.textColor = tile.hasReading ? Theme.textPrimary : Theme.textDisabled
        mark.textColor = tile.hasReading ? Theme.textSecondary : Theme.textDisabled
        name.stringValue = tile.name
        name.textColor = tile.hasReading ? Theme.textPrimary : Theme.textDisabled
        label.stringValue = tile.hasReading ? tile.headlineLabel : tile.statusLabel
        bars.views.forEach { bars.removeView($0) }
        if tile.hasReading {
            for window in tile.secondary.prefix(2) {
                guard let used = window.usedFraction else { continue }
                let bar = BarView(height: 3)
                bar.set(used, color: Theme.color(Band.of(used)))
                bars.addView(bar, in: .top)
                bar.widthAnchor.constraint(equalTo: bars.widthAnchor).isActive = true
            }
        }
        activity.show(tile.activity?.state)
    }
}

/// The compact row of mini dials and the expanded grid of cells. Dials are kept between updates so readings glide instead of jumping.
final class PanelContentView: NSView {
    let compactRow = NSStackView()
    let expanded = NSView()
    private let cellRow = NSStackView()
    private let sessionsLine = Label.make("", size: 11, color: Theme.textSecondary, alignment: .center)
    private let mutedBadge = NSView()
    private var compactDials: [String: DialView] = [:]
    private var cells: [String: CellView] = [:]
    private var order: [String] = []
    var onCellClick: ((String) -> Void)?

    override init(frame: NSRect) {
        super.init(frame: frame)
        translatesAutoresizingMaskIntoConstraints = false
        compactRow.orientation = .horizontal
        compactRow.spacing = Theme.compactSpacing
        compactRow.alignment = .centerY
        compactRow.translatesAutoresizingMaskIntoConstraints = false
        addSubview(compactRow)
        expanded.translatesAutoresizingMaskIntoConstraints = false
        expanded.alphaValue = 0
        expanded.isHidden = true
        addSubview(expanded)
        cellRow.orientation = .horizontal
        cellRow.spacing = 0
        cellRow.alignment = .top
        cellRow.translatesAutoresizingMaskIntoConstraints = false
        expanded.addSubview(cellRow)
        expanded.addSubview(sessionsLine)
        mutedBadge.wantsLayer = true
        mutedBadge.layer?.backgroundColor = Theme.watch.cgColor
        mutedBadge.layer?.cornerRadius = 4
        mutedBadge.translatesAutoresizingMaskIntoConstraints = false
        mutedBadge.widthAnchor.constraint(equalToConstant: 8).isActive = true
        mutedBadge.heightAnchor.constraint(equalToConstant: 8).isActive = true
        mutedBadge.isHidden = true
        NSLayoutConstraint.activate([
            compactRow.centerXAnchor.constraint(equalTo: centerXAnchor),
            compactRow.centerYAnchor.constraint(equalTo: centerYAnchor),
            expanded.leadingAnchor.constraint(equalTo: leadingAnchor),
            expanded.trailingAnchor.constraint(equalTo: trailingAnchor),
            expanded.topAnchor.constraint(equalTo: topAnchor),
            expanded.bottomAnchor.constraint(equalTo: bottomAnchor),
            cellRow.topAnchor.constraint(equalTo: expanded.topAnchor, constant: 10),
            cellRow.centerXAnchor.constraint(equalTo: expanded.centerXAnchor),
            cellRow.heightAnchor.constraint(equalToConstant: 96),
            sessionsLine.topAnchor.constraint(equalTo: cellRow.bottomAnchor, constant: 2),
            sessionsLine.leadingAnchor.constraint(equalTo: expanded.leadingAnchor, constant: Theme.expandedPadding),
            sessionsLine.trailingAnchor.constraint(equalTo: expanded.trailingAnchor, constant: -Theme.expandedPadding)
        ])
    }

    required init?(coder: NSCoder) { fatalError() }

    var count: Int { order.count }
    var cellViews: [CellView] { order.compactMap { cells[$0] } }

    func apply(_ model: PanelModel, now: Date, animated: Bool) {
        let ids = model.tiles.map { $0.id }
        if ids != order {
            order = ids
            rebuild(model)
        }
        for tile in model.tiles {
            let fraction = tile.hasReading ? min(1, max(0, tile.fraction ?? 0)) : 0
            if let mini = compactDials[tile.id] {
                mini.hollow = !tile.hasReading
                mini.color = Theme.color(tile.band)
                mini.set(fraction, animated: animated)
            }
            cells[tile.id]?.apply(tile, animated: animated)
        }
        mutedBadge.isHidden = model.muted == 0
        sessionsLine.stringValue = PanelModel.sessionsCopy(model.sessions, now: now)
        sessionsLine.textColor = model.anyWaiting ? Theme.waiting : Theme.textSecondary
    }

    /// After a theme switch: repaint what is kept between updates, and drop the cells so the next apply rebuilds them.
    func retheme() {
        mutedBadge.layer?.backgroundColor = Theme.watch.cgColor
        sessionsLine.textColor = Theme.textSecondary
        order = []
    }

    private func rebuild(_ model: PanelModel) {
        compactRow.views.forEach { compactRow.removeView($0) }
        cellRow.views.forEach { cellRow.removeView($0) }
        compactDials = [:]
        cells = [:]
        if model.tiles.isEmpty {
            compactRow.addView(Label.make("Tokendial", size: 11, color: Theme.textDisabled), in: .center)
            let empty = Label.make("No providers connected. Right-click for settings.", size: 11, color: Theme.textSecondary, alignment: .center)
            empty.widthAnchor.constraint(equalToConstant: Theme.cellWidth * 2.6).isActive = true
            cellRow.addView(empty, in: .center)
        }
        for tile in model.tiles {
            let mini = DialView(diameter: Theme.compactDial, stroke: Theme.compactStroke)
            compactDials[tile.id] = mini
            compactRow.addView(mini, in: .center)
            let cell = CellView(id: tile.id, mark: tile.mark)
            cell.onClick = { [weak self] in self?.onCellClick?(tile.id) }
            cells[tile.id] = cell
            cellRow.addView(cell, in: .center)
        }
        compactRow.addView(mutedBadge, in: .center)
    }
}
