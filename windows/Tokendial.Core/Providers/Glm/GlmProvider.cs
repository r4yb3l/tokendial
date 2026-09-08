using System.Net.Http.Headers;
using System.Text.Json;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.Model;
using Tokendial.Core.Store;

namespace Tokendial.Core.Providers.Glm;

/// <summary>A Z.ai Coding Plan key borrowed from whichever tool holds one, in a fixed order. Only the console host survives.</summary>
public sealed record GlmCredential(string Token, Uri Console, string Source)
{
    public sealed record Paths(string ClaudeSettings, string ZcodeConfig, string ZcodeCredentials, string OpenCodeAuth)
    {
        public static Paths Default => new(Http.Under(".claude", "settings.json"), Http.Under(".zcode", "v2", "config.json"),
            Http.Under(".zcode", "v2", "credentials.json"), Http.Under(".local", "share", "opencode", "auth.json"));
    }

    private static readonly string[] OpenCodeIds = ["zai-coding-plan", "zai", "z-ai", "z.ai", "zhipu", "zhipuai"];
    private static readonly string[] OpenCodeKeys = ["apiKey", "api_key", "token", "key", "accessToken", "auth_token"];

    public bool China => Console.Host.EndsWith("bigmodel.cn", StringComparison.Ordinal);

    public static GlmCredential? Find(Paths? paths = null)
    {
        paths ??= Paths.Default;
        return FromClaude(Root(paths.ClaudeSettings)) ?? FromZcodePlan(Root(paths.ZcodeConfig)) ?? FromZcodeOAuth(Root(paths.ZcodeCredentials)) ?? FromOpenCode(Root(paths.OpenCodeAuth));
    }

    /// <summary>Claude Code's key counts only when its base URL points at Z.ai; otherwise it is an Anthropic key.</summary>
    public static GlmCredential? FromClaude(JsonElement? root)
    {
        var env = root?.Obj("env");
        var token = env?.Str("ANTHROPIC_AUTH_TOKEN") ?? env?.Str("ANTHROPIC_API_KEY");
        if (token is null || !Uri.TryCreate(env?.Str("ANTHROPIC_BASE_URL"), UriKind.Absolute, out var uri) || !IsZai(uri.Host)) return null;
        return new GlmCredential(token, ConsoleFor(uri.Host), "Claude Code");
    }

    public static GlmCredential? FromZcodePlan(JsonElement? root)
    {
        if (root?.Obj("provider") is not JsonElement providers) return null;
        foreach (var p in providers.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (!p.Name.Contains("coding-plan", StringComparison.Ordinal) || p.Value.Bool("enabled") == false) continue;
            var key = p.Value.Obj("options")?.Str("apiKey");
            if (key is null) continue;
            var console = Uri.TryCreate(p.Value.Obj("options")?.Str("baseURL"), UriKind.Absolute, out var uri) ? ConsoleFor(uri.Host) : new Uri("https://api.z.ai");
            return new GlmCredential(key, console, "ZCode");
        }
        return null;
    }

    /// <summary>An encrypted token cannot be read here and would only yield a 401 that reads as signed-out.</summary>
    public static GlmCredential? FromZcodeOAuth(JsonElement? root)
    {
        var token = root?.Str("oauth:zai:access_token");
        return token is null || token.StartsWith("enc:v1:", StringComparison.Ordinal) ? null : new GlmCredential(token, new Uri("https://api.z.ai"), "ZCode");
    }

    public static GlmCredential? FromOpenCode(JsonElement? root)
    {
        if (root is not JsonElement r) return null;
        foreach (var id in OpenCodeIds)
        {
            if (!r.TryGetProperty(id, out var entry)) continue;
            var key = entry.ValueKind == JsonValueKind.String ? entry.GetString() : OpenCodeKeys.Select(k => entry.Str(k)).FirstOrDefault(v => v is not null);
            if (string.IsNullOrEmpty(key)) continue;
            return new GlmCredential(key, id.StartsWith("zhipu", StringComparison.Ordinal) ? new Uri("https://open.bigmodel.cn") : new Uri("https://api.z.ai"), "OpenCode");
        }
        return null;
    }

    public static bool IsZai(string host) => host == "api.z.ai" || host.EndsWith(".z.ai", StringComparison.Ordinal) || host == "open.bigmodel.cn" || host.EndsWith(".bigmodel.cn", StringComparison.Ordinal);
    public static Uri ConsoleFor(string host) => host.EndsWith("bigmodel.cn", StringComparison.Ordinal) ? new Uri("https://open.bigmodel.cn") : new Uri("https://api.z.ai");

    public static JsonElement? Root(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var document = Json.Parse(File.ReadAllText(path));
            return document?.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (Exception) { return null; }
    }
}

