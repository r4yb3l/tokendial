import SwiftUI
import TokendialCore

/// The settings design system: the surface scale, the one brand green and the controls of the Windows
/// Chrome, as SwiftUI. The palette follows `Theme.dark` rather than keeping its own flag, so the panel and
/// the windows cannot drift apart.
enum Chrome {
    /// One look for the windows: the surface scale, the text inks and the translucent card fills built from them.
    struct Look {
        let windowBackground, surface850, surface800, surface750, surface700, surface600: Color
        let slate100, slate200, slate300, slate400, slate500, strong: Color
        let divider, titleBarFill: Color
        let sheet, sheetStrong, sheetSoft, sheetFaint: Color
        let edge, edgeSoft, line, lineSoft, lineFaint: Color
        let well, wellStrong, hoverWash, buttonEdge: Color
        let activeCard: LinearGradient
    }

    static let darkLook = Look(
        windowBackground: rgb(0x0B0E14), surface850: rgb(0x11151D), surface800: rgb(0x171C26), surface750: rgb(0x1D2331),
        surface700: rgb(0x262F40), surface600: rgb(0x384358),
        slate100: rgb(0xF1F5F9), slate200: rgb(0xE2E8F0), slate300: rgb(0xCBD5E1), slate400: rgb(0x94A3B8),
        slate500: rgb(0x64748B), strong: .white,
        divider: rgba(0x262F40, 0.6), titleBarFill: rgba(0x11151D, 0.8),
        sheet: rgba(0x11151D, 0.9), sheetStrong: rgba(0x11151D, 0.8), sheetSoft: rgba(0x11151D, 0.6), sheetFaint: rgba(0x11151D, 0.4),
        edge: rgba(0x1D2331, 0.7), edgeSoft: rgba(0x1D2331, 0.5), line: rgba(0x262F40, 0.6), lineSoft: rgba(0x262F40, 0.5), lineFaint: rgba(0x262F40, 0.4),
        well: rgba(0x171C26, 0.4), wellStrong: rgba(0x171C26, 0.8), hoverWash: rgba(0x171C26, 0.4), buttonEdge: rgba(0x384358, 0.7),
        activeCard: gradient(rgba(0x171C26, 0.85), rgba(0x11151D, 0.95)))

    static let lightLook = Look(
        windowBackground: rgb(0xF4F6FA), surface850: rgb(0xFFFFFF), surface800: rgb(0xF1F4F8), surface750: rgb(0xE8ECF2),
        surface700: rgb(0xD9DFE8), surface600: rgb(0xB9C2CF),
        slate100: rgb(0x0F172A), slate200: rgb(0x1E293B), slate300: rgb(0x334155), slate400: rgb(0x64748B),
        slate500: rgb(0x94A3B8), strong: rgb(0x0F172A),
        divider: rgba(0x0F172A, 0.10), titleBarFill: rgba(0xFFFFFF, 0.85),
        sheet: rgba(0xFFFFFF, 0.95), sheetStrong: rgba(0xFFFFFF, 0.9), sheetSoft: rgba(0xFFFFFF, 0.8), sheetFaint: rgba(0xFFFFFF, 0.6),
        edge: rgba(0xD9DFE8, 0.9), edgeSoft: rgba(0xD9DFE8, 0.6), line: rgba(0xC5CEDA, 0.8), lineSoft: rgba(0xC5CEDA, 0.6), lineFaint: rgba(0xC5CEDA, 0.4),
        well: rgba(0xE8ECF2, 0.6), wellStrong: rgb(0xE8ECF2), hoverWash: rgba(0x0F172A, 0.05), buttonEdge: rgba(0xB9C2CF, 0.8),
        activeCard: gradient(rgb(0xFFFFFF), rgb(0xF5F8FB)))

    private static var look: Look { Theme.dark ? darkLook : lightLook }

