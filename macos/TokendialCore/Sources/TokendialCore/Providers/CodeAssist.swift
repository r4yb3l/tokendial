import Foundation

/// What the Code Assist gate says about an account: which project bills it, which tier it is on, and whether
/// it may use the client at all.
public struct CodeAssistGate: Equatable, Sendable {
    public var project: String?
    public var tier: String?
    public var eligible: Bool
    public var reason: String?
}

/// Google's Code Assist API on cloudcode-pa.googleapis.com, shared by Antigravity and Gemini CLI. Only
/// transport and parsing live here; each provider decides what a status means for its own cached credential.
public enum CodeAssist {
    public static let gate = URL(string: "https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist")!
    public static let quota = URL(string: "https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota")!
    public static let quotaSummary = URL(string: "https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuotaSummary")!

    /// POST a JSON body with a bearer token; the caller maps the status.
    public static func post(_ transport: Transport, _ endpoint: URL, token: String, body: String) async throws -> (Data, HTTPURLResponse) {
        let request = HTTP.request(endpoint, method: "POST", headers: ["Authorization": "Bearer \(token)"], body: Data(body.utf8))
        return try await transport.send(request)
    }

    /// The project may be a string or an object, the tier a name; eligible means some tier is current or
    /// allowed. An ineligible answer only contributes its reason.
    public static func parseGate(_ body: Data) -> CodeAssistGate {
        guard let root = JSON.object(body) else { return CodeAssistGate(project: nil, tier: nil, eligible: false, reason: nil) }
        let project = root.str("cloudaicompanionProject")
            ?? root.obj("cloudaicompanionProject").flatMap { $0.str("id") ?? $0.str("projectId") }
        let current = root.obj("currentTier")
        let paid = root.obj("paidTier")
        let tier = paid?.str("name") ?? paid?.str("id") ?? current?.str("name") ?? current?.str("id")
        let eligible = current != nil || !root.objects("allowedTiers").isEmpty
        let reason = root.objects("ineligibleTiers").compactMap { $0.str("reasonMessage") ?? $0.str("reasonCode") }.first
        return CodeAssistGate(project: project, tier: tier, eligible: eligible, reason: reason)
    }

    /// retrieveUserQuota: one bucket per model with the fraction still left and when it refills.
    public static func parseQuotaBuckets(_ body: Data) -> [UsageWindow] {
        guard let root = JSON.object(body) else { return [] }
        var windows: [UsageWindow] = []
        for bucket in root.objects("buckets") {
            guard let model = bucket.str("modelId"), let remaining = bucket.num("remainingFraction"),
                  remaining >= 0, remaining <= 1 else { continue }
            windows.append(UsageWindow(id: model, label: modelLabel(model), usedFraction: 1 - remaining, resetsAt: bucket.date("resetTime")))
        }
        return windows
    }

    /// retrieveUserQuotaSummary: groups of buckets with used/limit counts. Never validated against a licensed
    /// response, so paranoid about bounds.
    public static func parseQuotaSummary(_ body: Data) -> [UsageWindow] {
        guard let root = JSON.object(body) else { return [] }
        let buckets = root.objects("quotaGroups").flatMap { $0.objects("buckets") } + root.objects("buckets")
        var windows: [UsageWindow] = []
        for bucket in buckets {
            guard let limit = bucket.num("limit"), let used = bucket.num("used"), limit > 0, used >= 0, used <= limit * 1.5 else { continue }
            let name = bucket.str("name")
            let label = bucket.str("displayName") ?? name ?? "Usage"
            windows.append(UsageWindow(id: name ?? label, label: label, usedFraction: used / limit, resetsAt: bucket.date("resetTime")))
        }
        return windows
    }

    /// gemini-2.5-pro → Gemini 2.5 Pro; anything else is title-cased word by word.
    public static func modelLabel(_ modelId: String) -> String {
        modelId.split(whereSeparator: { $0 == "-" || $0 == "_" }).map { word -> String in
            guard let first = word.first, first.isLetter else { return String(word) }
            return first.uppercased() + word.dropFirst()
        }.joined(separator: " ")
    }
}
