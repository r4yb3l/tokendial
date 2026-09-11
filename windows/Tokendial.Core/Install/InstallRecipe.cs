namespace Tokendial.Core.Install;

public enum InstallKind
{
    /// <summary>A command-line tool: the terminal chains its sign-in right after installing.</summary>
    Cli,
    /// <summary>A desktop app: signing in means opening it.</summary>
    App
}

/// <summary>Where a tool shows up once installed: executable names looked up on PATH, and absolute locations with ~, %VAR% and one * per segment.</summary>
public sealed record Detect(IReadOnlyList<string> Commands, IReadOnlyList<string> Paths)
{
    public static readonly Detect None = new([], []);
}

/// <summary>The tool's own sign-in command and the catalogue key of the sentence shown before it runs.</summary>
public sealed record SignInStep(string Command, string Hint);

/// <summary>One operating system's recipe: how to tell the tool is there, the vendor's install line, and how it signs in.</summary>
public sealed record PlatformRecipe(InstallKind Kind, Detect Detect, IReadOnlyList<string> Requires, string Install, SignInStep? SignIn, string? DownloadUrl)
{
    public bool NeedsWinget => Requires.Contains("winget");
    public bool NeedsBrew => Requires.Contains("brew");
}

/// <summary>
/// The install block of one provider spec in docs/providers, embedded in this assembly and never fetched
/// from the network.
/// </summary>
/// <remarks>
/// A map rather than one field per operating system, because the pair it replaced meant "everything that is
/// not Windows is macOS" - and so the assistant offered Homebrew commands to a Mint user. A platform that is
/// absent is absent: the assistant has nothing to offer rather than something wrong.
/// </remarks>
public sealed record InstallRecipe(string ProviderId, string Vendor, string DocsUrl, IReadOnlyDictionary<Desktop, PlatformRecipe> Platforms)
{
    public PlatformRecipe? On(Desktop desktop) => Platforms.GetValueOrDefault(desktop);

    public PlatformRecipe? Here => On(Roots.Current);

    public PlatformRecipe Windows => Platforms[Desktop.Windows];
    public PlatformRecipe MacOS => Platforms[Desktop.MacOS];
    public PlatformRecipe? Linux => On(Desktop.Linux);
}
