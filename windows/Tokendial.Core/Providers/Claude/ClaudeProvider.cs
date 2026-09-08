using System.Net.Http.Headers;
using System.Text.Json;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.Model;
using Tokendial.Core.Store;

namespace Tokendial.Core.Providers.Claude;

/// <summary>One Claude Code configuration directory: ~/.claude, or ~/.claude-&lt;slug&gt; for a second account.</summary>
public sealed record ClaudeProfile(string? Slug, string Directory)
{
    private static readonly string[] Markers = ["sessions", "projects", "settings.json", "history.jsonl", ".claude.json"];

    public string Id => Slug is null ? "claude" : $"claude-{Slug}";
    public string DisplayName => Slug is null ? "Claude Code" : $"Claude Code ({Slug})";
    public string CredentialsFile => Path.Combine(Directory, ".credentials.json");
    public string SessionsDirectory => Path.Combine(Directory, "sessions");
    public string SignInCommand => Slug is null ? "claude" : $"$env:CLAUDE_CONFIG_DIR='~/.claude-{Slug}'; claude";

    public static ClaudeProfile Default(string? home = null) => new(null, Path.Combine(home ?? Http.Home, ".claude"));

    /// <summary>The default first, then every ~/.claude-&lt;slug&gt; Claude Code has actually used, slugs in ordinal order.</summary>
    public static IReadOnlyList<ClaudeProfile> Discover(string? home = null)
    {
        home ??= Http.Home;
        var extras = new List<ClaudeProfile>();
        if (System.IO.Directory.Exists(home))
        {
            foreach (var dir in System.IO.Directory.EnumerateDirectories(home, ".claude-*"))
            {
                var slug = Path.GetFileName(dir)[".claude-".Length..];
                if (slug.Length == 0) continue;
                if (!Markers.Any(m => Path.Exists(Path.Combine(dir, m)))) continue;
                extras.Add(new ClaudeProfile(slug, dir));
            }
        }
        extras.Sort((a, b) => string.CompareOrdinal(a.Slug, b.Slug));
        return [Default(home), .. extras];
    }

    public static bool IsClaude(string providerId) => providerId == "claude" || providerId.StartsWith("claude-", StringComparison.Ordinal);
}

/// <summary>The OAuth token Claude Code stores. Read when the file changes, never written or refreshed.</summary>
public sealed record ClaudeCredential(string AccessToken, DateTimeOffset ExpiresAt, string? Plan)
{
    public bool Expired(DateTimeOffset now) => ExpiresAt <= now;

    public static ClaudeCredential Read(string file)
    {
        if (!File.Exists(file)) throw UsageError.NeedsSignIn();
        string text;
        try { text = File.ReadAllText(file); }
        catch (IOException) { throw UsageError.CredentialExpired(); }
        using var document = Json.Parse(text) ?? throw UsageError.NeedsSignIn();
        var oauth = document.RootElement.Obj("claudeAiOauth") ?? throw UsageError.NeedsSignIn();
        var token = oauth.Str("accessToken") ?? throw UsageError.NeedsSignIn();
        var expires = oauth.EpochMillis("expiresAt") ?? DateTimeOffset.MinValue;
        return new ClaudeCredential(token, expires, oauth.Str("subscriptionType"));
    }
}

/// <summary>Holds the credential until the file behind it changes or the token ages out.</summary>
public sealed class ClaudeCredentialStore
{
    private readonly string file;
    private readonly Func<DateTimeOffset> now;
    private ClaudeCredential? held;
    private DateTime? stamp;

