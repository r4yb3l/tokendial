import Foundation

/// The shape of GET /api/oauth/usage. limits[] is preferred; the named windows fill in what it drops at a rollover.
public enum ClaudeUsage {
    public static func parse(_ body: Data) throws -> Parsed {
        guard let root = JSON.object(body) else { throw UsageError.badResponse(0) }
        var windows: [UsageWindow] = []
        for limit in root.objects("limits") {
            guard let kind = limit.str("kind"), let resetsAt = limit.date("resets_at") else { continue }
            windows.append(UsageWindow(id: kind, label: label(kind, limit), usedFraction: (limit.num("percent") ?? 0) / 100, resetsAt: resetsAt))
        }
        func merge(_ window: JSONObject?, _ id: String, _ label: String) {
            guard let window, let at = window.date("resets_at"), !windows.contains(where: { $0.id == id }) else { return }
            windows.append(UsageWindow(id: id, label: label, usedFraction: (window.num("utilization") ?? 0) / 100, resetsAt: at))
        }
        merge(root.obj("five_hour"), "session", "Current session")
        merge(root.obj("seven_day"), "weekly_all", "All models")
        let sorted = windows.sorted { a, b in
            if rank(a) != rank(b) { return rank(a) < rank(b) }
            return a.id.utf8.lexicographicallyPrecedes(b.id.utf8)
        }
        return Parsed(windows: sorted, headline: "session")
    }

    public static func label(_ kind: String, _ limit: JSONObject) -> String {
        if kind.hasSuffix("_scoped"), let model = limit.obj("scope")?.obj("model")?.str("display_name") { return model }
        switch kind {
        case "session": return "Current session"
        case "weekly_all": return "All models"
        case "weekly_opus": return "Opus"
        case "weekly_sonnet": return "Sonnet"
        default: return kind.replacingOccurrences(of: "weekly_", with: "").replacingOccurrences(of: "_", with: " ").lowercased().capitalized
        }
    }

    private static func rank(_ w: UsageWindow) -> Int {
        switch w.id { case "session": return 0; case "weekly_all": return 1; default: return 2 }
    }
}

/// GET /backend-api/wham/usage: primary and secondary windows, labelled by their length.
public enum CodexUsage {
    public static func parse(_ body: Data, now: Date) throws -> Parsed {
        guard let root = JSON.object(body) else { throw UsageError.badResponse(0) }
        guard let rateLimit = root.obj("rate_limit") else { throw UsageError.nothingMetered("Codex reported no usage windows") }
        var windows: [UsageWindow] = []
        for (id, key) in [("primary", "primary_window"), ("secondary", "secondary_window")] {
            guard let window = rateLimit.obj(key) else { continue }
            guard let seconds = window.num("limit_window_seconds"), let used = window.num("used_percent") else { throw UsageError.badResponse(0) }
            let resetsAt = window.epochSeconds("reset_at") ?? window.num("reset_after_seconds").map { now.addingTimeInterval($0) }
            windows.append(UsageWindow(id: id, label: label(seconds, primary: id == "primary"), usedFraction: used / 100, resetsAt: resetsAt))
        }
        if windows.isEmpty { throw UsageError.nothingMetered("Codex reported no usage windows") }
        return Parsed(windows: windows, headline: windows[0].id)
    }

    public static func label(_ seconds: Double, primary: Bool) -> String {
        if seconds <= 0 { return primary ? "Current session" : "Longer window" }
        let minutes = seconds / 60
        if minutes < 60 { return "\(Int(minutes))m limit" }
        if minutes < 1440 { return "\(Int(minutes / 60))h limit" }
        let days = Int((minutes / 1440).rounded())
        switch days { case 7: return "Weekly limit"; case 30: return "Monthly limit"; default: return "\(days)d limit" }
    }
}