    static var windowBackground: Color { look.windowBackground }
    static var surface850: Color { look.surface850 }
    static var surface800: Color { look.surface800 }
    static var surface750: Color { look.surface750 }
    static var surface700: Color { look.surface700 }
    static var surface600: Color { look.surface600 }
    static var slate100: Color { look.slate100 }
    static var slate200: Color { look.slate200 }
    static var slate300: Color { look.slate300 }
    static var slate400: Color { look.slate400 }
    static var slate500: Color { look.slate500 }
    static var strong: Color { look.strong }
    static var divider: Color { look.divider }
    static var titleBarFill: Color { look.titleBarFill }
    static var sheet: Color { look.sheet }
    static var sheetStrong: Color { look.sheetStrong }
    static var sheetSoft: Color { look.sheetSoft }
    static var sheetFaint: Color { look.sheetFaint }
    static var edge: Color { look.edge }
    static var edgeSoft: Color { look.edgeSoft }
    static var line: Color { look.line }
    static var lineSoft: Color { look.lineSoft }
    static var lineFaint: Color { look.lineFaint }
    static var well: Color { look.well }
    static var wellStrong: Color { look.wellStrong }
    static var hoverWash: Color { look.hoverWash }
    static var buttonEdge: Color { look.buttonEdge }
    static var activeCard: LinearGradient { look.activeCard }

    static var accent: Color { Theme.UI.ample }
    static let brand500 = rgb(0x10B981)
    static let brandFaint = rgba(0x10B981, 0.10)
    static let brandSoft = rgba(0x10B981, 0.20)
    static let brandLine = rgba(0x10B981, 0.40)
    static let brandLineMid = rgba(0x10B981, 0.50)
    static let brandLineStrong = rgba(0x10B981, 0.60)
    static let onBrand = rgb(0x062B1F)
    static let titleBarHeight: CGFloat = 44

    /// The traffic lights sit here, so the header's own contents start after them.
    static let trafficLights: CGFloat = 76

    static func font(_ size: CGFloat, _ weight: Font.Weight = .regular) -> Font { .system(size: size, weight: weight) }
    static func mono(_ size: CGFloat) -> Font { .system(size: size, weight: .medium, design: .monospaced) }
    static func upper(_ text: String) -> String { text.uppercased(with: Strings.locale) }

    static func rgb(_ hex: UInt32) -> Color { rgba(hex, 1) }

    static func rgba(_ hex: UInt32, _ alpha: Double) -> Color {
        Color(.sRGB, red: Double((hex >> 16) & 0xFF) / 255, green: Double((hex >> 8) & 0xFF) / 255, blue: Double(hex & 0xFF) / 255, opacity: alpha)
    }

    private static func gradient(_ top: Color, _ bottom: Color) -> LinearGradient {
        LinearGradient(colors: [top, bottom], startPoint: .top, endPoint: .bottom)
    }
}

extension View {
    func chromeCard(_ background: Color, _ border: Color, padding: CGFloat = 14, radius: CGFloat = 12) -> some View {
        self.padding(padding)
            .background(RoundedRectangle(cornerRadius: radius).fill(background))
            .overlay(RoundedRectangle(cornerRadius: radius).stroke(border, lineWidth: 1))
    }
}

/// The header the Windows title bar became: logo, title, version badge and a centred status pill. macOS keeps
/// drawing the traffic lights on the left, and like the Windows bar it stays left to right in every language.
struct ChromeHeader<Status: View>: View {
    let title: String
    let badge: String?
    @ViewBuilder var status: () -> Status

    var body: some View {
        ZStack {
            HStack(spacing: 8) {
                ChromeLogo()
                Text(title).font(Chrome.font(12, .semibold)).foregroundStyle(Chrome.slate200)
                if let badge {
                    ChromePill(badge.uppercased(), foreground: Chrome.slate400, background: Chrome.surface800, border: Chrome.lineSoft, mono: true, size: 10, radius: 4)
                }
                Spacer()
            }
            .padding(.leading, Chrome.trafficLights)
            .padding(.trailing, 16)
            status()
        }
        .frame(height: Chrome.titleBarHeight)
        .background(Chrome.titleBarFill)
        .overlay(alignment: .bottom) { Rectangle().fill(Chrome.lineSoft).frame(height: 1) }
        .environment(\.layoutDirection, .leftToRight)
    }
}

/// A 20 pt disc with the dial arc inside and a breathing green dot on its shoulder.
struct ChromeLogo: View {
    @State private var dim = false