    public ClaudeCredentialStore(string file, Func<DateTimeOffset>? now = null)
    {
        this.file = file;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public ClaudeCredential Load()
    {
        var current = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : (DateTime?)null;
        if (held is not null && current == stamp && !held.Expired(now())) return held;
        if (held is not null && current == stamp && held.Expired(now())) return held;
        held = ClaudeCredential.Read(file);
        stamp = current;
        return held;
    }

    public void Forget() => held = null;
}

/// <summary>The shape of GET /api/oauth/usage. limits[] is preferred; the named windows fill in what it drops at a rollover.</summary>
public static class ClaudeUsage
{
    public static Parsed Parse(string body)
    {
        using var document = Json.Parse(body) ?? throw UsageError.BadResponse(0);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw UsageError.BadResponse(0);

        var windows = new List<UsageWindow>();
        foreach (var limit in root.Arr("limits"))
        {
            var kind = limit.Str("kind");
            var resetsAt = limit.Date("resets_at");
            if (kind is null || resetsAt is null) continue;
            windows.Add(new UsageWindow(kind, Label(kind, limit), (limit.Num("percent") ?? 0) / 100, ResetsAt: resetsAt));
        }
        Merge(root.Obj("five_hour"), "session", "Current session");
        Merge(root.Obj("seven_day"), "weekly_all", "All models");

        return new Parsed(windows.OrderBy(Rank).ThenBy(w => w.Id, StringComparer.Ordinal).ToList(), "session");

        void Merge(JsonElement? window, string id, string label)
        {
            if (window?.Date("resets_at") is not DateTimeOffset at || windows.Any(w => w.Id == id)) return;
            windows.Add(new UsageWindow(id, label, (window.Value.Num("utilization") ?? 0) / 100, ResetsAt: at));
        }
    }

    public static string Label(string kind, JsonElement limit)
    {
        if (kind.EndsWith("_scoped", StringComparison.Ordinal) && limit.Obj("scope")?.Obj("model")?.Str("display_name") is string model) return model;
        return kind switch
        {
            "session" => "Current session",
            "weekly_all" => "All models",
            "weekly_opus" => "Opus",
            "weekly_sonnet" => "Sonnet",
            _ => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(kind.Replace("weekly_", "").Replace('_', ' ').ToLowerInvariant())
        };
    }

    private static int Rank(UsageWindow w) => w.Id switch { "session" => 0, "weekly_all" => 1, _ => 2 };
}

/// <summary>Claude Code's own usage endpoint, one provider per profile. Exponential back-off on 429, persisted.</summary>
public sealed class ClaudeProvider : IUsageProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://api.anthropic.com/api/oauth/usage");
    private readonly HttpClient http;
    private readonly ReadingArchive archive;
    private readonly ClaudeCredentialStore store;
    private readonly Func<DateTimeOffset> now;
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset? retryAt;
    private int consecutive429;

    public ClaudeProvider(ClaudeProfile? profile = null, HttpMessageHandler? handler = null, ReadingArchive? archive = null, string? credentialsFile = null, Func<DateTimeOffset>? now = null)
    {
        Profile = profile ?? ClaudeProfile.Default();
        http = Http.Client(handler);
        this.archive = archive ?? new ReadingArchive();
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        store = new ClaudeCredentialStore(credentialsFile ?? Profile.CredentialsFile, this.now);
        retryAt = this.archive.BackoffUntil(Id);
    }

    public ClaudeProfile Profile { get; }
    public string Id => Profile.Id;
    public string DisplayName => Profile.DisplayName;
    public SignInRoute SignIn => new SignInRoute.Guidance($"Run `{Profile.SignInCommand}` once; it signs in and refreshes the token this reads.");

    public ProviderAccount? Account()
    {
        try { return new ProviderAccount(null, store.Load().Plan, Profile.Slug is null ? "Claude Code" : $"Claude Code in ~/.claude-{Profile.Slug}", new Uri("https://claude.ai/settings/usage")); }
        catch (Exception) { return null; }
    }

    public void ForgetCredential() => store.Forget();

    public async Task<ProviderReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (retryAt is DateTimeOffset at && at > now()) throw UsageError.RateLimited(at - now());
            try
            {
                var parsed = await Fetch(retry: true, cancellationToken).ConfigureAwait(false);
                retryAt = null;
                consecutive429 = 0;
                archive.SetBackoff(Id, null);
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
        finally { gate.Release(); }
    }

    private async Task<Parsed> Fetch(bool retry, CancellationToken cancellationToken)
    {
        var credential = store.Load();
        if (credential.Expired(now())) throw UsageError.CredentialExpired();

        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        Log.Usage.Debug($"{Id}: usage {status}");

        if (status is 401 or 403)
        {
            store.Forget();
            if (retry) return await Fetch(retry: false, cancellationToken).ConfigureAwait(false);
            throw UsageError.NeedsSignIn();
        }
        if (status == 429) throw UsageError.RateLimited(Backoff.Exponential(consecutive429, RetryAfterHeader.From(response, now())));
        Http.ThrowUnlessOk(response);
        return ClaudeUsage.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }

    public void Dispose()
    {
        http.Dispose();
        gate.Dispose();
    }
}
