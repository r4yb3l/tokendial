using System.Text;
using Tokendial.Core;
using Tokendial.Core.Install;
using Tokendial.Core.Providers.Claude;
using Tokendial.Core.Providers.Codex;

namespace Tokendial.Tests;

/// <summary>The POSIX half of the install script: the same template, in a shell that is not PowerShell.</summary>
public class ShellScriptTests
{
    private static readonly InstallRecipe Grok = InstallCatalog.All["grok"];
    private static readonly InstallRecipe Copilot = InstallCatalog.All["copilot"];
    private static readonly InstallRecipe Cursor = InstallCatalog.All["cursor"];

    private static string[] Lines(string script) => script.Split('\n');

    [Fact]
    public void ShowsTheCommandThenRunsItInAChildProcess()
    {
        var script = InstallScript.Shell("Grok", Grok, Grok.Linux!, InstallAction.Install);
        var lines = Lines(script).ToList();
        var shown = lines.FindIndex(l => l.Contains("'> curl -fsSL https://x.ai/cli/install.sh | bash'"));
        var run = lines.FindIndex(l => l == "sh -c 'curl -fsSL https://x.ai/cli/install.sh | bash'");
        Assert.True(shown >= 0 && run > shown);
        Assert.StartsWith("#!/bin/sh\n", script);
        Assert.Contains("Done. You can close this window.", script);
    }

    /// <summary>
    /// The one line that is not a translation of the PowerShell: $? is replaced by the next command, so it
    /// has to be captured before the message that reports it.
    /// </summary>
    [Fact]
    public void TheExitStatusIsCapturedBeforeAnythingElseRuns()
    {
        var lines = Lines(InstallScript.Shell("Grok", Grok, Grok.Linux!, InstallAction.Install)).ToList();
        var run = lines.FindIndex(l => l.StartsWith("sh -c 'curl"));
        Assert.Equal("status=$?", lines[run + 1]);
        Assert.Contains("if [ \"$status\" -ne 0 ]; then", lines);
    }

    [Fact]
    public void AmissingPackageManagerStopsBeforeTheInstall()
    {
        var script = InstallScript.Shell("GitHub Copilot", Copilot, Copilot.Linux!, InstallAction.Install);
        var check = script.IndexOf("if ! command -v npm >/dev/null 2>&1; then", StringComparison.Ordinal);
        var install = script.IndexOf("npm install -g @github/copilot'", StringComparison.Ordinal);
        Assert.True(check >= 0 && install > check);
        Assert.Contains("exit 1", script);
    }

    [Fact]
    public void ThePathGainsTheDirectoryEveryPerUserInstallerWritesInto()
    {
        var script = InstallScript.Shell("Grok", Grok, Grok.Linux!, InstallAction.Install);
        Assert.Contains("PATH=\"$HOME/.local/bin:$PATH\"", script);
        Assert.Contains("export PATH", script);
    }

    [Fact]
    public void SignInOnlyRunsNoInstaller()
    {
        var script = InstallScript.Shell("Grok", Grok, Grok.Linux!, InstallAction.SignIn);
        Assert.DoesNotContain("install.sh", script);
        Assert.DoesNotContain("status=$?", script);
        Assert.Contains("sh -c 'grok login'", script);
    }

    [Fact]
    public void AnAppInstallEndsWithOpeningTheApp()
    {
        var script = InstallScript.Shell("Cursor", Cursor, Cursor.Linux!, InstallAction.Install);
        Assert.Contains("Open Cursor and sign in", script);
        Assert.DoesNotContain("sh -c ''", script);
    }

    [Fact]
    public void QuotingLeavesTheQuoteEscapesItAndComesBack()
    {
        Assert.Equal("'it'\\''s | \"here\"'", InstallScript.Posix("it's | \"here\""));
        Assert.Equal("'plain'", InstallScript.Posix("plain"));
    }

    /// <summary>
    /// A profile's sign-in has to be written for the shell that will run it: PowerShell assigns $env:NAME,
    /// sh prefixes the command, and ~ does not expand inside quotes there.
    /// </summary>
    [Fact]
    public void AprofileNamesItsDirectoryTheWayTheShellWillUnderstand()
    {
        var claude = new ClaudeProfile("work", "/home/ada/.claude-work");
        Assert.Equal("$env:CLAUDE_CONFIG_DIR='~/.claude-work'; claude", claude.SignInFor(Desktop.Windows));
        Assert.Equal("CLAUDE_CONFIG_DIR=\"$HOME/.claude-work\" claude", claude.SignInFor(Desktop.Linux));

        var codex = new CodexProfile("work", "/home/ada/.codex-work");
        Assert.Equal("CODEX_HOME=\"$HOME/.codex-work\" codex login", codex.SignInFor(Desktop.MacOS));
        Assert.Equal("codex login", new CodexProfile(null, "/home/ada/.codex").SignInFor(Desktop.Linux));
    }

    [Fact]
    public void WritesLfWithNoByteOrderMarkAndOnlyForTheUser()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tokendial-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = InstallScript.WriteShell(directory, "grok", "printf 'é\\n'\n");
            Assert.Equal(Path.Combine(directory, "grok.sh"), path);
            var bytes = File.ReadAllBytes(path);
            Assert.NotEqual(0xEF, bytes[0]);
            Assert.DoesNotContain((byte)'\r', bytes);
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>
    /// Nothing but the recipe's own text and the fixed template reaches the terminal. Asserted for every
    /// recipe on every platform rather than for one of them, because the whole point of a generated script is
    /// that nobody reads it before it runs.
    /// </summary>
    [Theory]
    [MemberData(nameof(Everything))]
    public void AscriptCarriesNothingBeyondItsRecipe(string providerId, Desktop desktop, InstallAction action)
    {
        var recipe = InstallCatalog.All[providerId];
        var platform = recipe.On(desktop)!;
        var script = desktop == Desktop.Windows
            ? InstallScript.Compose(providerId, recipe, platform, action)
            : InstallScript.Shell(providerId, recipe, platform, action);

        var allowed = new List<string> { providerId, recipe.Vendor, recipe.DocsUrl, platform.Install };
        if (platform.SignIn is SignInStep signIn) allowed.Add(signIn.Command);
        allowed.AddRange(Hints());
        // Longest first: stripping "gemini" before the docs URL would leave a mangled URL that no longer
        // matches, and the test would fail on its own arithmetic rather than on the script.
        var residue = allowed.Where(a => a.Length > 0).OrderByDescending(a => a.Length)
            .Aggregate(script, (text, piece) => text.Replace(piece, ""));

        foreach (var forbidden in new[] { "://", "curl", "wget", "sudo", "rm ", "$(", "`" })
            Assert.DoesNotContain(forbidden, residue);
    }

    public static IEnumerable<object[]> Everything() =>
        from recipe in InstallCatalog.All.Values
        from platform in recipe.Platforms.Keys
        from action in new[] { InstallAction.Install, InstallAction.SignIn }
        select new object[] { recipe.ProviderId, platform, action };

    /// <summary>The English sentences the template prints; they are catalogue text, not recipe text.</summary>
    private static IEnumerable<string> Hints() =>
        Tokendial.Core.I18n.Strings.Keys("en")
            .Where(k => k.Key.StartsWith("install.hint.", StringComparison.Ordinal) || k.Key.StartsWith("install.requires.", StringComparison.Ordinal))
            .Select(k => k.Value.GetString() ?? "")
            .SelectMany(text => new[] { text, text.Replace("{name}", "") });
}
