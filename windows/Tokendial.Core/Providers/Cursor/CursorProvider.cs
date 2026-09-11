using System.Net.Http.Headers;
using System.Text.Json;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.Model;

namespace Tokendial.Core.Providers.Cursor;

/// <summary>The editor's own session, read from its global state database. The cookie pair is what the usage endpoint accepts.</summary>
public sealed record CursorCredential(string AccessToken, string AccountId)
{
    public static string DefaultStore => StorePath(Roots.Here);

    /// <summary>
    /// Electron keeps its user data under the platform's config root: %APPDATA% on Windows,
    /// Application Support on macOS, $XDG_CONFIG_HOME or ~/.config on Linux.
    /// </summary>
    public static string StorePath(Roots roots) => roots.In(roots.Config, "Cursor", "User", "globalStorage", "state.vscdb");

    public string Cookie => $"WorkosCursorSessionToken={AccountId}::{AccessToken}";

    public static CursorCredential Read(string? store = null)
    {
        store ??= DefaultStore;
        var token = Value(store, "cursorAuth/accessToken");
        var account = Value(store, "cursorAuth/stripeMembershipAuthId");
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(account)) throw UsageError.NeedsSignIn();
        return new CursorCredential(token, account);
    }

    public static ProviderAccount? Account(string? store = null)
    {
        store ??= DefaultStore;
        var email = Value(store, "cursorAuth/cachedEmail");
        return string.IsNullOrEmpty(email) ? null : new ProviderAccount(email, Value(store, "cursorAuth/stripeMembershipType"), "Cursor", new Uri("https://cursor.com/dashboard"));
    }

    private static string? Value(string store, string key) => Sqlite.Column(store, "SELECT value FROM ItemTable WHERE key = $p", key)?.FirstOrDefault();
}

/// <summary>GET /api/usage-summary. Zero is a reading; an empty plan is not.</summary>
public static class CursorUsage
{
    public static Parsed Parse(string body)
    {
        using var document = Json.Parse(body) ?? throw UsageError.BadResponse(0);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw UsageError.BadResponse(0);
        var resetsAt = root.Date("billingCycleEnd");
        var individual = root.Obj("individualUsage");
        var plan = individual?.Obj("plan");
        var windows = new List<UsageWindow>();
        if (plan?.Num("totalPercentUsed") is double total) windows.Add(new UsageWindow("included", "Included usage", total / 100, ResetsAt: resetsAt));
        if (plan?.Num("apiPercentUsed") is double api && api > 0) windows.Add(new UsageWindow("api", "API usage", api / 100, ResetsAt: resetsAt));
        if (individual?.Obj("onDemand") is JsonElement onDemand && onDemand.Bool("enabled") == true
            && onDemand.Num("limit") is double limit && limit > 0 && onDemand.Num("used") is double used)
        {
            windows.Add(new UsageWindow("on_demand", "On demand", used / limit, ResetsAt: resetsAt));
        }
        if (windows.Count > 0) return new Parsed(windows, "included");
        var membership = root.Str("membershipType") ?? "this";
        throw UsageError.NothingMetered(root.Bool("isUnlimited") == true
            ? $"Unlimited on the {membership} plan — nothing to meter"
            : $"The {membership} plan has nothing for Cursor to meter yet");
    }
}

public sealed class CursorProvider : IUsageProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://cursor.com/api/usage-summary");
    private readonly HttpClient http;
    private readonly string store;

    public CursorProvider(HttpMessageHandler? handler = null, string? store = null)
    {
        http = Http.Client(handler);
        this.store = store ?? CursorCredential.DefaultStore;
    }

    public string Id => "cursor";
    public string DisplayName => "Cursor";
    public SignInRoute SignIn => new SignInRoute.OpenApp("cursor", "Cursor");
    public ProviderAccount? Account() => CursorCredential.Account(store);

    public async Task<ProviderReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        var credential = CursorCredential.Read(store);
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.TryAddWithoutValidation("Cookie", credential.Cookie);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        Log.Usage.Debug($"cursor: usage {(int)response.StatusCode}");
        Http.ThrowUnlessOk(response);
        var parsed = CursorUsage.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return new ProviderReading(Id, DisplayName, Fidelity.Official, ReadingStatus.LiveNow, parsed.Windows, parsed.Headline);
    }

    public void Dispose() => http.Dispose();
}
