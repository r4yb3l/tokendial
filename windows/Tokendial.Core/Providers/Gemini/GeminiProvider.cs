using Tokendial.Core.Diagnostics;
using Tokendial.Core.Model;
using Tokendial.Core.Providers.Google;
using Tokendial.Core.Store;

namespace Tokendial.Core.Providers.Gemini;

/// <summary>The OAuth token Gemini CLI caches after "Login with Google". Read only; the CLI refreshes it when it runs.</summary>
public sealed record GeminiCredential(string AccessToken, DateTimeOffset ExpiresAt, string? Email)
{
    public static string Directory => Path.Combine(Environment.GetEnvironmentVariable("GEMINI_CLI_HOME") is { Length: > 0 } home ? home : Http.Home, ".gemini");
    public static string DefaultFile => Path.Combine(Directory, "oauth_creds.json");
    public static string DefaultSettings => Path.Combine(Directory, "settings.json");
    public static string DefaultAccounts => Path.Combine(Directory, "google_accounts.json");

    public bool Expired(DateTimeOffset now) => ExpiresAt <= now;

    /// <summary>Throws NothingMetered when the CLI signs in with an API key or Vertex, which publish no quota.</summary>
    public static GeminiCredential Read(string? file = null, string? settingsFile = null, string? accountsFile = null)
    {
        file ??= DefaultFile;
        settingsFile ??= DefaultSettings;
        accountsFile ??= DefaultAccounts;
        var authType = AuthType(settingsFile);
        if (authType is not null && authType != "oauth-personal") throw UsageError.NothingMetered("Gemini CLI is signed in with " + authType + "; no quota is published for it");
        if (!File.Exists(file)) throw UsageError.NeedsSignIn();
        using var document = Json.Parse(File.ReadAllText(file)) ?? throw UsageError.NeedsSignIn();
        var root = document.RootElement;
        var token = root.Str("access_token") ?? throw UsageError.NeedsSignIn();
        var expires = root.EpochMillis("expiry_date") ?? DateTimeOffset.MinValue;
        return new GeminiCredential(token, expires, ActiveAccount(accountsFile));
    }

    private static string? AuthType(string settingsFile)
    {
        if (!File.Exists(settingsFile)) return null;
        using var document = Json.Parse(File.ReadAllText(settingsFile));
        return document?.RootElement.Obj("security")?.Obj("auth")?.Str("selectedType") ?? document?.RootElement.Str("selectedAuthType");
    }

    private static string? ActiveAccount(string accountsFile)
    {
        if (!File.Exists(accountsFile)) return null;
        using var document = Json.Parse(File.ReadAllText(accountsFile));
        return document?.RootElement.Str("active");
    }
}

/// <summary>The two Code Assist answers Gemini CLI itself reads: the gate for the project and tier, the quota for the per-model buckets.</summary>
public static class GeminiUsage
{
    public const string GateBody = "{\"metadata\":{\"ideType\":\"IDE_UNSPECIFIED\",\"platform\":\"PLATFORM_UNSPECIFIED\",\"pluginType\":\"GEMINI\"}}";

    /// <summary>The gate as a parse: an ineligible account is NothingMetered, an eligible one yields its tier as the plan.</summary>
    public static Parsed ParseGate(string body)
    {
        var gate = CodeAssist.ParseGate(body);
        if (!gate.Eligible) throw UsageError.NothingMetered(gate.Reason ?? "Gemini CLI no longer serves this account; Google points it to Antigravity");
        return new Parsed([], null, gate.Tier);
    }

    /// <summary>One window per model; the dial shows the tightest.</summary>
    public static Parsed Parse(string body)
    {
        var windows = CodeAssist.ParseQuotaBuckets(body);
        if (windows.Count == 0) throw UsageError.NothingMetered("Gemini publishes no quota for this account");
        var headline = windows.OrderByDescending(w => w.UsedFraction ?? 0).First().Id;
        return new Parsed(windows, headline);
    }
}

public sealed class GeminiProvider : IUsageProvider, IDisposable
{
    private readonly HttpClient http;
    private readonly Func<GeminiCredential> read;
    private readonly Func<DateTimeOffset> now;
    private readonly ReadingArchive? archive;
    private string? plan;

    public GeminiProvider(HttpMessageHandler? handler = null, Func<GeminiCredential>? read = null, Func<DateTimeOffset>? now = null, ReadingArchive? archive = null)
    {
        http = Http.Client(handler);
        this.read = read ?? (() => GeminiCredential.Read());
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.archive = archive;
    }

    public string Id => "gemini";
    public string DisplayName => "Gemini CLI";
    public SignInRoute SignIn => new SignInRoute.Guidance("signin.gemini");

    public ProviderAccount? Account()
    {
        try
        {
            var credential = read();
            return new ProviderAccount(credential.Email, plan, "Gemini CLI", new Uri("https://codeassist.google/"));
        }
        catch (Exception) { return null; }
    }

    public async Task<ProviderReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (archive?.BackoffUntil(Id) is DateTimeOffset until && until > now()) throw UsageError.RateLimited(until - now());
        var credential = read();
        if (credential.Expired(now())) throw UsageError.CredentialExpired();

        var (gateStatus, gateBody, gateResponse) = await CodeAssist.PostAsync(http, CodeAssist.Gate, credential.AccessToken, GeminiUsage.GateBody, cancellationToken).ConfigureAwait(false);
        using (gateResponse)
        {
            Log.Usage.Debug($"gemini: gate {gateStatus}");
            Throw(gateStatus, gateResponse);
        }
        var gate = GeminiUsage.ParseGate(gateBody);
        plan = gate.Plan;
        var project = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT") ?? Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT_ID") ?? CodeAssist.ParseGate(gateBody).Project
            ?? throw UsageError.NothingMetered("Gemini CLI has no Code Assist project for this account");

        var (status, body, response) = await CodeAssist.PostAsync(http, CodeAssist.Quota, credential.AccessToken, $"{{\"project\":{System.Text.Json.JsonSerializer.Serialize(project)}}}", cancellationToken).ConfigureAwait(false);
        using (response)
        {
            Log.Usage.Debug($"gemini: quota {status}");
            Throw(status, response);
        }
        var parsed = GeminiUsage.Parse(body);
        return new ProviderReading(Id, DisplayName, Fidelity.Official, ReadingStatus.LiveNow, parsed.Windows, parsed.Headline);
    }

    private void Throw(int status, HttpResponseMessage response)
    {
        if (status is 401 or 403) throw UsageError.NeedsSignIn();
        if (status == 429)
        {
            var wait = RetryAfterHeader.From(response, now()) ?? Backoff.Floor;
            archive?.SetBackoff(Id, now() + wait);
            throw UsageError.RateLimited(wait);
        }
        if (status != 200) throw UsageError.BadResponse(status);
    }

    public void Dispose() => http.Dispose();
}
