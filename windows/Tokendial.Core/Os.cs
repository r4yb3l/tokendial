namespace Tokendial.Core;

public enum Desktop { Windows, MacOS, Linux }

/// <summary>
/// The three directories every provider path is written against, per operating system.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="Environment.SpecialFolder"/>. On Unix .NET maps ApplicationData to
/// $XDG_CONFIG_HOME or ~/.config and LocalApplicationData to $XDG_DATA_HOME or ~/.local/share, so a path
/// written for Windows does not throw on Linux - it resolves to a directory that exists and holds nothing,
/// the credential is not found, and the provider reports "sign in" to someone who is signed in. Silent, not
/// loud, which is the whole reason this is a value rather than a call into the environment.
/// <para>
/// <see cref="For"/> is pure: given a home directory and a way to read environment variables it computes the
/// roots of any operating system from any other, so the path table can be asserted on Windows and mean
/// something about Linux. Separators are the target's, for the same reason.
/// </para>
/// </remarks>
public sealed record Roots(Desktop Desktop, string Home, string Config, string Data)
{
    public static Desktop Current =>
        OperatingSystem.IsWindows() ? Desktop.Windows : OperatingSystem.IsMacOS() ? Desktop.MacOS : Desktop.Linux;

    public static Roots Here { get; } =
        For(Current, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable);

    /// <summary>
    /// Windows reads %APPDATA% and %LOCALAPPDATA%; macOS keeps both in Application Support, which is what
    /// the Swift core and the specs say; Linux follows the XDG base directory specification, which requires
    /// a relative value to be ignored rather than resolved.
    /// </summary>
    public static Roots For(Desktop desktop, string home, Func<string, string?> env) => desktop switch
    {
        Desktop.Windows => new Roots(desktop, home,
            Absolute(env("APPDATA"), desktop) ?? Join(desktop, home, "AppData", "Roaming"),
            Absolute(env("LOCALAPPDATA"), desktop) ?? Join(desktop, home, "AppData", "Local")),

        Desktop.MacOS => new Roots(desktop, home,
            Join(desktop, home, "Library", "Application Support"),
            Join(desktop, home, "Library", "Application Support")),

        _ => new Roots(desktop, home,
            Absolute(env("XDG_CONFIG_HOME"), desktop) ?? Join(desktop, home, ".config"),
            Absolute(env("XDG_DATA_HOME"), desktop) ?? Join(desktop, home, ".local", "share"))
    };

    /// <summary>A path under this root, joined with the target operating system's separator.</summary>
    public string In(string root, params string[] parts) => Join(Desktop, root, parts);

    public string UnderHome(params string[] parts) => In(Home, parts);

    public char Separator => Desktop == Desktop.Windows ? '\\' : '/';

    private static string Join(Desktop desktop, string root, params string[] parts) =>
        string.Join(desktop == Desktop.Windows ? '\\' : '/', [root.TrimEnd('/', '\\'), .. parts]);

    /// <summary>An environment variable only counts when it names an absolute path, as the XDG specification requires.</summary>
    private static string? Absolute(string? value, Desktop desktop)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var rooted = desktop == Desktop.Windows
            ? value.Length > 2 && value[1] == ':'
            : value[0] == '/';
        return rooted ? value.TrimEnd('/', '\\') : null;
    }
}
