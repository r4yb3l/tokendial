using Tokendial.Core.Install;

namespace Tokendial.Tests;

public class ToolLocatorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tokendial-tests", Guid.NewGuid().ToString("N"));

    public ToolLocatorTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Touch(params string[] parts)
    {
        var path = Path.Combine([root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
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

    [Fact]
    public void ScansTheProcessUserAndMachinePaths()
    {
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

    [Fact]
    public void DeclaredPathsWinOverPath()
    {
        var declared = Touch("Programs", "Cursor.exe");
        var onPath = Touch("bin", "Cursor.exe");
        var locator = new ToolLocator(_ => Path.GetDirectoryName(onPath), home: root);
        Assert.Equal(declared, locator.Resolve(new Detect(["Cursor.exe"], ["~/Programs/Cursor.exe"])));
    }
}