/// GET /api/usage-summary. Zero is a reading; an empty plan is not.
public enum CursorUsage {
    public static func parse(_ body: Data) throws -> Parsed {
        guard let root = JSON.object(body) else { throw UsageError.badResponse(0) }
        let resetsAt = root.date("billingCycleEnd")
        let individual = root.obj("individualUsage")
        let plan = individual?.obj("plan")
        var windows: [UsageWindow] = []
        if let total = plan?.num("totalPercentUsed") { windows.append(UsageWindow(id: "included", label: "Included usage", usedFraction: total / 100, resetsAt: resetsAt)) }
        if let api = plan?.num("apiPercentUsed"), api > 0 { windows.append(UsageWindow(id: "api", label: "API usage", usedFraction: api / 100, resetsAt: resetsAt)) }
        if let onDemand = individual?.obj("onDemand"), onDemand.bool("enabled") == true, let limit = onDemand.num("limit"), limit > 0, let used = onDemand.num("used") {
            windows.append(UsageWindow(id: "on_demand", label: "On demand", usedFraction: used / limit, resetsAt: resetsAt))
        }
        if !windows.isEmpty { return Parsed(windows: windows, headline: "included") }
        let membership = root.str("membershipType") ?? "this"
        throw UsageError.nothingMetered(root.bool("isUnlimited") == true
            ? "Unlimited on the \(membership) plan — nothing to meter"
            : "The \(membership) plan has nothing for Cursor to meter yet")
    }
}

/// Errors ride under HTTP 200; window identity comes from length, never from meter type.
public enum GlmUsage {
    public static func parse(_ body: Data) throws -> Parsed {
        guard let root = JSON.object(body) else { throw UsageError.badResponse(0) }
        let code = root.num("code").map { Int($0) }
        if !(root.bool("success") == true || code == nil || code == 200) {
            switch code {
            case 401, 403: throw UsageError.needsSignIn()
            case 429: throw UsageError.rateLimited(0)
            case .some(let other): throw UsageError.badResponse(other)
            case .none: throw UsageError.badResponse(200)
            }
        }
        let data = root.obj("data")
        var windows: [UsageWindow] = []
        for limit in data?.objects("limits") ?? [] {
            guard let pct = limit.num("percentage") else { continue }
            let id = self.id(limit)
            windows.append(UsageWindow(id: id, label: label(id, limit), usedFraction: pct / 100, resetsAt: limit.epochMillis("nextResetTime")))
        }
        let sorted = windows.sorted { a, b in
            if rank(a) != rank(b) { return rank(a) < rank(b) }
            return a.id.utf8.lexicographicallyPrecedes(b.id.utf8)
        }
        return Parsed(windows: sorted, headline: "session", plan: data?.str("level"))
    }

    public static func id(_ limit: JSONObject) -> String {
        if limit.str("type") == "TIME_LIMIT" { return "mcp" }
        switch (limit.num("unit"), limit.num("number")) {
        case (3, 5): return "session"
        case (6, 1): return "weekly"
        case (.some(let u), .some(let n)): return "window-\(Int(u))x\(Int(n))"
        default: return limit.str("type")?.lowercased() ?? "unknown"
        }
    }

    private static func label(_ id: String, _ limit: JSONObject) -> String {
        switch id {
        case "session": return "Current session"
        case "weekly": return "Weekly"
        case "mcp": return "MCP (1 month)"
        case _ where id.hasPrefix("window-"):
            switch limit.num("unit") {
            case 3: return "Usage (\(Int(limit.num("number") ?? 0)) h)"
            case 6: return "Usage (\(Int(limit.num("number") ?? 0)) wk)"
            default: return "Usage"
            }
        default: return "Usage"
        }
    }

    private static func rank(_ w: UsageWindow) -> Int {
        switch w.id { case "session": return 0; case "weekly": return 1; case "mcp": return 2; default: return 3 }
    }
}

