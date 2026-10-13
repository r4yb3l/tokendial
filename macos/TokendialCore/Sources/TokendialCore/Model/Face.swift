import Foundation

/// What the floating face shows: the arc takes the one provider closest to running out, colour and fill
/// both from it, and the glyphs take that band together with the strongest session state.
/// The table is written down in docs/design/tokens.md, which every platform draws from.
public struct Face: Equatable, Sendable {
    public var band: Band?
    public var fraction: Double
    public var mood: SessionState?

    public static let empty = Face(band: nil, fraction: 0, mood: nil)

    /// Eyes shut for the length of a blink.
    public static let blink = "- -"

    public init(band: Band?, fraction: Double, mood: SessionState?) {
        self.band = band
        self.fraction = fraction
        self.mood = mood
    }

    public static func of(_ readings: [ProviderReading], sessions: [AgentSession]) -> Face {
        let readable = readings.filter { r in
            guard r.hasReading, r.band != nil else { return false }
            switch r.status { case .needsSignIn, .unsupported: return false; default: return true }
        }
        let worst = readable.max { a, b in
            rank(a.band) != rank(b.band) ? rank(a.band) < rank(b.band) : fill(a) < fill(b)
        }
        return Face(band: worst?.band, fraction: worst.map(fill) ?? 0, mood: Activity.of(sessions)?.state)
    }

    /// A blocked provider cannot be used however much its headline says is left, so it reads as spent.
    private static func fill(_ reading: ProviderReading) -> Double {
        reading.block != nil ? 1 : min(1, max(0, reading.headlineFraction ?? 0))
    }

    private static func rank(_ band: Band?) -> Int {
        switch band { case .critical: return 2; case .watch: return 1; default: return 0 }
    }

    public var spent: Bool { fraction >= 1 }

    public var eyes: String {
        if spent { return "x x" }
        if mood == .waiting { return "O O" }
        if mood == .working { return "o o" }
        if band == .critical { return "> <" }
        if band == .watch { return "- -" }
        return "^ ^"
    }

    public var mouth: String {
        if spent { return "_" }
        if mood == .waiting { return "o" }
        if band == .critical { return "~" }
        if band == .watch { return "_" }
        return "◡"
    }
}
