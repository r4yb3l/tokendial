using System.Net.Http.Headers;
using System.Text.Json;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.Model;
using Tokendial.Core.Store;

namespace Tokendial.Core.Providers.OpenCode;

/// <summary>Only the opencode-go entry; every other entry is another vendor's key.</summary>
public static class OpenCodeCredential
{
    private static readonly string[] Keys = ["key", "apiKey", "api_key", "token", "accessToken"];
    public static string DefaultFile => Http.Under(".local", "share", "opencode", "auth.json");

    public static string? Read(string? file = null)
    {
        try
        {
            file ??= DefaultFile;
            if (!File.Exists(file)) return null;
            using var document = Json.Parse(File.ReadAllText(file));
            return document is null ? null : Pick(document.RootElement);
        }
        catch (Exception) { return null; }
    }

    public static string? Pick(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("opencode-go", out var entry)) return null;
        if (entry.ValueKind == JsonValueKind.String) return string.IsNullOrEmpty(entry.GetString()) ? null : entry.GetString();
        return Keys.Select(k => entry.Str(k)).FirstOrDefault(v => v is not null);
    }
}

/// <summary>GET /zen/go/v1/usage: a fixed table of three windows in headline order.</summary>
public static class OpenCodeUsage
{
    private static readonly (string Key, string Label)[] Table = [("rolling", "5h limit"), ("weekly", "Weekly limit"), ("monthly", "Monthly limit")];

    public static Parsed Parse(string body)
    {
        using var document = Json.Parse(body) ?? throw UsageError.BadResponse(0);
        var usage = document.RootElement.Obj("usage") ?? throw UsageError.BadResponse(0);
        var windows = new List<UsageWindow>();
        foreach (var (key, label) in Table)
        {
            if (usage.Obj(key) is not JsonElement entry || entry.Num("percent") is not double pct) continue;
            windows.Add(new UsageWindow(key, label, pct / 100, ResetsAt: entry.Date("resetsAt")));
        }
        if (windows.Count == 0) throw UsageError.BadResponse(0);
        return new Parsed(windows, "rolling");
    }
}

public sealed class OpenCodeProvider : IUsageProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://opencode.ai/zen/go/v1/usage");
    private readonly HttpClient http;
    private readonly ReadingArchive archive;
    private readonly string? file;
    private readonly Func<DateTimeOffset> now;
    private DateTimeOffset? retryAt;
    private int consecutive429;

    public OpenCodeProvider(HttpMessageHandler? handler = null, ReadingArchive? archive = null, string? file = null, Func<DateTimeOffset>? now = null)
    {
        http = Http.Client(handler);
        this.archive = archive ?? new ReadingArchive();
        this.file = file;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        retryAt = this.archive.BackoffUntil(Id);
    }

    public string Id => "opencode";
    public string DisplayName => "OpenCode";
    public SignInRoute SignIn => new SignInRoute.Guidance("signin.opencode");
    public ProviderAccount? Account() => OpenCodeCredential.Read(file) is null ? null : new ProviderAccount(null, "Go", "OpenCode", new Uri("https://opencode.ai"));

    public async Task<ProviderReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (retryAt is DateTimeOffset at && at > now()) throw UsageError.RateLimited(at - now());
        var token = OpenCodeCredential.Read(file) ?? throw UsageError.NeedsSignIn();
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        Log.Usage.Debug($"opencode: usage {status}");
        if (status == 403) throw UsageError.NothingMetered("No OpenCode Go subscription on this key");
        if (status == 429)
        {
            var delay = Backoff.Exponential(consecutive429++, RetryAfterHeader.From(response, now()));
            retryAt = now() + delay;
            archive.SetBackoff(Id, retryAt);
            throw UsageError.RateLimited(delay);
        }
        Http.ThrowUnlessOk(response);
        var parsed = OpenCodeUsage.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        consecutive429 = 0;
        retryAt = null;
        archive.SetBackoff(Id, null);
        return new ProviderReading(Id, DisplayName, Fidelity.Official, ReadingStatus.LiveNow, parsed.Windows, parsed.Headline);
    }

    public void Dispose() => http.Dispose();
}
