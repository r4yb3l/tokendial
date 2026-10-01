using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Tokendial.Core.I18n;
using Tokendial.Core.Model;
using Tokendial.Core.Providers;
using Tokendial.Core.Providers.Antigravity;
using Tokendial.Core.Providers.Gemini;
using Tokendial.Core.Providers.Claude;
using Tokendial.Core.Providers.Codex;
using Tokendial.Core.Providers.Copilot;
using Tokendial.Core.Providers.Cursor;
using Tokendial.Core.Providers.Glm;
using Tokendial.Core.Providers.Grok;
using Tokendial.Core.Providers.OpenCode;
using Tokendial.Core.Store;

namespace Tokendial.Tests;

/// <summary>Every response fixture in docs/fixtures is parsed by its provider and compared to what the spec says it means.</summary>
public class FixtureTests
{
    public static IEnumerable<object[]> Fixtures() =>
        Directory.EnumerateFiles(Docs.Path("fixtures"), "*.json", SearchOption.AllDirectories)
            .OrderBy(p => p)
            .Select(p => new object[] { Path.GetFileName(Path.GetDirectoryName(p)!), Path.GetFileName(p) });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void FixtureParsesAsSpecified(string provider, string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Docs.Path("fixtures", provider, file)));
        var root = document.RootElement;
        var response = root.GetProperty("response").GetRawText();
        var expected = root.GetProperty("expected");
        var now = root.TryGetProperty("now", out var n) ? DateTimeOffset.FromUnixTimeSeconds(n.GetInt64()) : DateTimeOffset.UtcNow;

        if (expected.TryGetProperty("gate", out _)) return;
        if (expected.TryGetProperty("accessToken", out var token))
        {
            var credential = AntigravityCredential.Decode(Encoding.UTF8.GetBytes("go-keyring-base64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(response))))!;
            Assert.Equal(token.GetString(), credential.AccessToken);
            Assert.Equal(expected.GetProperty("authMethod").GetString(), credential.AuthMethod);
            Assert.Equal(DateTimeOffset.Parse(expected.GetProperty("expiresAtUtc").GetString()!, CultureInfo.InvariantCulture), credential.ExpiresAt);
            return;
        }

        Parsed Parse() => (provider, file) switch
        {
            ("claude", _) => ClaudeUsage.Parse(response),
            ("codex", _) => CodexUsage.Parse(response, now),
            ("copilot", _) => CopilotUsage.Parse(response),
            ("cursor", _) => CursorUsage.Parse(response),
            ("glm", _) => GlmUsage.Parse(response),
            ("grok", _) => GrokUsage.Parse(response),
            ("opencode", _) => OpenCodeUsage.Parse(response),
            ("antigravity", "bridge-quota.json") => new Parsed(Bridge.ParseQuota(response), "gemini-weekly"),
            ("antigravity", "google-quota.json") => new Parsed(AntigravityProvider.ParseGoogleQuota(response), null),
            ("gemini", "unsupported-client.json") => GeminiUsage.ParseGate(response),
            ("gemini", _) => GeminiUsage.Parse(response),
            _ => throw new InvalidOperationException($"no parser for {provider}/{file}")
        };

        if (expected.TryGetProperty("error", out var error))
        {
            var thrown = Assert.Throws<UsageError>(() => Parse());
            Assert.Equal(error.GetString(), thrown.Kind switch
            {
                UsageErrorKind.NeedsSignIn => "needsAuth",
                UsageErrorKind.CredentialExpired => "credentialExpired",
                UsageErrorKind.RateLimited => "rateLimited",
                UsageErrorKind.NothingMetered => "nothingMetered",
                _ => "badResponse"
            });
            if (expected.TryGetProperty("status", out var status)) Assert.Equal(status.GetInt32(), thrown.Status);
            return;
        }

        var parsed = Parse();
        if (expected.TryGetProperty("headline", out var headline)) Assert.Equal(headline.GetString(), parsed.Headline);
        if (expected.TryGetProperty("plan", out var plan)) Assert.Equal(plan.GetString(), parsed.Plan);
        var windows = expected.GetProperty("windows").EnumerateArray().ToList();
        Assert.Equal(windows.Select(w => w.GetProperty("id").GetString()), parsed.Windows.Select(w => w.Id));
        for (var i = 0; i < windows.Count; i++)
        {
            Assert.Equal(windows[i].GetProperty("label").GetString(), parsed.Windows[i].Label);
            Assert.Equal(windows[i].GetProperty("usedFraction").GetDouble(), parsed.Windows[i].UsedFraction!.Value, 5);
            if (windows[i].TryGetProperty("resetsAt", out var reset))
                Assert.Equal(DateTimeOffset.Parse(reset.GetString()!, CultureInfo.InvariantCulture), parsed.Windows[i].ResetsAt);
            else
                Assert.Null(parsed.Windows[i].ResetsAt);
        }
    }
}