    var body: some View {
        ZStack(alignment: .topTrailing) {
            ZStack {
                Circle().fill(Chrome.surface800).overlay(Circle().stroke(Chrome.buttonEdge, lineWidth: 1))
                    .frame(width: 20, height: 20)
                Circle().trim(from: 0, to: 0.66 * 240 / 360)
                    .stroke(Chrome.accent, style: StrokeStyle(lineWidth: 2, lineCap: .round))
                    .rotationEffect(.degrees(150))
                    .frame(width: 12, height: 12)
            }
            .frame(width: 22, height: 22, alignment: .bottomLeading)
            Circle().fill(Chrome.windowBackground).frame(width: 10, height: 10)
                .overlay { Circle().fill(Chrome.accent).frame(width: 6, height: 6).opacity(dim ? 0.35 : 1) }
        }
        .frame(width: 22, height: 22)
        .onAppear {
            guard !Theme.reduceMotion else { return }
            withAnimation(.easeInOut(duration: 1.1).repeatForever(autoreverses: true)) { dim = true }
        }
    }
}

struct ChromePill: View {
    let text: String
    let foreground: Color
    let background: Color
    let border: Color
    var mono = false
    var size: CGFloat = 11
    var radius: CGFloat = 999

    init(_ text: String, foreground: Color, background: Color, border: Color, mono: Bool = false, size: CGFloat = 11, radius: CGFloat = 999) {
        self.text = text
        self.foreground = foreground
        self.background = background
        self.border = border
        self.mono = mono
        self.size = size
        self.radius = radius
    }

    var body: some View {
        Text(text)
            .font(mono ? Chrome.mono(size) : Chrome.font(size, .medium))
            .foregroundStyle(foreground)
            .lineLimit(1)
            .fixedSize()
            .padding(.horizontal, 8).padding(.vertical, 2)
            .background(RoundedRectangle(cornerRadius: radius).fill(background))
            .overlay(RoundedRectangle(cornerRadius: radius).stroke(border, lineWidth: 1))
    }
}

/// "PROVIDERS  (subtitle)" with something aligned to the far end.
struct ChromeSectionTitle<Right: View>: View {
    let title: String
    var subtitle: String?
    @ViewBuilder var right: () -> Right

    var body: some View {
        HStack(spacing: 8) {
            Text(Chrome.upper(title)).font(Chrome.font(13, .semibold)).foregroundStyle(Chrome.strong)
            if let subtitle {
                Text(subtitle).font(Chrome.font(12)).foregroundStyle(Chrome.slate400)
            }
            Spacer(minLength: 8)
            right()
        }
    }
}

extension ChromeSectionTitle where Right == EmptyView {
    init(_ title: String, subtitle: String? = nil) {
        self.init(title: title, subtitle: subtitle) { EmptyView() }
    }
}

/// The quieter uppercase heading of secondary sections.
struct ChromeSmallTitle: View {
    let text: String

    init(_ text: String) { self.text = text }

    var body: some View {
        Text(Chrome.upper(text)).font(Chrome.font(11, .semibold)).foregroundStyle(Chrome.slate400)
    }
}

struct ChromeRule: View {
    var body: some View { Rectangle().fill(Chrome.divider).frame(height: 1) }
}

/// One option in a picker: the value it stands for, its label and the line under it.
struct ChromeChoice<Value: Hashable>: Identifiable {
    let value: Value
    let label: String
    var hint: String?

    var id: Value { value }
}

/// A row of chips that wraps to the next line when it runs out of width, so a long list of languages does
/// not push its card wider than the column.
struct ChromeWrap: Layout {
    var spacing: CGFloat = 8

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let limit = proposal.width ?? .infinity
        var x: CGFloat = 0, y: CGFloat = 0, lineHeight: CGFloat = 0, widest: CGFloat = 0
        for view in subviews {
            let size = view.sizeThatFits(.unspecified)
            if x > 0, x + size.width > limit { x = 0; y += lineHeight + spacing; lineHeight = 0 }
            x += size.width + spacing
            widest = max(widest, x - spacing)
            lineHeight = max(lineHeight, size.height)
        }
        return CGSize(width: min(widest, limit), height: y + lineHeight)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var x: CGFloat = 0, y: CGFloat = 0, lineHeight: CGFloat = 0
        for view in subviews {
            let size = view.sizeThatFits(.unspecified)
            if x > 0, x + size.width > bounds.width { x = 0; y += lineHeight + spacing; lineHeight = 0 }
            view.place(at: CGPoint(x: bounds.minX + x, y: bounds.minY + y), proposal: ProposedViewSize(size))
            x += size.width + spacing
            lineHeight = max(lineHeight, size.height)
        }
    }
}

