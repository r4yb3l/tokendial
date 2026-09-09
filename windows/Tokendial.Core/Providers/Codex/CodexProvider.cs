using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.Model;
using Tokendial.Core.Store;

namespace Tokendial.Core.Providers.Codex;

/// <summary>The ChatGPT sign-in the Codex CLI keeps in ~/.codex/auth.json.</summary>
public sealed record CodexCredential(string AccessToken, string AccountId, string? IdToken)
{
    public static string DefaultFile => Http.Under(".codex", "auth.json");

    public static CodexCredential Read(string? file = null, DateTimeOffset? now = null)
    {
        file ??= DefaultFile;
        if (!File.Exists(file)) throw UsageError.NeedsSignIn();
        using var document = Json.Parse(File.ReadAllText(file)) ?? throw UsageError.NeedsSignIn();
        var tokens = document.RootElement.Obj("tokens") ?? throw UsageError.NeedsSignIn();
        var credential = new CodexCredential(
            tokens.Str("access_token")?.Trim() ?? throw UsageError.NeedsSignIn(),
            tokens.Str("account_id")?.Trim() ?? throw UsageError.NeedsSignIn(),
            tokens.Str("id_token"));
        if (credential.Expired(now ?? DateTimeOffset.UtcNow)) throw UsageError.CredentialExpired();
        return credential;
    }

    /// <summary>The access token's own exp claim, as a local hint.</summary>
    public bool Expired(DateTimeOffset now) =>
        Claims(AccessToken)?.EpochSeconds("exp") is DateTimeOffset exp && exp <= now;

    public ProviderAccount Account()
    {
        var claims = IdToken is null ? null : Claims(IdToken);
        return new ProviderAccount(claims?.Str("email"), claims?.Obj("https://api.openai.com/auth")?.Str("chatgpt_plan_type"), "Codex", new Uri("https://chatgpt.com/#settings/Account"));
    }

    /// <summary>JWT claims, for labels and the expiry hint only; malformed tokens are refused rather than crashing.</summary>
    public static JsonElement? Claims(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        try
        {
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return document.RootElement.Clone();
        }
        catch (Exception) { return null; }
    }
}

/// <summary>GET /backend-api/wham/usage: primary and secondary windows, labelled by their length.</summary>
public static class CodexUsage
{
    public static Parsed Parse(string body, DateTimeOffset now)
    {
        using var document = Json.Parse(body) ?? throw UsageError.BadResponse(0);
        var rateLimit = document.RootElement.Obj("rate_limit") ?? throw UsageError.NothingMetered("Codex reported no usage windows");
        var windows = new List<UsageWindow>();
        foreach (var (id, key) in new[] { ("primary", "primary_window"), ("secondary", "secondary_window") })
        {
            if (rateLimit.Obj(key) is not JsonElement window) continue;
            var seconds = window.Num("limit_window_seconds") ?? throw UsageError.BadResponse(0);
            var used = window.Num("used_percent") ?? throw UsageError.BadResponse(0);
            var resetsAt = window.EpochSeconds("reset_at") ?? (window.Num("reset_after_seconds") is double after ? now.AddSeconds(after) : null);
            windows.Add(new UsageWindow(id, Label(seconds, id == "primary"), used / 100, ResetsAt: resetsAt));
        }
        if (windows.Count == 0) throw UsageError.NothingMetered("Codex reported no usage windows");
        return new Parsed(windows, windows[0].Id);
    }

    public static string Label(double seconds, bool primary)
    {
        if (seconds <= 0) return primary ? "Current session" : "Longer window";
        var minutes = seconds / 60;
        if (minutes < 60) return $"{(int)minutes}m limit";
        if (minutes < 1440) return $"{(int)(minutes / 60)}h limit";
        var days = (int)Math.Round(minutes / 1440);
        return days switch { 7 => "Weekly limit", 30 => "Monthly limit", _ => $"{days}d limit" };
    }
}

public sealed class CodexProvider : IUsageProvider, IProfiled, IDisposable
{
    private static readonly Uri Endpoint = new("https://chatgpt.com/backend-api/wham/usage");
    private readonly HttpClient http;
    private readonly ReadingArchive archive;
    private readonly string file;
    private readonly Func<DateTimeOffset> now;
    private DateTimeOffset? retryAt;

    public CodexProvider(HttpMessageHandler? handler = null, ReadingArchive? archive = null, string? file = null, Func<DateTimeOffset>? now = null, CodexProfile? profile = null)
    {
        Profile = profile ?? CodexProfile.Default();
        http = Http.Client(handler);
        this.archive = archive ?? new ReadingArchive();
        this.file = file ?? Profile.AuthFile;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        retryAt = this.archive.BackoffUntil(Id);
    }

    public CodexProfile Profile { get; }
    public string Id => Profile.Id;
    public string DisplayName => Profile.DisplayName;
    public string SignInCommand => Profile.SignInCommand;
    public SignInRoute SignIn => Profile.Slug is null ? new SignInRoute.OpenApp("codex", "Codex") : new SignInRoute.Guidance("signin.claude", ("command", Profile.SignInCommand));

    public ProviderAccount? Account()
    {
        try { return CodexCredential.Read(file, now()).Account(); }
        catch (UsageError e) when (e.Kind == UsageErrorKind.CredentialExpired) { return new ProviderAccount(null, null, "Codex", null); }
        catch (Exception) { return null; }
    }

    public async Task<ProviderReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (retryAt is DateTimeOffset at && at > now()) throw UsageError.RateLimited(at - now());
        var credential = CodexCredential.Read(file, now());
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", credential.AccountId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        Log.Usage.Debug($"codex: usage {(int)response.StatusCode}");
        if ((int)response.StatusCode == 429)
        {
            var delay = Backoff.Flat(RetryAfterHeader.From(response, now()));
            retryAt = now() + delay;
            archive.SetBackoff(Id, retryAt);
            throw UsageError.RateLimited(delay);
        }
        Http.ThrowUnlessOk(response);
        var parsed = CodexUsage.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), now());
        retryAt = null;
        archive.SetBackoff(Id, null);
        return new ProviderReading(Id, DisplayName, Fidelity.Official, ReadingStatus.LiveNow, parsed.Windows, parsed.Headline);
    }

    public void Dispose() => http.Dispose();
}

/// <summary>Where the CLI and the desktop app keep their thread indexes.</summary>
public static class CodexStores
{
    public static string StateFile => Http.Under(".codex", "state_5.sqlite");
    public static string DesktopFile => Http.Under(".codex", "sqlite", "codex-dev.db");

    public static string? NewestRollout(string stateFile)
    {
        var rows = Sqlite.Column(stateFile, "SELECT rollout_path FROM threads WHERE archived = 0 ORDER BY updated_at_ms DESC LIMIT 8");
        return rows?.Select(Expand).FirstOrDefault(File.Exists);
    }

    /// <summary>source_updated_at is fractional seconds, not milliseconds.</summary>
    public static (DateTimeOffset At, string Title)? NewestDesktopThread(string desktopFile)
    {
        var row = Sqlite.Rows(desktopFile, "SELECT source_updated_at, display_title FROM local_thread_catalog ORDER BY source_updated_at DESC LIMIT 1")?.FirstOrDefault();
        if (row is null || !double.TryParse(row[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds)) return null;
        return (DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000)), string.IsNullOrEmpty(row[1]) ? "Codex" : row[1]!);
    }

    private static string Expand(string path) => path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal)
        ? Path.Combine(Http.Home, path[2..]) : path;
}
