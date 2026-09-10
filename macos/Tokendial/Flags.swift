import SwiftUI

/// A 16x11 flag for the language picker, drawn from bands and crosses.
///
/// Windows cannot use flag emoji at all - Segoe UI Emoji carries no regional indicator glyphs, so a flag
/// renders there as its two letters in boxes - and drawing them keeps the two platforms identical instead
/// of letting macOS drift into emoji. Arabic is not a country, so it gets its script letter rather than a
/// flag of any one state.
struct Flag: View {
    let code: String?

    private static let width: CGFloat = 16
    private static let height: CGFloat = 11

    var body: some View {
        face
            .frame(width: Self.width, height: Self.height)
            .clipShape(RoundedRectangle(cornerRadius: 2))
            .overlay(RoundedRectangle(cornerRadius: 2).stroke(Chrome.line, lineWidth: 0.5))
    }

    @ViewBuilder private var face: some View {
        switch code {
        case "en": unitedStates
        case "en-GB": unitedKingdom
        case "es": bands([(rgb(0xC60B1E), 3), (rgb(0xFFC400), 5), (rgb(0xC60B1E), 3)])
        case "de": bands([(rgb(0x000000), 4), (rgb(0xDD0000), 4), (rgb(0xFFCE00), 3)])
        case "fr": thirds([rgb(0x002654), .white, rgb(0xED2939)])
        default: script
        }
    }

    private var unitedStates: some View {
        ZStack(alignment: .topLeading) {
            Color.white
            VStack(spacing: 1.06) {
                ForEach(0..<6, id: \.self) { _ in
                    Rectangle().fill(rgb(0xB22234)).frame(height: 0.95)
                }
            }
            Rectangle().fill(rgb(0x3C3B6E)).frame(width: 7, height: 5.5)
        }
    }

    private var unitedKingdom: some View {
        ZStack {
            rgb(0x012169)
            saltire(.white, 1.9)
            saltire(rgb(0xC8102E), 0.9)
            Rectangle().fill(Color.white).frame(height: 3.4)
            Rectangle().fill(Color.white).frame(width: 3.4)
            Rectangle().fill(rgb(0xC8102E)).frame(height: 1.8)
            Rectangle().fill(rgb(0xC8102E)).frame(width: 1.8)
        }
    }

    private func saltire(_ colour: Color, _ thickness: CGFloat) -> some View {
        ZStack {
            Rectangle().fill(colour).frame(width: 21, height: thickness).rotationEffect(.degrees(34.5))
            Rectangle().fill(colour).frame(width: 21, height: thickness).rotationEffect(.degrees(-34.5))
        }
    }

    private func bands(_ rows: [(Color, CGFloat)]) -> some View {
        VStack(spacing: 0) {
            ForEach(Array(rows.enumerated()), id: \.offset) { row in
                Rectangle().fill(row.element.0).frame(height: row.element.1)
            }
        }
    }

    private func thirds(_ columns: [Color]) -> some View {
        HStack(spacing: 0) {
            ForEach(Array(columns.enumerated()), id: \.offset) { column in
                Rectangle().fill(column.element)
            }
        }
    }

    private var script: some View {
        Chrome.well.overlay {
            Text("\u{0639}").font(Chrome.font(9, .semibold)).foregroundStyle(Chrome.slate400)
        }
    }

    private func rgb(_ value: Int) -> Color {
        Color(red: Double((value >> 16) & 0xFF) / 255, green: Double((value >> 8) & 0xFF) / 255, blue: Double(value & 0xFF) / 255)
    }
}
