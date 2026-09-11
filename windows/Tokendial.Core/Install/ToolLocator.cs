namespace Tokendial.Core.Install;

/// <summary>
/// Finds an installed tool from its recipe: declared locations first, then every directory on the PATH the
/// process, the user and the machine know. The registry PATHs matter because an installer that just ran
/// added its directory there, and this process still carries the PATH it was born with.
/// </summary>
public sealed class ToolLocator
{
    /// <summary>
    /// User and Machine are Windows registry scopes; asking for them on Unix throws PlatformNotSupported.
    /// </summary>
    private static readonly EnvironmentVariableTarget[] Targets =
        OperatingSystem.IsWindows()
            ? [EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine]
            : [EnvironmentVariableTarget.Process];

    /// <summary>
    /// Where a Unix installer puts a tool for one user. An application started from a desktop entry inherits
    /// the session's PATH, which on most distributions does not include ~/.local/bin - Claude Code's own
    /// installer warns about exactly that - so a tool that installed perfectly would be reported missing.
    /// </summary>
    private static readonly string[] UnixFallback =
        [".local/bin", "bin", ".opencode/bin", "/usr/local/bin", "/usr/bin", "/snap/bin",
         ".local/share/flatpak/exports/bin", "/var/lib/flatpak/exports/bin"];

    private readonly Func<EnvironmentVariableTarget, string?> readPath;
    private readonly string home;

    public ToolLocator(Func<EnvironmentVariableTarget, string?>? readPath = null, string? home = null)
    {
        this.readPath = readPath ?? (target => Environment.GetEnvironmentVariable("PATH", target));
        this.home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public bool IsInstalled(Detect detect) => Resolve(detect) is not null;

    /// <summary>The first location where the tool exists, or null.</summary>
    public string? Resolve(Detect detect)
    {
        foreach (var pattern in detect.Paths)
        {
            foreach (var candidate in Expand(pattern))
            {
                if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
            }
        }
        if (detect.Commands.Count == 0) return null;
        foreach (var directory in Directories())
        {
            foreach (var name in detect.Commands)
            {
                var candidate = Path.Combine(directory, name);
                if (Executable(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// On Unix a file is only a command if it is marked executable; a same-named data file is not the tool.
    /// A mode that cannot be read is treated as executable rather than as a miss.
    /// </summary>
    private static bool Executable(string path)
    {
        if (!File.Exists(path)) return false;
        if (OperatingSystem.IsWindows()) return true;
        try
        {
            var mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception) { return true; }
    }

    private IEnumerable<string> Directories()
    {
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var target in Targets)
        {
            var value = readPath(target) ?? "";
            foreach (var raw in value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var directory = Environment.ExpandEnvironmentVariables(raw.Trim('"'));
                if (directory.Length > 0 && seen.Add(directory)) yield return directory;
            }
        }

        if (OperatingSystem.IsWindows()) yield break;
        foreach (var entry in UnixFallback)
        {
            var directory = entry.StartsWith('/') ? entry : Path.Combine(home, entry);
            if (seen.Add(directory)) yield return directory;
        }
    }

    /// <summary>~ and %VAR% expanded; a segment containing * stands for every entry with that name, so a versioned package directory still resolves.</summary>
    public IEnumerable<string> Expand(string pattern)
    {
        var text = pattern == "~" || pattern.StartsWith("~/", StringComparison.Ordinal) ? home + pattern[1..] : pattern;
        text = Environment.ExpandEnvironmentVariables(text).Replace('/', Path.DirectorySeparatorChar);
        if (!text.Contains('*'))
        {
            yield return text;
            yield break;
        }
        var segments = text.Split(Path.DirectorySeparatorChar);
        var star = Array.FindIndex(segments, s => s.Contains('*'));
        var parent = string.Join(Path.DirectorySeparatorChar, segments[..star]);
        if (parent.Length == 0 || !Directory.Exists(parent)) yield break;
        var rest = string.Join(Path.DirectorySeparatorChar, segments[(star + 1)..]);
        foreach (var match in Entries(parent, segments[star]))
        {
            foreach (var expanded in Expand(rest.Length == 0 ? match : Path.Combine(match, rest))) yield return expanded;
        }
    }

    private static string[] Entries(string parent, string pattern)
    {
        try { return Directory.GetFileSystemEntries(parent, pattern); }
        catch (Exception) { return []; }
    }
}
