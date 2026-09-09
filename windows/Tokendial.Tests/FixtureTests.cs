using System.Globalization;
using System.Text;
using System.Text.Json;
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

[Collection("language")]
public class CopyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

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
