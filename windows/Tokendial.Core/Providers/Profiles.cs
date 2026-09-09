namespace Tokendial.Core.Providers;

/// <summary>
/// One account per configuration directory: the tool's default directory, then every sibling named
/// &lt;prefix&gt;-&lt;slug&gt; the tool has actually used. Claude Code does this through CLAUDE_CONFIG_DIR, Codex through CODEX_HOME.
/// </summary>
public static class Profiles
{
    /// <summary>The default directory first (whether or not it exists), then the used extras in ordinal slug order. A directory counts as used when any marker is present.</summary>
    public static IReadOnlyList<(string? Slug, string Directory)> Discover(string home, string prefix, IReadOnlyList<string> markers)
    {
        var extras = new List<(string? Slug, string Directory)>();
        if (Directory.Exists(home))
        {
            foreach (var dir in Directory.EnumerateDirectories(home, prefix + "-*"))
            {
                var slug = Path.GetFileName(dir)[(prefix.Length + 1)..];
                if (slug.Length == 0) continue;
                if (!markers.Any(m => Path.Exists(Path.Combine(dir, m)))) continue;
                extras.Add((slug, dir));
            }
        }
        extras.Sort((a, b) => string.CompareOrdinal(a.Slug, b.Slug));
        return [(null, Path.Combine(home, prefix)), .. extras];
    }
}

/// <summary>A provider that is one profile of a tool and knows the exact command that signs that profile in.</summary>
public interface IProfiled
{
    string SignInCommand { get; }
}

/// <summary>Which provider an id belongs to: claude-work is a Claude profile, codex-team a Codex one; any other id is its own family.</summary>
public static class ProviderFamily
{
    public static string Of(string providerId)
    {
        var dash = providerId.IndexOf('-');
        if (dash <= 0) return providerId;
        var head = providerId[..dash];
        return ProviderCatalog.Order.Contains(head) ? head : providerId;
    }

    public static bool IsProfile(string providerId) => Of(providerId) != providerId;
}
