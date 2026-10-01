using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.Model;
using Tokendial.Core.Store;

namespace Tokendial.Core.Providers.Copilot;

/// <summary>The GitHub OAuth token the Copilot plugin keeps, or failing that the GitHub CLI's.</summary>
public sealed record CopilotCredential(string Token, string? User, string Source)
{
    public sealed record Files(string Apps, string Hosts, string GhHosts)
    {
        public static Files Default => For(Roots.Current, Roots.Here, Environment.GetEnvironmentVariable);

        /// <summary>
        /// The only provider that genuinely branches three ways. Windows keeps the plugin's tokens in
        /// %LOCALAPPDATA% and the gh CLI's under %APPDATA%\GitHub CLI; everywhere else Copilot follows the
        /// XDG convention directly - ~/.config/github-copilot and ~/.config/gh - which is not the same thing
        /// as the platform's config root, since on macOS that is Application Support. Both tools put
        /// $XDG_CONFIG_HOME ahead of all of that on every platform, and gh puts $GH_CONFIG_DIR ahead of it:
        /// the order gh documents.
        /// </summary>
        public static Files For(Desktop desktop, Roots roots, Func<string, string?> env)
        {
            var plugin = roots.In(desktop == Desktop.Windows ? roots.XdgConfigHome ?? roots.Data : roots.XdgConfig, "github-copilot");
            var gh = env("GH_CONFIG_DIR") is { Length: > 0 } configured
                ? configured.TrimEnd('/', '\\')
                : desktop == Desktop.Windows && roots.XdgConfigHome is null
                    ? roots.In(roots.Config, "GitHub CLI")
                    : roots.In(roots.XdgConfig, "gh");
            return new Files(roots.In(plugin, "apps.json"), roots.In(plugin, "hosts.json"), roots.In(gh, "hosts.yml"));
        }
    }

    public static CopilotCredential Read(Files? files = null)
    {
        files ??= Files.Default;
        return FromPluginFile(files.Apps) ?? FromPluginFile(files.Hosts) ?? FromGhHosts(files.GhHosts) ?? throw UsageError.NeedsSignIn();
    }

    /// <summary>apps.json keys entries "github.com:&lt;client id&gt;"; hosts.json keys them by host. Either way: oauth_token and user, github.com's only.</summary>
    public static CopilotCredential? FromPluginFile(string path)
    {
        if (CredentialFile.Text(path) is not string text) return null;
        using var document = Json.Parse(text);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object) return null;
        foreach (var entry in document.RootElement.EnumerateObject().OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            if (!IsGitHubDotCom(entry.Name) || entry.Value.ValueKind != JsonValueKind.Object) continue;
            var token = entry.Value.Str("oauth_token");
            if (token is not null) return new CopilotCredential(token, entry.Value.Str("user"), "GitHub Copilot");
        }
        return null;
    }

    /// <summary>A bare "github.com" prefix also matched github.company.com, an Enterprise host whose token must never reach api.github.com.</summary>
    private static bool IsGitHubDotCom(string key) => key == "github.com" || key.StartsWith("github.com:", StringComparison.Ordinal);

    /// <summary>
    /// gh writes one block per host, and since 2.40 a users: map inside it holding every signed-in account's
    /// token; the active account's is the host's own oauth_token. Only that one is read, and only github.com's:
    /// the first oauth_token anywhere in the file used to win, which sent an Enterprise host's token, or
    /// another account's, to api.github.com.
    /// </summary>
    public static CopilotCredential? FromGhHosts(string path)
    {
        if (CredentialFile.Text(path) is not string text) return null;
        var host = GhHost(text, "github.com");
        return host.TryGetValue("oauth_token", out var token) ? new CopilotCredential(token, host.GetValueOrDefault("user"), "GitHub CLI") : null;
    }

    /// <summary>The scalar keys directly under one top-level host of gh's hosts.yml; anything nested deeper is ignored.</summary>
    public static IReadOnlyDictionary<string, string> GhHost(string yaml, string host)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var inside = false;
        var depth = -1;
        foreach (var raw in yaml.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var content = line.TrimStart();
            if (content.Length == 0 || content[0] == '#') continue;
            var indent = line.Length - content.Length;
            var (key, value) = YamlPair(content);
            if (indent == 0)
            {
                if (inside) break;
                inside = key == host;
                continue;
            }
            if (!inside) continue;
            if (depth < 0) depth = indent;
            if (indent == depth && value.Length > 0) values.TryAdd(key, value);
        }
        return values;
    }

    private static (string Key, string Value) YamlPair(string content)
    {
        var colon = content.IndexOf(':');
        if (colon < 0) return (content, "");
        var value = content[(colon + 1)..].Trim().Split(' ', '\t')[0];
        return (content[..colon].Trim().Trim('"', '\''), value.Trim('"', '\''));
    }
}

/// <summary>GET /copilot_internal/user: three quota snapshots, each reporting what remains. Unlimited snapshots are not windows.</summary>
public static class CopilotUsage
{
    private static readonly (string Key, string Id, string Label)[] Table =
    [
        ("premium_interactions", "premium", "Premium requests"),
        ("chat", "chat", "Chat"),
        ("completions", "completions", "Completions")
    ];

