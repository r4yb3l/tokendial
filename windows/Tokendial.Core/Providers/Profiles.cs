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

    /// <summary>
    /// The line that signs one profile in: the tool's directory variable, set for the shell that will run it,
    /// then the tool's own command. PowerShell assigns $env:NAME; a POSIX shell prefixes the command.
    /// </summary>
    /// <remarks>
    /// Neither shell expands ~ inside quotes, so the directory is built from the home the profile was found
    /// under, and the directory name is whatever the user called a folder, so it is quoted for each shell.
    /// sh gets double quotes so $HOME expands, with the four characters those quotes still interpret escaped.
    /// PowerShell gets none: the install assistant hands the line to powershell.exe -Command, and Windows
    /// PowerShell strips embedded double quotes from an argument it passes to a program. In single quotes only
    /// the quote itself is special, straight or curly, and doubling it is the escape.
    /// </remarks>
    public static string SignInLine(Desktop desktop, string variable, string directoryName, string command) =>
        desktop == Desktop.Windows
            ? $"$env:{variable} = Join-Path $env:USERPROFILE '{PowerShellSingleQuoted(directoryName)}'; {command}"
            : $"{variable}=\"$HOME/{PosixDoubleQuoted(directoryName)}\" {command}";

    private static string PowerShellSingleQuoted(string text) =>
        string.Concat(text.Select(c => c is '\'' or '\u2018' or '\u2019' or '\u201A' or '\u201B' ? $"{c}{c}" : c.ToString()));

    private static string PosixDoubleQuoted(string text) =>
        string.Concat(text.Select(c => c is '\\' or '"' or '$' or '`' ? $"\\{c}" : c.ToString()));
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