/// GET /v1/billing?format=credits: one credits window, labelled after the product.
public enum GrokUsage {
    public static func parse(_ body: Data) throws -> Parsed {
        guard let root = JSON.object(body), let config = root.obj("config") else { throw UsageError.badResponse(0) }
        let resetsAt = config.obj("currentPeriod")?.date("end") ?? config.date("billingPeriodEnd")
        let products = config.objects("productUsage")
        var windows: [UsageWindow] = []
        if let pct = config.num("creditUsagePercent") {
            windows.append(UsageWindow(id: "credits", label: humanize(products.first?.str("product")) ?? "Grok Build", usedFraction: pct / 100, resetsAt: resetsAt))
        } else {
            for product in products {
                guard let used = product.num("usagePercent") else { continue }
                let name = humanize(product.str("product"))
                windows.append(UsageWindow(id: windows.isEmpty ? "credits" : (product.str("product") ?? name ?? "usage"), label: name ?? "Usage", usedFraction: used / 100, resetsAt: resetsAt))
            }
        }
        if windows.isEmpty { throw UsageError.nothingMetered("Grok has nothing metered on this account yet") }
        return Parsed(windows: windows, headline: "credits")
    }

    /// GrokBuild → Grok Build.
    public static func humanize(_ name: String?) -> String? {
        guard let name, !name.isEmpty else { return nil }
        var out = ""
        for (i, c) in name.enumerated() {
            if i > 0 && c.isUppercase { out.append(" ") }
            out.append(c)
        }
        return out
    }
}

/// GET /zen/go/v1/usage: a fixed table of three windows in headline order.
public enum OpenCodeUsage {
    private static let table: [(String, String)] = [("rolling", "5h limit"), ("weekly", "Weekly limit"), ("monthly", "Monthly limit")]

    public static func parse(_ body: Data) throws -> Parsed {
        guard let root = JSON.object(body), let usage = root.obj("usage") else { throw UsageError.badResponse(0) }
        var windows: [UsageWindow] = []
        for (key, label) in table {
            guard let entry = usage.obj(key), let pct = entry.num("percent") else { continue }
            windows.append(UsageWindow(id: key, label: label, usedFraction: pct / 100, resetsAt: entry.date("resetsAt")))
        }
        if windows.isEmpty { throw UsageError.badResponse(0) }
        return Parsed(windows: windows, headline: "rolling")
    }
}

/// Antigravity: the language server reports what remains; the dial shows what is used.
public enum AntigravityUsage {
    public static func parseBridge(_ body: Data) -> [UsageWindow] {
        guard let root = JSON.object(body) else { return [] }
        var windows: [UsageWindow] = []
        for group in root.obj("response")?.objects("groups") ?? [] {
            let groupName = group.str("displayName")
            for bucket in group.objects("buckets") {
                guard let remaining = bucket.num("remainingFraction"), remaining >= 0, remaining <= 1 else { continue }
                windows.append(UsageWindow(id: bucket.str("bucketId") ?? groupName ?? "quota", label: groupName ?? bucket.str("displayName") ?? "Usage", usedFraction: 1 - remaining, resetsAt: bucket.date("resetTime")))
            }
        }
        return windows
    }

    /// Never validated against a licensed response, so paranoid about bounds.
    /// The same summary Gemini CLI reads, so the parsing lives once, in CodeAssist.
    public static func parseGoogleQuota(_ body: Data) -> [UsageWindow] { CodeAssist.parseQuotaSummary(body) }

    /// The Google sign-in Antigravity keeps through Go's keyring: JSON blob, optional base64 wrapper.
    public struct Credential: Equatable {
        public var authMethod: String
        public var accessToken: String
        public var expiresAt: Date

        public static func decode(_ text: String) -> Credential? {
            var text = text.trimmingCharacters(in: .whitespacesAndNewlines)
            let prefix = "go-keyring-base64:"
            if text.hasPrefix(prefix) { text = String(text.dropFirst(prefix.count)) }
            if let direct = fromJSON(Data(text.utf8)) { return direct }
            guard let decoded = Data(base64Encoded: text) else { return nil }
            return fromJSON(decoded)
        }

        private static func fromJSON(_ data: Data) -> Credential? {
            guard let root = JSON.object(data), let token = root.obj("token"), let access = token.str("access_token") else { return nil }
            return Credential(authMethod: root.str("auth_method") ?? "", accessToken: access, expiresAt: token.date("expiry") ?? .distantPast)
        }
    }
}
