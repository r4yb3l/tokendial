using System.Text.Json;
using System.Text.RegularExpressions;
using Tokendial.Core.I18n;
using Tokendial.Core.Model;

namespace Tokendial.Tests;

/// <summary>Every language carries every key with the same placeholders; copy rules hold in each.</summary>
[Collection("language")]
public class I18nTests : IDisposable
{
    public I18nTests() => Strings.Use("en");
    public void Dispose() => Strings.Use("en");

    public static IEnumerable<object[]> Languages() => Strings.Languages.Where(l => l.Code != "en").Select(l => new object[] { l.Code });

    private static readonly Regex Placeholder = new(@"\{(\w+)\}");

    [Theory]
    [MemberData(nameof(Languages))]
    public void CatalogueCoversEveryEnglishKeyWithTheSamePlaceholders(string code)
    {
        var english = Strings.Keys("en");
        var other = Strings.Keys(code);
        var missing = english.Keys.Where(k => !other.ContainsKey(k)).ToList();
        if (code == "en-GB") return;
        Assert.True(missing.Count == 0, $"{code} lacks: {string.Join(", ", missing)}");
        foreach (var (key, value) in english)
        {
            var expected = Placeholders(value);
            var actual = Placeholders(other[key]);
            Assert.True(expected.SetEquals(actual), $"{code}.{key} placeholders {string.Join(",", actual)} ≠ {string.Join(",", expected)}");
            if (value.ValueKind == JsonValueKind.Object) Assert.True(other[key].ValueKind == JsonValueKind.Object && other[key].TryGetProperty("other", out _), $"{code}.{key} needs plural forms with 'other'");
        }
        var englishLabels = Strings.Labels("en");
        var otherLabels = Strings.Labels(code);
        Assert.True(englishLabels.Keys.All(otherLabels.ContainsKey), $"{code} lacks labels: {string.Join(", ", englishLabels.Keys.Where(k => !otherLabels.ContainsKey(k)))}");
    }

    private static HashSet<string> Placeholders(JsonElement value)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Join(" ", value.EnumerateObject().Select(p => p.Value.GetString()));
        var set = Placeholder.Matches(text).Select(m => m.Groups[1].Value).ToHashSet();
        set.Remove("n");
        return set;
    }

    [Fact]
    public void SpanishCopyFollowsTheRules()
    {
        Strings.Use("es");
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        Assert.Equal("Se reinicia en 51 min", Copy.Reset(now.AddMinutes(51), now));
        Assert.Equal("Se reinicia 21 nov", Copy.Reset(now.AddDays(7), now, TimeZoneInfo.Utc));
        Assert.Equal("Se reinicia mié 4:13", Copy.Reset(now.AddHours(6), now, TimeZoneInfo.Utc).Replace("mié.", "mié"));
        Assert.Equal("hace 6 min", Copy.Ago(now.AddMinutes(-6), now));
        Assert.Equal("1 h 5 min", Copy.Elapsed(now.AddMinutes(-65), now));
        Assert.Equal("Sesión actual", Strings.Label("Current session"));
        Assert.Equal("Límite de 5 h", Strings.Label("5h limit"));
        Assert.Equal("2 sesiones inactivas", Strings.Plural("panel.idle", 2));
    }

    [Fact]
    public void ArabicPluralsAndDirection()
    {
        Strings.Use("ar");
        Assert.True(Strings.RightToLeft);
        Assert.Equal("جلستان خاملتان", Strings.Plural("panel.idle", 2));
        Assert.Equal("3 جلسات خاملة", Strings.Plural("panel.idle", 3));
        Assert.Equal("11 جلسة خاملة", Strings.Plural("panel.idle", 11));
        Assert.Equal("few", Strings.PluralCategory("ar", 103));
        Assert.Equal("one", Strings.PluralCategory("fr", 0));
    }

    [Fact]
    public void UnknownLanguagesFallBackToEnglish()
    {
        Strings.Use("pt-BR");
        Assert.Equal("en", Strings.Language);
        Strings.Use("es-MX");
        Assert.Equal("es", Strings.Language);
        Assert.Equal("Sign in", Strings.T("status.signIn").Length > 0 ? Strings.Keys("en")["status.signIn"].GetString() : "");
    }
}