    public static Parsed Parse(string body)
    {
        using var document = Json.Parse(body) ?? throw UsageError.BadResponse(0);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw UsageError.BadResponse(0);
        var plan = Plan(root.Str("access_type_sku") ?? root.Str("copilot_plan"));
        var resetsAt = ResetDate(root.Str("quota_reset_date"));
        var snapshots = root.Obj("quota_snapshots");
        var windows = new List<UsageWindow>();
        foreach (var (key, id, label) in Table)
        {
            if (snapshots?.Obj(key) is not JsonElement snapshot || snapshot.Bool("unlimited") == true) continue;
            if (snapshot.Num("percent_remaining") is not double remaining) continue;
            var used = Math.Clamp(1 - remaining / 100, 0, 1);
            windows.Add(new UsageWindow(id, label, used, ResetsAt: resetsAt));
        }
        if (windows.Count == 0) throw UsageError.NothingMetered($"Unlimited on the {plan ?? "current"} plan — nothing to meter");
        return new Parsed(windows, windows[0].Id, plan);
    }

    /// <summary>
    /// GitHub answers an exhausted rate limit with 403 as often as with 429, so a 403 is a missing seat only
    /// when it carries neither x-ratelimit-remaining: 0 nor Retry-After. Read as a missing seat, a rate limit
    /// erased the last reading and told someone with a seat that nothing was metered.
    /// </summary>
    public static bool RateLimited(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (status == 429) return true;
        if (status != 403) return false;
        if (response.Headers.Contains("Retry-After")) return true;
        return response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.FirstOrDefault()?.Trim() == "0";
    }

    /// <summary>quota_reset_date is a calendar date; the quota rolls at midnight UTC.</summary>
    public static DateTimeOffset? ResetDate(string? text) =>
        text is not null && DateTimeOffset.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : Json.Iso(text);

    public static string? Plan(string? raw) => raw switch
    {
        null => null,
        "free" or "free_limited_copilot" => "Free",
        "individual" or "copilot_pro" => "Pro",
        "individual_pro" or "copilot_pro_plus" => "Pro+",
        "business" or "copilot_business_seat" => "Business",
        "enterprise" or "copilot_enterprise_seat" => "Enterprise",
        _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(raw.Replace('_', ' '))
    };
}

public sealed class CopilotProvider : IUsageProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://api.github.com/copilot_internal/user");
    private readonly HttpClient http;
    private readonly ReadingArchive archive;
    private readonly CopilotCredential.Files files;
    private readonly Func<DateTimeOffset> now;
    private DateTimeOffset? retryAt;
    private int consecutive429;
    private string? plan;

    public CopilotProvider(HttpMessageHandler? handler = null, ReadingArchive? archive = null, CopilotCredential.Files? files = null, Func<DateTimeOffset>? now = null)
    {
        http = Http.Client(handler);
        this.archive = archive ?? new ReadingArchive();
        this.files = files ?? CopilotCredential.Files.Default;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        retryAt = this.archive.BackoffUntil(Id);
    }

    public string Id => "copilot";
    public string DisplayName => "GitHub Copilot";
    public SignInRoute SignIn => new SignInRoute.Guidance("signin.copilot");

    public ProviderAccount? Account()
    {
        try
        {
            var credential = CopilotCredential.Read(files);
            return new ProviderAccount(credential.User, plan, credential.Source, new Uri("https://github.com/settings/copilot/features"));
        }
        catch (UsageError) { return null; }
    }

    public async Task<ProviderReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (retryAt is DateTimeOffset at && at > now()) throw UsageError.RateLimited(at - now());
        var credential = CopilotCredential.Read(files);
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.TryAddWithoutValidation("Authorization", $"token {credential.Token}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Editor-Version", "vscode/1.104.0");
        request.Headers.TryAddWithoutValidation("Editor-Plugin-Version", "copilot-chat/0.30.0");
        request.Headers.TryAddWithoutValidation("User-Agent", "GitHubCopilotChat/0.30.0");
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        Log.Usage.Debug($"copilot: user {status}");
        if (CopilotUsage.RateLimited(response))
        {
            var delay = Backoff.Exponential(consecutive429++, RetryAfterHeader.From(response, now()));
            retryAt = now() + delay;
            archive.SetBackoff(Id, retryAt);
            throw UsageError.RateLimited(delay);
        }
        if (status == 403) throw UsageError.NothingMetered("No Copilot seat on this GitHub account");
        Http.ThrowUnlessOk(response);
        var parsed = CopilotUsage.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        consecutive429 = 0;
        retryAt = null;
        archive.SetBackoff(Id, null);
        plan = parsed.Plan;
        return new ProviderReading(Id, DisplayName, Fidelity.Official, ReadingStatus.LiveNow, parsed.Windows, parsed.Headline);
    }

    public void Dispose() => http.Dispose();
}
