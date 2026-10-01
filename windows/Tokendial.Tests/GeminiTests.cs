using System.Net;
using System.Net.Http.Headers;
using Tokendial.Core.Providers;
using Tokendial.Core.Providers.Gemini;
using Tokendial.Core.Providers.Google;
using Tokendial.Core.Store;

namespace Tokendial.Tests;

public class GeminiTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tokendial-tests", Guid.NewGuid().ToString("N"));

    public GeminiTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Write(string name, string json)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void ReadsTheCachedTokenAndTheActiveAccount()
    {
        var creds = Write("oauth_creds.json", """{"access_token":"ya29.abc","refresh_token":"1//r","expiry_date":1788940800000,"token_type":"Bearer"}""");
        var accounts = Write("google_accounts.json", """{"active":"dev@example.com","old":[]}""");
        var credential = GeminiCredential.Read(creds, Path.Combine(root, "missing-settings.json"), accounts);
        Assert.Equal("ya29.abc", credential.AccessToken);
        Assert.Equal("dev@example.com", credential.Email);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1788940800000), credential.ExpiresAt);
        Assert.True(credential.Expired(DateTimeOffset.FromUnixTimeMilliseconds(1788940800001)));
        Assert.False(credential.Expired(DateTimeOffset.FromUnixTimeMilliseconds(1788940799999)));
    }

    [Fact]
    public void AnApiKeySignInPublishesNoQuota()
    {
        var creds = Write("oauth_creds.json", """{"access_token":"ya29.abc","expiry_date":1788940800000}""");
        var settings = Write("settings.json", """{"security":{"auth":{"selectedType":"gemini-api-key"}}}""");
        var error = Assert.Throws<UsageError>(() => GeminiCredential.Read(creds, settings, Path.Combine(root, "none.json")));
        Assert.Equal(UsageErrorKind.NothingMetered, error.Kind);
    }

    [Fact]
    public void MissingCredentialMeansSignIn()
    {
        var error = Assert.Throws<UsageError>(() => GeminiCredential.Read(Path.Combine(root, "oauth_creds.json"), Path.Combine(root, "settings.json"), Path.Combine(root, "accounts.json")));
        Assert.Equal(UsageErrorKind.NeedsSignIn, error.Kind);
    }

    /// <summary>The CLI rewrites the file when it refreshes the token; landing on it half-written is not a sign-out.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("""{"access_token":"ya29.ab""")]
    public void AhalfWrittenCredentialIsTransient(string content)
    {
        var creds = Write("oauth_creds.json", content);
        var error = Assert.Throws<UsageError>(() => GeminiCredential.Read(creds, Path.Combine(root, "settings.json"), Path.Combine(root, "accounts.json")));
        Assert.Equal(UsageErrorKind.CredentialExpired, error.Kind);
    }

    private sealed class Answering(HttpStatusCode status, Func<HttpContent>? content = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request, Content = content?.Invoke() ?? new StringContent("{}") });
    }

    /// <summary>The spec says exponential, as for every other provider; a flat minute retried a throttled account every minute.</summary>
    [Fact]
    public async Task ConsecutiveRateLimitsBackOffExponentially()
    {
        var clock = DateTimeOffset.FromUnixTimeSeconds(1_790_000_000);
        var archive = new ReadingArchive(root, () => clock);
        using var provider = new GeminiProvider(new Answering(HttpStatusCode.TooManyRequests), () => new GeminiCredential("ya29.abc", clock.AddHours(1), null), () => clock, archive);
        var first = await Assert.ThrowsAsync<UsageError>(() => provider.ReadAsync());
        clock += first.RetryAfter + TimeSpan.FromSeconds(1);
        var second = await Assert.ThrowsAsync<UsageError>(() => provider.ReadAsync());
        Assert.Equal(UsageErrorKind.RateLimited, second.Kind);
        Assert.Equal(first.RetryAfter * 2, second.RetryAfter);
    }

    private sealed class Tracked(byte[] bytes) : ByteArrayContent(bytes)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// The caller owns the response only once it is handed one. A body that arrives whole but cannot be decoded
    /// throws after HttpClient has let go of the response, and nothing else would dispose it.
    /// </summary>
    [Fact]
    public async Task AbodyThatFailsToDecodeDoesNotLeakItsResponse()
    {
        var body = new Tracked("{}"u8.ToArray());
        body.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=no-such-charset");
        using var client = Http.Client(new Answering(HttpStatusCode.OK, () => body));
        await Assert.ThrowsAnyAsync<Exception>(() => CodeAssist.PostAsync(client, CodeAssist.Gate, "ya29.abc", "{}", CancellationToken.None));
        Assert.True(body.Disposed);
    }

    [Fact]
    public void GateYieldsProjectAndTierForAnEligibleAccount()
    {
        var gate = CodeAssist.ParseGate("""{"cloudaicompanionProject":"my-project","currentTier":{"id":"standard-tier","name":"Gemini Code Assist"},"allowedTiers":[{"id":"standard-tier"}]}""");
        Assert.True(gate.Eligible);
        Assert.Equal("my-project", gate.Project);
        Assert.Equal("Gemini Code Assist", gate.Tier);
        var asObject = CodeAssist.ParseGate("""{"cloudaicompanionProject":{"id":"proj-2"},"currentTier":{"id":"standard-tier"}}""");
        Assert.Equal("proj-2", asObject.Project);
        Assert.Equal("standard-tier", asObject.Tier);
    }

    [Fact]
    public void AnAllowedTierBesideAnUnsupportedClientReasonStaysEligible()
    {
        var body = File.ReadAllText(Docs.Path("fixtures", "antigravity", "load-code-assist.json"));
        using var document = System.Text.Json.JsonDocument.Parse(body);
        var gate = CodeAssist.ParseGate(document.RootElement.GetProperty("response").GetRawText());
        Assert.True(gate.Eligible);
        Assert.Equal("This client is no longer supported.", gate.Reason);
    }

    [Theory]
    [InlineData("gemini-2.5-pro", "Gemini 2.5 Pro")]
    [InlineData("gemini-2.5-flash-lite", "Gemini 2.5 Flash Lite")]
    [InlineData("gemini_3_pro_preview", "Gemini 3 Pro Preview")]
    public void ModelIdsBecomeReadableLabels(string id, string label) => Assert.Equal(label, CodeAssist.ModelLabel(id));
}
