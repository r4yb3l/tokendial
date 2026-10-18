using System.Net.Http.Headers;
using System.Text.Json;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.I18n;
using Tokendial.Core.Model;
using Tokendial.Core.Store;

namespace Tokendial.Core.Providers.OpenCode;

/// <summary>
/// Only the opencode-go entry; every other entry is another vendor's key. opencode v2 keeps logins in its
/// SQLite store and stopped writing auth.json, which then holds whatever key v1 left behind: the database
/// answers first and auth.json is read only when the database has no active Go key.
/// </summary>
public static class OpenCodeCredential
{
    private static readonly string[] Keys = ["key", "apiKey", "api_key", "token", "accessToken"];

    /// <summary>Replacing a key adds a row and sets the old one inactive, so the newest active row is the one opencode uses.</summary>
    private const string ActiveGoKey =
        "SELECT value FROM credential WHERE integration_id = $p AND active = 1 ORDER BY time_created DESC, time_updated DESC LIMIT 1";

    public static string DefaultFile => FileFor(Roots.Here);
    public static string DefaultDatabase => DatabaseFor(Roots.Here);

    /// <summary>OpenCode keeps its data in the XDG data directory on every platform, so $XDG_DATA_HOME moves it there too.</summary>
    public static string FileFor(Roots roots) => roots.In(roots.XdgData, "opencode", "auth.json");

    public static string DatabaseFor(Roots roots) => roots.In(roots.XdgData, "opencode", "opencode.db");

    /// <summary>
    /// The key opencode is using now. Throws <see cref="Sqlite.BusyException"/> while opencode holds its store
    /// busy: auth.json may hold a key it already replaced, so falling back there would turn a write in progress
    /// into a rejected key.
    /// </summary>
    public static string? Read(string? file = null, string? database = null) =>
        FromDatabase(database ?? DefaultDatabase) ?? FromFile(file ?? DefaultFile);

    public static string? FromDatabase(string database)
    {
        try
        {
            var value = Sqlite.CurrentColumn(database, ActiveGoKey, "opencode-go")?.FirstOrDefault();
            if (value is null) return null;
            using var document = Json.Parse(value);
            return document is null ? null : Entry(document.RootElement);
        }
        catch (Sqlite.BusyException) { throw; }
        catch (Exception) { return null; }
    }

    public static string? FromFile(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            using var document = Json.Parse(File.ReadAllText(file));
            return document is null ? null : Pick(document.RootElement);
        }
        catch (Exception) { return null; }
    }

    public static string? Pick(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("opencode-go", out var entry)) return null;
        return Entry(entry);
    }

    private static string? Entry(JsonElement entry)
    {
        if (entry.ValueKind == JsonValueKind.String) return string.IsNullOrEmpty(entry.GetString()) ? null : entry.GetString();
        if (entry.ValueKind != JsonValueKind.Object) return null;
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

    /// <summary>
    /// A 401 whose body says the key expired. Signing in again does not renew it - only a new key from the
    /// console does - so it must not read as signed out. Any other 401 body is a key that was never valid.
    /// </summary>
    public static bool KeyExpired(string body)
    {
        using var document = Json.Parse(body);
        return document?.RootElement.Obj("error")?.Str("code") == "api_key_expired";
    }
}

public sealed class OpenCodeProvider : IUsageProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://opencode.ai/zen/go/v1/usage");
    private readonly HttpClient http;
    private readonly ReadingArchive archive;
    private readonly string? file;
    private readonly string? database;
    private readonly Func<DateTimeOffset> now;
    private DateTimeOffset? retryAt;
    private int consecutive429;

    public OpenCodeProvider(HttpMessageHandler? handler = null, ReadingArchive? archive = null, string? file = null, Func<DateTimeOffset>? now = null, string? database = null)
    {
        http = Http.Client(handler);
        this.archive = archive ?? new ReadingArchive();
        this.file = file;
        this.database = database;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        retryAt = this.archive.BackoffUntil(Id);
    }

    public string Id => "opencode";
    public string DisplayName => "OpenCode";
    public SignInRoute SignIn => new SignInRoute.Guidance("signin.opencode");
    public ProviderAccount? Account()
    {
        var signedIn = new ProviderAccount(null, "Go", "OpenCode", new Uri("https://opencode.ai"));
        try { return OpenCodeCredential.Read(file, database) is null ? null : signedIn; }
        catch (Sqlite.BusyException) { return signedIn; }
    }

    public async Task<ProviderReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (retryAt is DateTimeOffset at && at > now()) throw UsageError.RateLimited(at - now());
        string token;
        try { token = OpenCodeCredential.Read(file, database) ?? throw UsageError.NeedsSignIn(); }
        catch (Sqlite.BusyException) { throw UsageError.CredentialExpired(); }
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
        if (status == 401 && OpenCodeUsage.KeyExpired(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)))
            throw UsageError.Rejected(Strings.T("card.opencodeKeyExpired"));
        Http.ThrowUnlessOk(response);
        var parsed = OpenCodeUsage.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        consecutive429 = 0;
        retryAt = null;
        archive.SetBackoff(Id, null);
        return new ProviderReading(Id, DisplayName, Fidelity.Official, ReadingStatus.LiveNow, parsed.Windows, parsed.Headline);
    }

    public void Dispose() => http.Dispose();
}