struct ChromeHeading: View {
    let text: String

    init(_ text: String) { self.text = text }

    var body: some View { Text(text).font(Chrome.font(13, .semibold)).foregroundStyle(Chrome.strong) }
}

struct ChromeBody: View {
    let text: String
    var size: CGFloat = 12

    init(_ text: String, size: CGFloat = 12) {
        self.text = text
        self.size = size
    }

    var body: some View {
        Text(text)
            .font(Chrome.font(size))
            .foregroundStyle(Chrome.slate400)
            .fixedSize(horizontal: false, vertical: true)
            .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// A titled card: heading, an optional line of explanation, then the body.
struct ChromeSection<Content: View>: View {
    let title: String
    var description: String?
    @ViewBuilder var content: () -> Content

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ChromeHeading(title)
            if let description { ChromeBody(description).padding(.top, 4) }
            content().padding(.top, 12)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .chromeCard(Chrome.sheetSoft, Chrome.edge, padding: 16)
    }
}

/// The check square or the radio disc: dark with a slate edge, brand green with a white glyph when on.
struct ChromeIndicator: View {
    let round: Bool
    let on: Bool
    var size: CGFloat = 16
    var glyph: CGFloat = 9

    var body: some View {
        ZStack {
            shape.fill(on ? Chrome.brand500 : (round ? Chrome.windowBackground : Chrome.surface800))
            shape.stroke(on ? Chrome.brand500 : Chrome.surface600, lineWidth: 1)
            if on {
                if round {
                    Circle().fill(.white).frame(width: glyph, height: glyph)
                } else {
                    Image(systemName: "checkmark").font(.system(size: glyph, weight: .heavy)).foregroundStyle(.white)
                }
            }
        }
        .frame(width: size, height: size)
    }

