using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tokendial.Core.I18n;

/// <summary>
/// The shared string catalogue in docs/i18n, embedded in the core. English is the base;
/// a language file overrides what it translates. Plurals follow CLDR categories, dates
/// and times follow the culture. Both platforms load the same files.
/// </summary>
public static class Strings
{
    public static readonly IReadOnlyList<(string Code, string Name)> Languages =
    [
        ("en", "English"), ("en-GB", "English (UK)"), ("es", "Español"), ("fr", "Français"), ("de", "Deutsch"), ("ar", "العربية")
    ];

    private static readonly object gate = new();
    private static Catalog baseCatalog = Catalog.Load("en");
    private static Catalog current = baseCatalog;
    private static CultureInfo culture = CultureInfo.CurrentUICulture;
    private static string? requested;

    public static event Action? Changed;

    /// <summary>The language in use: a code from <see cref="Languages"/>.</summary>
    public static string Language => current.Language;
    public static CultureInfo Culture => culture;
    public static bool RightToLeft => current.Direction == "rtl";

    /// <summary>Switch language. Null follows the system; an unknown code falls back to English.</summary>
    public static void Use(string? language)
    {
        var code = Resolve(language ?? CultureInfo.CurrentUICulture.Name);
        lock (gate)
        {
            requested = language;
            current = code == "en" ? baseCatalog : Catalog.Load(code);
            culture = CultureInfo.GetCultureInfo(code == "en" ? "en-US" : code == "ar" ? "ar-SA" : code);
        }
        Changed?.Invoke();
    }

    public static string? Requested => requested;

    /// <summary>"es-MX" → "es"; "en-GB" stays; anything unknown → "en".</summary>
    public static string Resolve(string name)
    {
        if (Languages.Any(l => l.Code.Equals(name, StringComparison.OrdinalIgnoreCase))) return Languages.First(l => l.Code.Equals(name, StringComparison.OrdinalIgnoreCase)).Code;
        var two = name.Split('-')[0].ToLowerInvariant();
        return Languages.Any(l => l.Code == two) ? two : "en";
    }

    public static string T(string key, params (string Name, object? Value)[] args) => Fill(Lookup(key) is string s ? s : key, args);

    /// <summary>A plural form for n, chosen by the language's CLDR rules; {n} is filled with the number.</summary>
    public static string Plural(string key, int n, params (string Name, object? Value)[] args)
    {
        var forms = LookupForms(key);
        if (forms is null) return T(key, [.. args, ("n", n)]);
        var category = PluralCategory(current.Language, n);
        var text = forms.GetValueOrDefault(category) ?? forms.GetValueOrDefault("other") ?? key;
        return Fill(text, [.. args, ("n", n)]);
    }

    /// <summary>A provider-produced English window label in the current language; unknown labels pass through.</summary>
    public static string Label(string english)
    {
        var exact = current.Labels.GetValueOrDefault(english) ?? baseCatalog.Labels.GetValueOrDefault(english);
        if (exact is not null) return exact;
        foreach (var (pattern, key) in LabelPatterns)
        {
            var m = pattern.Match(english);
            if (!m.Success) continue;
            var template = current.Labels.GetValueOrDefault(key) ?? baseCatalog.Labels.GetValueOrDefault(key);
            if (template is not null) return template.Replace("{n}", m.Groups[1].Value);
        }
        return english;
    }

    private static readonly (Regex, string)[] LabelPatterns =
    [
        (new Regex(@"^(\d+)h limit$"), "{n}h limit"),
        (new Regex(@"^(\d+)m limit$"), "{n}m limit"),
        (new Regex(@"^(\d+)d limit$"), "{n}d limit"),
        (new Regex(@"^Usage \((\d+) h\)$"), "Usage ({n} h)"),
        (new Regex(@"^Usage \((\d+) wk\)$"), "Usage ({n} wk)")
    ];

    public static string PluralCategory(string language, int n)
    {
        var lang = language.Split('-')[0];
        switch (lang)
        {
            case "ar":
                if (n == 0) return "zero";
                if (n == 1) return "one";
                if (n == 2) return "two";
                var mod = n % 100;
                if (mod is >= 3 and <= 10) return "few";
                if (mod is >= 11 and <= 99) return "many";
                return "other";
            case "fr":
                return n is 0 or 1 ? "one" : "other";
            default:
                return n == 1 ? "one" : "other";
        }
    }

    private static string? Lookup(string key)
    {
        if (current.Strings.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String) return value.GetString();
        if (baseCatalog.Strings.TryGetValue(key, out var fallback) && fallback.ValueKind == JsonValueKind.String) return fallback.GetString();
        return null;
    }

    private static Dictionary<string, string>? LookupForms(string key)
    {
        if (!(current.Strings.TryGetValue(key, out var forms) && forms.ValueKind == JsonValueKind.Object)
            && !(baseCatalog.Strings.TryGetValue(key, out forms) && forms.ValueKind == JsonValueKind.Object)) return null;
        return forms.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }

    private static string Fill(string text, (string Name, object? Value)[] args)
    {
        foreach (var (name, value) in args) text = text.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
        return text;
    }

    /// <summary>Every key in a catalogue, for the conformance test that keeps languages aligned.</summary>
    public static IReadOnlyDictionary<string, JsonElement> Keys(string language) => Catalog.Load(language).Strings;
    public static IReadOnlyDictionary<string, string> Labels(string language) => Catalog.Load(language).Labels;

    private sealed class Catalog
    {
        public string Language = "en";
        public string Direction = "ltr";
        public Dictionary<string, JsonElement> Strings = new();
        public Dictionary<string, string> Labels = new();

        public static Catalog Load(string code)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"i18n/{code}.json");
            if (stream is null) return code == "en" ? new Catalog() : Load("en");
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var catalog = new Catalog { Language = root.GetProperty("language").GetString() ?? code, Direction = root.TryGetProperty("direction", out var d) ? d.GetString() ?? "ltr" : "ltr" };
            if (root.TryGetProperty("strings", out var strings))
                foreach (var p in strings.EnumerateObject()) catalog.Strings[p.Name] = p.Value.Clone();
            if (root.TryGetProperty("labels", out var labels))
                foreach (var p in labels.EnumerateObject()) catalog.Labels[p.Name] = p.Value.GetString() ?? p.Name;
            return catalog;
        }
    }
}
