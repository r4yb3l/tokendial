using System.Reflection;
using System.Text.Json;

namespace Tokendial.Core.Providers;

/// <summary>
/// Which operating systems a provider's usage reading actually works on, read from
/// <c>status.platforms</c> in the embedded specs.
/// </summary>
/// <remarks>
/// A provider that cannot be read somewhere has to say so. The alternative is what Antigravity did on Linux:
/// its token lives in the session keyring, nothing here reads one, so the credential came back empty and the
/// provider reported "sign in" - to someone who is signed in. A spec that declares the platform false turns
/// that into a sentence that is true.
/// <para>
/// Absent means everywhere, so the eight providers that work on all three say nothing.
/// </para>
/// </remarks>
public static class Platforms
{
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<Desktop, bool>>> declared = new(Load);

    public static bool SupportedHere(string providerId) => Supported(providerId, Roots.Current);

    public static bool Supported(string providerId, Desktop desktop) =>
        !declared.Value.TryGetValue(ProviderFamily.Of(providerId), out var platforms)
        || !platforms.TryGetValue(desktop, out var supported)
        || supported;

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<Desktop, bool>> Load()
    {
        var assembly = typeof(Platforms).Assembly;
        var table = new Dictionary<string, IReadOnlyDictionary<Desktop, bool>>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith("providers/", StringComparison.Ordinal) && n != "providers/schema.json"))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (!root.TryGetProperty("status", out var status) || !status.TryGetProperty("platforms", out var platforms)) continue;

            var per = new Dictionary<Desktop, bool>();
            foreach (var (key, desktop) in new[] { ("windows", Desktop.Windows), ("macos", Desktop.MacOS), ("linux", Desktop.Linux) })
            {
                if (platforms.TryGetProperty(key, out var value)) per[desktop] = value.GetBoolean();
            }
            if (per.Count > 0) table[root.GetProperty("id").GetString()!] = per;
        }
        return table;
    }
}
