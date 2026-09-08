using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.Model;
using Tokendial.Core.Store;

namespace Tokendial.Core.Providers.Copilot;

/// <summary>The GitHub OAuth token the Copilot plugin keeps, or failing that the GitHub CLI's.</summary>
public sealed record CopilotCredential(string Token, string? User, string Source)
{
    public sealed record Files(string Apps, string Hosts, string GhHosts)
    {
        public static Files Default
        {
            get
            {
                var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "github-copilot");
                return new Files(Path.Combine(local, "apps.json"), Path.Combine(local, "hosts.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitHub CLI", "hosts.yml"));
            }
        }
    }

    public static CopilotCredential Read(Files? files = null)
    {
        files ??= Files.Default;
        return FromPluginFile(files.Apps) ?? FromPluginFile(files.Hosts) ?? FromGhHosts(files.GhHosts) ?? throw UsageError.NeedsSignIn();
    }

    /// <summary>apps.json keys entries "github.com:&lt;client id&gt;"; hosts.json keys them by host. Either way: oauth_token and user.</summary>
    public static CopilotCredential? FromPluginFile(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var document = Json.Parse(File.ReadAllText(path));
            if (document is null || document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var entry in document.RootElement.EnumerateObject().OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                if (!entry.Name.StartsWith("github.com", StringComparison.Ordinal) || entry.Value.ValueKind != JsonValueKind.Object) continue;
                var token = entry.Value.Str("oauth_token");
                if (token is not null) return new CopilotCredential(token, entry.Value.Str("user"), "GitHub Copilot");
            }
        }
        catch (IOException) { }
        return null;
    }

    private static readonly Regex GhToken = new(@"^\s*oauth_token:\s*(\S+)", RegexOptions.Multiline);
    private static readonly Regex GhUser = new(@"^\s*user:\s*(\S+)", RegexOptions.Multiline);

    public static CopilotCredential? FromGhHosts(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var text = File.ReadAllText(path);
            var token = GhToken.Match(text);
            return token.Success ? new CopilotCredential(token.Groups[1].Value, GhUser.Match(text) is { Success: true } u ? u.Groups[1].Value : null, "GitHub CLI") : null;
        }
        catch (IOException) { return null; }
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
        if (status == 403) throw UsageError.NothingMetered("No Copilot seat on this GitHub account");
        if (status == 429)
        {
            var delay = Backoff.Exponential(consecutive429++, RetryAfterHeader.From(response, now()));
            retryAt = now() + delay;
            archive.SetBackoff(Id, retryAt);
            throw UsageError.RateLimited(delay);
        }
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
