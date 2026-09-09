using System.Reflection;
using System.Text.Json;
using Tokendial.Core.Providers;

namespace Tokendial.Core.Install;

/// <summary>The install recipes read from the provider specs embedded in this assembly. A spec without an install block, like GLM, has no recipe.</summary>
public static class InstallCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, InstallRecipe>> all = new(Load);

    public static IReadOnlyDictionary<string, InstallRecipe> All => all.Value;

    /// <summary>The recipe for a provider; Claude profiles (claude-&lt;slug&gt;) share Claude's.</summary>
    public static InstallRecipe? For(string providerId) => All.GetValueOrDefault(Family(providerId));

    public static string Family(string providerId) => ProviderFamily.Of(providerId);

    private static IReadOnlyDictionary<string, InstallRecipe> Load()
    {
        var assembly = typeof(InstallCatalog).Assembly;
        var recipes = new Dictionary<string, InstallRecipe>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("providers/", StringComparison.Ordinal) && n != "providers/schema.json"))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (!root.TryGetProperty("install", out var install)) continue;
            var id = root.GetProperty("id").GetString()!;
            recipes[id] = new InstallRecipe(id, install.GetProperty("vendor").GetString()!, install.GetProperty("docsUrl").GetString()!,
                Platform(install.GetProperty("windows")), Platform(install.GetProperty("macos")));
        }
        return recipes;
    }

    private static PlatformRecipe Platform(JsonElement element)
    {
        var detect = element.TryGetProperty("detect", out var d) ? new Detect(Texts(d, "commands"), Texts(d, "paths")) : Detect.None;
        var signIn = element.TryGetProperty("signIn", out var s) ? new SignInStep(s.GetProperty("command").GetString()!, s.GetProperty("hint").GetString()!) : null;
        var kind = element.GetProperty("kind").GetString() == "app" ? InstallKind.App : InstallKind.Cli;
        var download = element.TryGetProperty("downloadUrl", out var u) ? u.GetString() : null;
        return new PlatformRecipe(kind, detect, Texts(element, "requires"), element.GetProperty("install").GetString()!, signIn, download);
    }

    private static IReadOnlyList<string> Texts(JsonElement element, string name) =>
        element.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
            : [];
}
