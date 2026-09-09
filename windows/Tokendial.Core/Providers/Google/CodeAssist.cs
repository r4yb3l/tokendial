using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tokendial.Core.Model;

namespace Tokendial.Core.Providers.Google;

/// <summary>What the Code Assist gate says about an account: which project bills it, which tier it is on, and whether it may use the client at all.</summary>
public sealed record CodeAssistGate(string? Project, string? Tier, bool Eligible, string? Reason);

/// <summary>
/// Google's Code Assist API on cloudcode-pa.googleapis.com, shared by Antigravity and Gemini CLI. Only transport and
/// parsing live here; each provider decides what a status means for its own cached credential.
/// </summary>
public static class CodeAssist
{
    public static readonly Uri Gate = new("https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist");
    public static readonly Uri Quota = new("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota");
    public static readonly Uri QuotaSummary = new("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuotaSummary");

    /// <summary>POST a JSON body with a bearer token; the caller maps the status.</summary>
    public static async Task<(int Status, string Body, HttpResponseMessage Response)> PostAsync(HttpClient client, Uri endpoint, string accessToken, string body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) : "";
        return ((int)response.StatusCode, text, response);
    }

    /// <summary>The gate body: the project may be a string or an object, the tier a name; eligible means some tier is current or allowed.</summary>
    public static CodeAssistGate ParseGate(string body)
    {
        using var document = Json.Parse(body);
        if (document is null) return new CodeAssistGate(null, null, false, null);
        var root = document.RootElement;
        var project = root.Str("cloudaicompanionProject") ?? (root.Obj("cloudaicompanionProject") is JsonElement p ? p.Str("id") ?? p.Str("projectId") : null);
        var current = root.Obj("currentTier");
        var paid = root.Obj("paidTier");
        var tier = paid?.Str("name") ?? paid?.Str("id") ?? current?.Str("name") ?? current?.Str("id");
        var eligible = current is not null || root.Arr("allowedTiers").Any();
        var reason = root.Arr("ineligibleTiers").Select(t => t.Str("reasonMessage") ?? t.Str("reasonCode")).FirstOrDefault(r => r is not null);
        return new CodeAssistGate(project, tier, eligible, reason);
    }

    /// <summary>retrieveUserQuota: one bucket per model with the fraction still left and when it refills.</summary>
    public static IReadOnlyList<UsageWindow> ParseQuotaBuckets(string body)
    {
        using var document = Json.Parse(body);
        if (document is null) return [];
        var windows = new List<UsageWindow>();
        foreach (var bucket in document.RootElement.Arr("buckets"))
        {
            var model = bucket.Str("modelId");
            if (model is null || bucket.Num("remainingFraction") is not double remaining || remaining < 0 || remaining > 1) continue;
            windows.Add(new UsageWindow(model, ModelLabel(model), 1 - remaining, ResetsAt: bucket.Date("resetTime")));
        }
        return windows;
    }

    /// <summary>retrieveUserQuotaSummary: groups of buckets with used/limit counts. Never validated against a licensed response, so paranoid about bounds.</summary>
    public static IReadOnlyList<UsageWindow> ParseQuotaSummary(string body)
    {
        using var document = Json.Parse(body);
        if (document is null) return [];
        var root = document.RootElement;
        var buckets = root.Arr("quotaGroups").SelectMany(g => g.Arr("buckets")).Concat(root.Arr("buckets"));
        var windows = new List<UsageWindow>();
        foreach (var bucket in buckets)
        {
            if (bucket.Num("limit") is not double limit || bucket.Num("used") is not double used || limit <= 0 || used < 0 || used > limit * 1.5) continue;
            var name = bucket.Str("name");
            var label = bucket.Str("displayName") ?? name ?? "Usage";
            windows.Add(new UsageWindow(name ?? label, label, used / limit, ResetsAt: bucket.Date("resetTime")));
        }
        return windows;
    }

    /// <summary>gemini-2.5-pro → Gemini 2.5 Pro; anything else is title-cased word by word.</summary>
    public static string ModelLabel(string modelId)
    {
        var words = modelId.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 0 && char.IsLetter(w[0]) ? char.ToUpperInvariant(w[0]) + w[1..] : w);
        return string.Join(' ', words);
    }
}
