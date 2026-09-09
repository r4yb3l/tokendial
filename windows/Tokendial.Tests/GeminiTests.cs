using Tokendial.Core.Providers;
using Tokendial.Core.Providers.Gemini;
using Tokendial.Core.Providers.Google;

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