/// <summary>
/// What a provider says when it cannot get a usable answer right now. None of these is a sign-out: a sign-out
/// erases the last reading and sends a signed-in user to sign in again.
/// </summary>
public class CredentialReadTests : IDisposable
{
    private const string CopilotApps = """{"github.com:Iv1.x":{"oauth_token":"gho_test","user":"ada"}}""";
    private readonly string root = Path.Combine(Path.GetTempPath(), "tokendial-tests", Guid.NewGuid().ToString("N"));

    public CredentialReadTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Write(string name, string content)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private CopilotProvider Copilot(string apps, HttpStatusCode status = HttpStatusCode.OK, params (string Name, string Value)[] headers) =>
        new(new Answering(status, headers), new ReadingArchive(root), new CopilotCredential.Files(apps, Path.Combine(root, "hosts.json"), Path.Combine(root, "hosts.yml")));

    private sealed class Answering : HttpMessageHandler
    {
        private readonly HttpStatusCode status;
        private readonly (string Name, string Value)[] headers;

        public Answering(HttpStatusCode status, (string Name, string Value)[] headers)
        {
            this.status = status;
            this.headers = headers;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status) { RequestMessage = request, Content = new StringContent("{}") };
            foreach (var (name, value) in headers) response.Headers.TryAddWithoutValidation(name, value);
            return Task.FromResult(response);
        }
    }

    /// <summary>The owner rewrites its file in place when it refreshes the token, so a read can land on an empty or half-written one.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("""{"claudeAiOauth":{"accessTo""")]
    public void AhalfWrittenCredentialIsTransientNotASignOut(string content)
    {
        var file = Write("credential.json", content);
        Assert.Equal(UsageErrorKind.CredentialExpired, Assert.Throws<UsageError>(() => ClaudeCredential.Read(file)).Kind);
        Assert.Equal(UsageErrorKind.CredentialExpired, Assert.Throws<UsageError>(() => CodexCredential.Read(file)).Kind);
        Assert.Equal(UsageErrorKind.CredentialExpired, Assert.Throws<UsageError>(() => GrokCredential.Read(file, DateTimeOffset.UnixEpoch)).Kind);
    }

    [Fact]
    public void AnAbsentOrSignedOutCredentialIsStillASignIn()
    {
        var missing = Path.Combine(root, "missing.json");
        Assert.Equal(UsageErrorKind.NeedsSignIn, Assert.Throws<UsageError>(() => ClaudeCredential.Read(missing)).Kind);
        Assert.Equal(UsageErrorKind.NeedsSignIn, Assert.Throws<UsageError>(() => CodexCredential.Read(missing)).Kind);
        var signedOut = Write("signed-out.json", "{}");
        Assert.Equal(UsageErrorKind.NeedsSignIn, Assert.Throws<UsageError>(() => ClaudeCredential.Read(signedOut)).Kind);
        Assert.Equal(UsageErrorKind.NeedsSignIn, Assert.Throws<UsageError>(() => CodexCredential.Read(signedOut)).Kind);
        Assert.Equal(UsageErrorKind.NeedsSignIn, Assert.Throws<UsageError>(() => GrokCredential.Read(signedOut, DateTimeOffset.UnixEpoch)).Kind);
    }

    /// <summary>A file the owner holds locked is transient, and must not fall through to the next file's token, another account's.</summary>
    [Fact]
    public async Task ALockedCopilotFileIsTransient()
    {
        var apps = Write("apps.json", CopilotApps);
        using var provider = Copilot(apps);
        using (new FileStream(apps, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Null(provider.Account());
            Assert.Equal(UsageErrorKind.CredentialExpired, (await Assert.ThrowsAsync<UsageError>(() => provider.ReadAsync())).Kind);
        }
    }

    /// <summary>
    /// A refused read used to escape Account() as UnauthorizedAccessException, which the settings window calls
    /// unguarded. Only stageable where a file mode can refuse the owner; root reads anything, so it says so and stops.
    /// </summary>
    [Fact]
    public async Task ArefusedCopilotReadIsTransientAndTheAccountRowSurvivesIt()
    {
        if (OperatingSystem.IsWindows()) return;
        var apps = Write("apps.json", CopilotApps);
        using var provider = Copilot(apps);
        File.SetUnixFileMode(apps, UnixFileMode.None);
        try
        {
            try { File.ReadAllBytes(apps); return; }
            catch (UnauthorizedAccessException) { }
            Assert.Null(provider.Account());
            Assert.Equal(UsageErrorKind.CredentialExpired, (await Assert.ThrowsAsync<UsageError>(() => provider.ReadAsync())).Kind);
        }
        finally { File.SetUnixFileMode(apps, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    }

    /// <summary>GitHub answers an exhausted rate limit with 403 as often as 429; only the headers tell it from a missing seat.</summary>
    [Theory]
    [InlineData("x-ratelimit-remaining", "0", UsageErrorKind.RateLimited)]
    [InlineData("Retry-After", "30", UsageErrorKind.RateLimited)]
    [InlineData("x-ratelimit-remaining", "4999", UsageErrorKind.NothingMetered)]
    public async Task ACopilot403IsARateLimitOnlyWhenGitHubSaysSo(string header, string value, UsageErrorKind kind)
    {
        using var provider = Copilot(Write("apps.json", CopilotApps), HttpStatusCode.Forbidden, (header, value));
        Assert.Equal(kind, (await Assert.ThrowsAsync<UsageError>(() => provider.ReadAsync())).Kind);
    }

    /// <summary>
    /// While the editor writes, its store answers busy. The immutable fallback then read the token as of the last
    /// checkpoint, one the editor had already replaced: a 401, and a signed-in user told to sign in.
    /// </summary>
    [Fact]
    public void ACursorStoreTheEditorHoldsBusyIsTransientNotAStaleToken()
    {
        var store = Path.Combine(root, "state.vscdb");
        var connection = new SqliteConnectionStringBuilder { DataSource = store, Pooling = false }.ToString();
        using (var setup = new SqliteConnection(connection))
        {
            setup.Open();
            using var create = setup.CreateCommand();
            create.CommandText = "CREATE TABLE ItemTable (key TEXT UNIQUE, value BLOB); INSERT INTO ItemTable VALUES ('cursorAuth/accessToken', 'token'), ('cursorAuth/stripeMembershipAuthId', 'account');";
            create.ExecuteNonQuery();
        }
        Assert.Equal("token", CursorCredential.Read(store).AccessToken);

        using var editor = new SqliteConnection(connection);
        editor.Open();
        using (var begin = editor.CreateCommand())
        {
            begin.CommandText = "BEGIN EXCLUSIVE";
            begin.ExecuteNonQuery();
        }
        Assert.Equal(UsageErrorKind.CredentialExpired, Assert.Throws<UsageError>(() => CursorCredential.Read(store)).Kind);
    }
}

/// <summary>
/// The expectations are English, and dates are formatted through Strings.Culture, which starts as the
/// machine's own; without pinning the language these failed on any machine not set to English.
/// </summary>
[Collection("language")]
public class CopyTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    public CopyTests() => Strings.Use("en");
    public void Dispose() => Strings.Use("en");

    [Fact]
    public void ResetCopyFollowsTheRules()
    {
        Assert.Equal("Resets in 51 min", Copy.Reset(Now.AddMinutes(51), Now));
        Assert.Equal("Resets in 50 min", Copy.Reset(Now.AddSeconds(50 * 60 + 20), Now));
        Assert.Equal("Resets in 51 min", Copy.Reset(Now.AddSeconds(50 * 60 + 40), Now));
        Assert.DoesNotContain("min", Copy.Reset(Now.AddSeconds(59 * 60 + 40), Now));
        Assert.Contains(":", Copy.Reset(Now.AddHours(6), Now, TimeZoneInfo.Utc));
        Assert.Equal("Resets Nov 21", Copy.Reset(Now.AddDays(7), Now, TimeZoneInfo.Utc));
        Assert.Equal("Resetting…", Copy.Reset(Now.AddSeconds(-5), Now));
    }

    [Fact]
    public void ElapsedCopyFollowsTheRules()
    {
        Assert.Equal("just now", Copy.Elapsed(Now.AddSeconds(-44), Now));
        Assert.Equal("6 min", Copy.Elapsed(Now.AddMinutes(-6), Now));
        Assert.Equal("1 hr", Copy.Elapsed(Now.AddMinutes(-60), Now));
        Assert.Equal("1 hr 5 min", Copy.Elapsed(Now.AddMinutes(-65), Now));
        Assert.Equal("just now", Copy.Elapsed(Now.AddSeconds(120), Now));
        Assert.Equal("6 min ago", Copy.Ago(Now.AddMinutes(-6), Now));
        Assert.Equal("Paused until Wed 4:00 AM", Copy.Until("Paused", Now.AddHours(6).AddMinutes(-13).AddSeconds(-20), Now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void ForecastCopyNamesTheHourOrTheReset()
    {
        Assert.Equal("At this pace, empty at 11:06 PM", Copy.Forecast(new Forecast(ForecastKind.RunsOut, Now.AddMinutes(53)), Now, TimeZoneInfo.Utc));
        Assert.Equal("At this pace, empty at Wed 2:26 AM", Copy.Forecast(new Forecast(ForecastKind.RunsOut, Now.AddHours(4).AddMinutes(13)), Now, TimeZoneInfo.Utc));
        Assert.Equal("At this pace it lasts until the reset", Copy.Forecast(new Forecast(ForecastKind.LastsUntilReset, null), Now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void SummaryReadsBothEnds()
    {
        Assert.Equal("63% used · 37% left", new UsageWindow("w", "W", 0.63).Summary(Fidelity.Official));
        Assert.Equal("~104% used · 0% left", new UsageWindow("w", "W", 1.04).Summary(Fidelity.Derived));
        Assert.Equal("~7 requests today", new UsageWindow("w", "W", Count: 7).Summary(Fidelity.Derived));
        Assert.Equal(Band.Watch, Bands.Of(0.5));
        Assert.Equal(Band.Critical, Bands.Of(0.8));
        Assert.Equal(Band.Ample, Bands.Of(0.49));
    }
}
