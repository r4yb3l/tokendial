using Tokendial.Core.Install;

namespace Tokendial.Tests;

public class ToolLocatorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tokendial-tests", Guid.NewGuid().ToString("N"));

    public ToolLocatorTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    /// <summary>
    /// A file the locator should accept as a command. Marked executable on Unix, because there a file
    /// without the bit is data rather than a tool - which is exactly what the locator now checks.
    /// </summary>
    private string Touch(params string[] parts)
    {
        var path = Path.Combine([root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    [Fact]
    public void FindsADeclaredPathUnderHome()
    {
        var exe = Touch(".local", "bin", "claude.exe");
        var locator = new ToolLocator(_ => null, home: root);
        Assert.Equal(exe, locator.Resolve(new Detect([], ["~/.local/bin/claude.exe"])));
        Assert.Null(locator.Resolve(new Detect([], ["~/.local/bin/codex.exe"])));
    }

    [Fact]
    public void AStarSegmentMatchesAVersionedDirectory()
    {
        var exe = Touch("Packages", "Anthropic.ClaudeCode_8wekyb3d8bbwe", "claude.exe");
        Touch("Packages", "Other_abc", "other.exe");
        var locator = new ToolLocator(_ => null, home: root);
        Assert.Equal(exe, locator.Resolve(new Detect([], ["~/Packages/Anthropic.ClaudeCode_*/claude.exe"])));
        Assert.Null(locator.Resolve(new Detect([], ["~/Packages/Nope_*/claude.exe"])));
    }

    /// <summary>
    /// The registry scopes exist only on Windows - asking for them on Unix throws - and they matter there
    /// because an installer that just ran added its directory to one of them while this process still
    /// carries the PATH it was born with.
    /// </summary>
    [Fact]
    public void ScansTheProcessUserAndMachinePaths()
    {
        if (!OperatingSystem.IsWindows()) return;
        var user = Touch("user", "grok.exe");
        var machine = Touch("machine", "codex.cmd");
        var locator = new ToolLocator(target => target switch
        {
            EnvironmentVariableTarget.Process => Path.Combine(root, "nothing"),
            EnvironmentVariableTarget.User => $"\"{Path.GetDirectoryName(user)}\"",
            _ => Path.GetDirectoryName(machine)
        }, home: root);
        Assert.Equal(user, locator.Resolve(new Detect(["grok.exe"], [])));
        Assert.Equal(machine, locator.Resolve(new Detect(["codex.exe", "codex.cmd"], [])));
        Assert.Null(locator.Resolve(new Detect(["opencode.exe"], [])));
    }

    /// <summary>
    /// An application started from a desktop entry inherits a PATH that usually lacks ~/.local/bin, which is
    /// where every per-user installer here puts its tool. Without the fallback, a tool that installed
    /// perfectly is reported missing.
    /// </summary>
    [Fact]
    public void FindsAToolInTheUnixFallbackPathWhenPathItselfDoesNotListIt()
    {
        if (OperatingSystem.IsWindows()) return;
        var claude = Touch(".local", "bin", "claude");
        var locator = new ToolLocator(_ => Path.Combine(root, "nothing"), home: root);
        Assert.Equal(claude, locator.Resolve(new Detect(["claude"], [])));
        Assert.Null(locator.Resolve(new Detect(["codex"], [])));
    }

    /// <summary>A file of the right name that nobody can run is not the tool.</summary>
    [Fact]
    public void AfileWithoutTheExecuteBitIsNotACommand()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = Path.Combine(root, ".local", "bin", "opencode");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var locator = new ToolLocator(_ => null, home: root);
        Assert.Null(locator.Resolve(new Detect(["opencode"], [])));
    }

    [Fact]
    public void DeclaredPathsWinOverPath()
    {
        var declared = Touch("Programs", "Cursor.exe");
        var onPath = Touch("bin", "Cursor.exe");
        var locator = new ToolLocator(_ => Path.GetDirectoryName(onPath), home: root);
        Assert.Equal(declared, locator.Resolve(new Detect(["Cursor.exe"], ["~/Programs/Cursor.exe"])));
    }
}