    private var shape: AnyShape { round ? AnyShape(Circle()) : AnyShape(RoundedRectangle(cornerRadius: 4)) }
}

private struct ChromeCaption: View {
    let label: String
    let hint: String?
    var strong = false
    var hintSize: CGFloat = 11

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(label)
                .font(Chrome.font(12, strong ? .semibold : .medium))
                .foregroundStyle(strong ? Chrome.strong : Chrome.slate200)
            if let hint {
                Text(hint).font(Chrome.font(hintSize)).foregroundStyle(Chrome.slate400).fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

struct ChromeCheck: View {
    let label: String
    var hint: String?
    @Binding var isOn: Bool
    @State private var hover = false

    var body: some View {
        Button {
            isOn.toggle()
        } label: {
            HStack(alignment: .top, spacing: 10) {
                ChromeIndicator(round: false, on: isOn)
                    .overlay { if hover, !isOn { RoundedRectangle(cornerRadius: 4).stroke(Chrome.slate400, lineWidth: 1) } }
                    .padding(.top, 1)
                ChromeCaption(label: label, hint: hint)
                Spacer(minLength: 0)
            }
            .padding(.vertical, 2)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
    }
}

/// A radio row that lights up under the cursor.
struct ChromeRadio: View {
    let label: String
    var hint: String?
    let selected: Bool
    let action: () -> Void
    @State private var hover = false

    var body: some View {
        Button(action: action) {
            HStack(alignment: .top, spacing: 10) {
                ChromeIndicator(round: true, on: selected, size: 15, glyph: 6).padding(.top, 1)
                ChromeCaption(label: label, hint: hint)
                Spacer(minLength: 0)
            }
            .padding(8)
            .background(RoundedRectangle(cornerRadius: 8).fill(hover ? Chrome.hoverWash : .clear))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
    }
}

/// A whole card that is one option: bordered in brand green when chosen.
struct ChromeRadioCard: View {
    let label: String
    var hint: String?
    let selected: Bool
    let action: () -> Void
    @State private var hover = false

    var body: some View {
        Button(action: action) {
            HStack(alignment: .top, spacing: 14) {
                ChromeIndicator(round: true, on: selected, size: 16, glyph: 6).padding(.top, 1)
                ChromeCaption(label: label, hint: hint, strong: selected)
                Spacer(minLength: 0)
            }
            .chromeCard(selected ? Chrome.sheet : Chrome.sheetFaint,
                        selected ? Chrome.brandLine : (hover ? Chrome.surface600 : Chrome.surface750),
                        padding: 12)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
    }
}

/// A small selectable chip, for the language, theme and position pickers.
struct ChromeChip: View {
    let label: String
    let selected: Bool
    let action: () -> Void
    @State private var hover = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 6) {
                ChromeIndicator(round: true, on: selected, size: 12, glyph: 4)
                Text(label).font(Chrome.font(11, .medium)).foregroundStyle(selected || hover ? Chrome.slate200 : Chrome.slate400)
            }
            .padding(.horizontal, 10).padding(.vertical, 6)
            .background(RoundedRectangle(cornerRadius: 8).fill(selected ? Chrome.wellStrong : Chrome.well))
            .overlay(RoundedRectangle(cornerRadius: 8).stroke(selected ? Chrome.brandLineMid : Chrome.line, lineWidth: 1))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
    }
}

struct ChromeButton: View {
    let label: String
    var primary = false
    let action: () -> Void
    @State private var hover = false

    var body: some View {
        Button(action: action) {
            Text(label)
                .font(Chrome.font(12, primary ? .semibold : .medium))
                .foregroundStyle(primary ? Chrome.onBrand : Chrome.slate200)
                .lineLimit(1)
                .fixedSize()
                .padding(.horizontal, 10).padding(.vertical, 5)
                .background(RoundedRectangle(cornerRadius: 8).fill(fill))
                .overlay(RoundedRectangle(cornerRadius: 8).stroke(primary ? Chrome.brand500 : Chrome.buttonEdge, lineWidth: 1))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
    }

    private var fill: Color {
        if primary { return hover ? Chrome.accent : Chrome.brand500 }
        return hover ? Chrome.surface700 : Chrome.surface750
    }
}

/// A full-width action with an icon before its label.
struct ChromeWideButton: View {
    let icon: String
    let label: String
    let action: () -> Void
    @State private var hover = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 8) {
                Image(systemName: icon).font(.system(size: 13, weight: .medium)).foregroundStyle(Chrome.accent)
                Text(label).font(Chrome.font(12, .medium)).foregroundStyle(Chrome.slate200)
            }
            .frame(maxWidth: .infinity)
            .padding(.horizontal, 12).padding(.vertical, 8)
            .background(RoundedRectangle(cornerRadius: 12).fill(hover ? Chrome.surface750 : Chrome.surface800))
            .overlay(RoundedRectangle(cornerRadius: 12).stroke(Chrome.surface700, lineWidth: 1))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
    }
}

/// A right-aligned monospaced field in a rounded well; the border turns green while it has focus, and the
/// value is handed over on Enter or when focus leaves, never on every keystroke.
struct ChromeField: View {
    @State private var text: String
    private let width: CGFloat
    private let commit: (String) -> Void
    @FocusState private var focused: Bool

    init(_ value: String, width: CGFloat = 120, commit: @escaping (String) -> Void) {
        _text = State(initialValue: value)
        self.width = width
        self.commit = commit
    }

    var body: some View {
        TextField("", text: $text)
            .textFieldStyle(.plain)
            .font(Chrome.mono(12))
            .foregroundStyle(Chrome.slate200)
            .multilineTextAlignment(.trailing)
            .focused($focused)
            .onSubmit { commit(text) }
            .onChange(of: focused) { _, now in if !now { commit(text) } }
            .padding(.horizontal, 8).padding(.vertical, 4)
            .frame(width: width)
            .background(RoundedRectangle(cornerRadius: 8).fill(Chrome.windowBackground))
            .overlay(RoundedRectangle(cornerRadius: 8).stroke(focused ? Chrome.brand500 : Chrome.surface700, lineWidth: 1))
    }
}

struct ChromeRow<Control: View>: View {
    let label: String
    var hint: String?
    @ViewBuilder var control: () -> Control

    var body: some View {
        HStack(alignment: .center, spacing: 12) {
            VStack(alignment: .leading, spacing: 1) {
                Text(label).font(Chrome.font(12)).foregroundStyle(Chrome.slate300)
                if let hint { Text(hint).font(Chrome.font(11)).foregroundStyle(Chrome.slate400) }
            }
            Spacer(minLength: 8)
            control()
        }
        .padding(.top, 6)
    }
}
