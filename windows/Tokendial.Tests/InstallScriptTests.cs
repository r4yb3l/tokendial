using System.Text;
using Tokendial.Core.Install;

namespace Tokendial.Tests;

public class InstallScriptTests
{
    private static readonly InstallRecipe Grok = InstallCatalog.All["grok"];
    private static readonly InstallRecipe Copilot = InstallCatalog.All["copilot"];
    private static readonly InstallRecipe Cursor = InstallCatalog.All["cursor"];

    [Fact]
    public void ShowsTheCommandThenRunsItInAChildProcessAndStopsOnFailure()
    {
        var script = InstallScript.Compose("Grok", Grok, Grok.Windows, InstallAction.Install);
        var lines = script.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var shown = lines.FindIndex(l => l == "Write-Host '> irm https://x.ai/cli/install.ps1 | iex' -ForegroundColor Yellow");
        var run = lines.FindIndex(l => l == "& powershell.exe -NoProfile -ExecutionPolicy Bypass -Command 'irm https://x.ai/cli/install.ps1 | iex'");
        Assert.True(shown >= 0 && run > shown);
        Assert.Contains("if ($LASTEXITCODE -ne 0) {", script);
        Assert.Contains("[Environment]::GetEnvironmentVariable('Path', 'User')", script);
        Assert.Contains("Done. You can close this window.", script);
    }

    [Fact]
    public void ChainsTheSignInAfterInstallingACli()
    {
        var script = InstallScript.Compose("Grok", Grok, Grok.Windows, InstallAction.Install);
        var install = script.IndexOf("-Command 'irm https://x.ai/cli/install.ps1 | iex'", StringComparison.Ordinal);
        var signIn = script.IndexOf("-Command 'grok login'", StringComparison.Ordinal);
        Assert.True(install >= 0 && signIn > install);
        Assert.Contains("Grok opens the browser to sign in with your xAI account.", script);
    }

    [Fact]
    public void SignInOnlyRunsNoInstaller()
    {
        var script = InstallScript.Compose("Grok", Grok, Grok.Windows, InstallAction.SignIn);
        Assert.DoesNotContain("install.ps1", script);
        Assert.DoesNotContain("$LASTEXITCODE", script);
        Assert.Contains("-Command 'grok login'", script);
    }

    [Fact]
    public void WingetIsCheckedBeforeAWingetInstall()
    {
        var script = InstallScript.Compose("GitHub Copilot", Copilot, Copilot.Windows, InstallAction.Install);
        var check = script.IndexOf("Get-Command winget", StringComparison.Ordinal);
        var install = script.IndexOf("winget install GitHub.Copilot", StringComparison.Ordinal);
        Assert.True(check >= 0 && install > check);
        Assert.Contains("type /login", script);
    }

    [Fact]
    public void AnAppInstallEndsWithOpeningTheApp()
    {
        var script = InstallScript.Compose("Cursor", Cursor, Cursor.Windows, InstallAction.Install);
        Assert.Contains("Open Cursor and sign in to your account", script);
        Assert.DoesNotContain("-Command 'Cursor'", script);
    }

    [Fact]
    public void ContainsOnlyRecipeTextAndTheFixedTemplate()
    {
        var script = InstallScript.Compose("Grok", Grok, Grok.Windows, InstallAction.Install);
        var allowed = new[] { "Grok", Grok.Vendor, Grok.DocsUrl, Grok.Windows.Install, Grok.Windows.SignIn!.Command, "Grok opens the browser to sign in with your xAI account." };
        var stripped = allowed.Aggregate(script, (text, piece) => text.Replace(piece, ""));
        Assert.DoesNotContain("http", stripped);
        Assert.DoesNotContain("irm", stripped);
    }

    [Fact]
    public void QuotesEscapeOnlyTheSingleQuote()
    {
        Assert.Equal("'it''s | \"here\"'", InstallScript.Quote("it's | \"here\""));
        var profile = Grok.Windows with { SignIn = new SignInStep("$env:CLAUDE_CONFIG_DIR='~/.claude-work'; claude", "install.hint.claude") };
        var script = InstallScript.Compose("Claude Code (work)", Grok, profile, InstallAction.SignIn);
        Assert.Contains("-Command '$env:CLAUDE_CONFIG_DIR=''~/.claude-work''; claude'", script);
    }

    [Fact]
    public void WritesUtf8WithAByteOrderMark()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tokendial-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = InstallScript.Write(directory, "grok", "Write-Host 'é'");
            Assert.Equal(Path.Combine(directory, "grok.ps1"), path);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            Assert.Equal("Write-Host 'é'", Encoding.UTF8.GetString(bytes[3..]));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