/// <summary>Errors ride under HTTP 200; window identity comes from length, never from meter type.</summary>
public static class GlmUsage
{
    public static Parsed Parse(string body)
    {
        using var document = Json.Parse(body) ?? throw UsageError.BadResponse(0);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw UsageError.BadResponse(0);
        var code = root.Num("code") is double c ? (int?)c : null;
        if (!(root.Bool("success") == true || code is null || code == 200))
        {
            throw code switch
            {
                401 or 403 => UsageError.NeedsSignIn(),
                429 => UsageError.RateLimited(TimeSpan.Zero),
                int other => UsageError.BadResponse(other),
                _ => UsageError.BadResponse(200)
            };
        }
        var data = root.Obj("data");
        var windows = new List<UsageWindow>();
        foreach (var limit in data?.Arr("limits") ?? [])
        {
            if (limit.Num("percentage") is not double pct) continue;
            var id = Id(limit);
            windows.Add(new UsageWindow(id, Label(id, limit), pct / 100, ResetsAt: limit.EpochMillis("nextResetTime")));
        }
        return new Parsed(windows.OrderBy(Rank).ThenBy(w => w.Id, StringComparer.Ordinal).ToList(), "session", data?.Str("level"));
    }

    public static string Id(JsonElement limit)
    {
        if (limit.Str("type") == "TIME_LIMIT") return "mcp";
        return (limit.Num("unit"), limit.Num("number")) switch
        {
            (3, 5) => "session",
            (6, 1) => "weekly",
            (double u, double n) => $"window-{(int)u}x{(int)n}",
            _ => limit.Str("type")?.ToLowerInvariant() ?? "unknown"
        };
    }

    private static string Label(string id, JsonElement limit) => id switch
    {
        "session" => "Current session",
        "weekly" => "Weekly",
        "mcp" => "MCP (1 month)",
        _ when id.StartsWith("window-", StringComparison.Ordinal) => limit.Num("unit") switch
        {
            3 => $"Usage ({(int)(limit.Num("number") ?? 0)} h)",
            6 => $"Usage ({(int)(limit.Num("number") ?? 0)} wk)",
            _ => "Usage"
        },
        _ => "Usage"
    };

    private static int Rank(UsageWindow w) => w.Id switch { "session" => 0, "weekly" => 1, "mcp" => 2, _ => 3 };
}

public sealed class GlmProvider : IUsageProvider, IDisposable
{
    private readonly HttpClient http;
    private readonly ReadingArchive archive;
    private readonly Func<GlmCredential?> find;
    private readonly Func<DateTimeOffset> now;
    private DateTimeOffset? retryAt;
    private int consecutive429;
    private string? plan;

    public GlmProvider(HttpMessageHandler? handler = null, ReadingArchive? archive = null, Func<GlmCredential?>? find = null, Func<DateTimeOffset>? now = null)
    {
        http = Http.Client(handler);
        this.archive = archive ?? new ReadingArchive();
        this.find = find ?? (() => GlmCredential.Find());
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        retryAt = this.archive.BackoffUntil(Id);
    }

    public string Id => "glm";
    public string DisplayName => "GLM";
    public SignInRoute SignIn => new SignInRoute.Guidance("signin.glm");

    public ProviderAccount? Account()
    {
        var credential = find();
        return credential is null ? null : new ProviderAccount(null, plan, credential.Source, new Uri(credential.China ? "https://open.bigmodel.cn/usage" : "https://z.ai/manage-apikey/apikey-list"));
    }

    public async Task<ProviderReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (retryAt is DateTimeOffset at && at > now()) throw UsageError.RateLimited(at - now());
        var credential = find() ?? throw UsageError.NeedsSignIn();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(credential.Console, "/api/monitor/usage/quota/limit"));
            request.Headers.TryAddWithoutValidation("Authorization", credential.Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            Log.Usage.Debug($"glm: quota {(int)response.StatusCode}");
            if ((int)response.StatusCode == 429) throw UsageError.RateLimited(Backoff.Exponential(consecutive429, RetryAfterHeader.From(response, now())));
            Http.ThrowUnlessOk(response);
            Parsed parsed;
            try { parsed = GlmUsage.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)); }
            catch (UsageError e) when (e.Kind == UsageErrorKind.RateLimited) { throw UsageError.RateLimited(Backoff.Exponential(consecutive429, null)); }
            consecutive429 = 0;
            retryAt = null;
            archive.SetBackoff(Id, null);
            plan = parsed.Plan;
            return new ProviderReading(Id, DisplayName, Fidelity.Official, ReadingStatus.LiveNow, parsed.Windows, parsed.Headline);
        }
        catch (UsageError error) when (error.Kind == UsageErrorKind.RateLimited)
        {
            consecutive429++;
            retryAt = now() + error.RetryAfter;
            archive.SetBackoff(Id, retryAt);
            throw;
        }
    }

    public void Dispose() => http.Dispose();
}
