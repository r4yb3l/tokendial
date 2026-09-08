using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.Model;
using Tokendial.Core.Store;

namespace Tokendial.Core.Providers.Grok;

/// <summary>The Grok CLI session. Only entries issued by auth.x.ai are trusted; a customer IdP's token is meant for a private proxy.</summary>
public sealed record GrokCredential(string Key, DateTimeOffset ExpiresAt, string? Email)
{
    private const string Issuer = "https://auth.x.ai";
    public static string DefaultFile => Http.Under(".grok", "auth.json");

    public bool Expired(DateTimeOffset now) => ExpiresAt <= now;

    public static GrokCredential Read(string? file, DateTimeOffset now)
    {
        file ??= DefaultFile;
        if (!File.Exists(file)) throw UsageError.NeedsSignIn();
        using var document = Json.Parse(File.ReadAllText(file)) ?? throw UsageError.NeedsSignIn();
        return Pick(document.RootElement, now) ?? throw UsageError.NeedsSignIn();
    }

    public static GrokCredential? Pick(JsonElement root, DateTimeOffset now)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        var trusted = root.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.Object && (p.Name.StartsWith(Issuer, StringComparison.Ordinal) || p.Value.Str("oidc_issuer") == Issuer))
            .Select(p => p.Value).ToList();
        if (trusted.Count == 0) return null;
        var entry = trusted.FirstOrDefault(e => e.Date("expires_at") is not DateTimeOffset at || at > now);
        if (entry.ValueKind != JsonValueKind.Object) entry = trusted[0];
        var key = entry.Str("key");
        return key is null ? null : new GrokCredential(key, entry.Date("expires_at") ?? now.AddDays(30), entry.Str("email"));
    }
}

/// <summary>GET /v1/billing?format=credits: one credits window, labelled after the product.</summary>
public static class GrokUsage
{
    public static Parsed Parse(string body)
    {
        using var document = Json.Parse(body) ?? throw UsageError.BadResponse(0);
        var config = document.RootElement.Obj("config") ?? throw UsageError.BadResponse(0);
        var resetsAt = config.Obj("currentPeriod")?.Date("end") ?? config.Date("billingPeriodEnd");
        var products = config.Arr("productUsage").Where(p => p.ValueKind == JsonValueKind.Object).ToList();
        var windows = new List<UsageWindow>();
        if (config.Num("creditUsagePercent") is double pct)
        {
            windows.Add(new UsageWindow("credits", Humanize(products.FirstOrDefault().Str("product")) ?? "Grok Build", pct / 100, ResetsAt: resetsAt));
        }
        else
        {
            foreach (var product in products)
            {
                if (product.Num("usagePercent") is not double used) continue;
                var name = Humanize(product.Str("product"));
                windows.Add(new UsageWindow(windows.Count == 0 ? "credits" : product.Str("product") ?? name ?? "usage", name ?? "Usage", used / 100, ResetsAt: resetsAt));
            }
        }
        if (windows.Count == 0) throw UsageError.NothingMetered("Grok has nothing metered on this account yet");
        return new Parsed(windows, "credits");
    }

    /// <summary>GrokBuild → Grok Build.</summary>
    public static string? Humanize(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
    }
}

public sealed class GrokProvider : IUsageProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://cli-chat-proxy.grok.com/v1/billing?format=credits");
    private readonly HttpClient http;
    private readonly string? file;
    private readonly Func<DateTimeOffset> now;

    public GrokProvider(HttpMessageHandler? handler = null, string? file = null, Func<DateTimeOffset>? now = null)
    {
        http = Http.Client(handler);
        this.file = file;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public string Id => "grok";
    public string DisplayName => "Grok";
    public SignInRoute SignIn => new SignInRoute.Guidance("Run `grok login`; it signs in and refreshes the token this reads.");

    public ProviderAccount? Account()
    {
        try { return new ProviderAccount(GrokCredential.Read(file, now()).Email, null, "Grok", new Uri("https://grok.com/?_s=usage")); }
        catch (Exception) { return null; }
    }

    public async Task<ProviderReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        var credential = GrokCredential.Read(file, now());
        if (credential.Expired(now())) throw UsageError.CredentialExpired();
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Key);
        request.Headers.TryAddWithoutValidation("X-XAI-Token-Auth", "xai-grok-cli");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        Log.Usage.Debug($"grok: billing {(int)response.StatusCode}");
        if ((int)response.StatusCode == 429) throw UsageError.RateLimited(Backoff.Floor);
        Http.ThrowUnlessOk(response);
        var parsed = GrokUsage.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return new ProviderReading(Id, DisplayName, Fidelity.Official, ReadingStatus.LiveNow, parsed.Windows, parsed.Headline);
    }

    public void Dispose() => http.Dispose();
}
