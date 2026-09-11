using System.Text;
using Tokendial.Core.I18n;

namespace Tokendial.Core.Install;

public enum InstallAction
{
    /// <summary>Install, then chain the tool's sign-in in the same window.</summary>
    Install,
    /// <summary>Only the tool's sign-in.</summary>
    SignIn
}

/// <summary>
/// The script the visible terminal runs: PowerShell on Windows, a POSIX shell everywhere else. Every line
/// comes from the recipe or from this fixed template: the vendor's command is printed before it runs, runs in
/// a child process so its own exit cannot cut the script short, and a failure stops everything after it. The
/// words are English on purpose: the console cannot shape Arabic, and the user already read the translated
/// sentence in the sheet.
/// </summary>
public static class InstallScript
{
    private static readonly Lazy<IReadOnlyDictionary<string, System.Text.Json.JsonElement>> english = new(() => Strings.Keys("en"));

    public static string Compose(string providerName, InstallRecipe recipe, PlatformRecipe platform, InstallAction action)
    {
        var script = new StringBuilder();
        script.AppendLine($"$Host.UI.RawUI.WindowTitle = {Quote($"Tokendial · {providerName}")}");
        script.AppendLine("Write-Host ''");
        script.AppendLine($"Write-Host {Quote($"Tokendial · {providerName}")} -ForegroundColor Cyan");
        script.AppendLine($"Write-Host {Quote($"Installer from {recipe.Vendor} · {recipe.DocsUrl}")}");
        script.AppendLine("Write-Host ''");
        if (action == InstallAction.Install)
        {
            if (platform.NeedsWinget)
            {
                script.AppendLine("if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {");
                script.AppendLine("  Write-Host 'Windows Package Manager (winget) was not found. Install \"App Installer\" from the Microsoft Store, then run this again.' -ForegroundColor Red");
                script.AppendLine("  return");
                script.AppendLine("}");
            }
            Run(script, platform.Install);
            script.AppendLine("if ($LASTEXITCODE -ne 0) {");
            script.AppendLine("  Write-Host \"Failed (code $LASTEXITCODE). Nothing else was run.\" -ForegroundColor Red");
            script.AppendLine("  return");
            script.AppendLine("}");
            script.AppendLine("$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + $env:Path");
            script.AppendLine("Write-Host ''");
            script.AppendLine("Write-Host 'Installed.' -ForegroundColor Green");
        }
        if (platform.Kind == InstallKind.Cli && platform.SignIn is SignInStep signIn)
        {
            script.AppendLine("Write-Host ''");
            script.AppendLine($"Write-Host {Quote(English(signIn.Hint))}");
            Run(script, signIn.Command);
        }
        else if (platform.Kind == InstallKind.App)
        {
            script.AppendLine($"Write-Host {Quote(English("install.hint.app").Replace("{name}", providerName))}");
        }
        script.AppendLine("Write-Host ''");
        script.AppendLine("Write-Host 'Done. You can close this window.' -ForegroundColor Green");
        return script.ToString();
    }

    /// <summary>
    /// The same script for a POSIX shell.
    /// </summary>
    /// <remarks>
    /// Its own template rather than the PowerShell one translated line by line, because two of the
    /// differences are not translations at all. <c>$?</c> is the status of the last command run and is
    /// replaced by the very next one, where <c>$LASTEXITCODE</c> survives an intervening Write-Host, so the
    /// status is captured on the spot. And <c>~</c> does not expand inside quotes in sh, so anything naming a
    /// home directory has to say <c>$HOME</c>.
    /// </remarks>
    public static string Shell(string providerName, InstallRecipe recipe, PlatformRecipe platform, InstallAction action)
    {
        var script = new StringBuilder();
        void Line(string text) => script.Append(text).Append('\n');

        Line("#!/bin/sh");
        Line($"printf '\\n%s\\n' {Posix($"Tokendial · {providerName}")}");
        Line($"printf '%s\\n\\n' {Posix($"Installer from {recipe.Vendor} · {recipe.DocsUrl}")}");

        // An app whose platform has no package-manager line is a download: there is nothing to run, and
        // emitting the empty command anyway would print a bare prompt and run an empty shell.
        if (action == InstallAction.Install && platform.Install.Length > 0)
        {
            foreach (var manager in platform.Requires)
            {
                Line($"if ! command -v {manager} >/dev/null 2>&1; then");
                Line($"  printf '%s\\n' {Posix(English(Requirement(manager)))}");
                Line("  exit 1");
                Line("fi");
            }
            RunShell(Line, platform.Install);
            Line("status=$?");
            Line("if [ \"$status\" -ne 0 ]; then");
            Line("  printf '%s\\n' \"Failed (code $status). Nothing else was run.\"");
            Line("  exit \"$status\"");
            Line("fi");
            // Every per-user installer here writes into ~/.local/bin, which the shell that started this
            // script may not have on its PATH yet.
            Line("PATH=\"$HOME/.local/bin:$PATH\"");
            Line("export PATH");
            Line("printf '\\n%s\\n' 'Installed.'");
        }

        if (platform.Kind == InstallKind.Cli && platform.SignIn is SignInStep signIn)
        {
            Line($"printf '\\n%s\\n' {Posix(English(signIn.Hint))}");
            RunShell(Line, signIn.Command);
        }
        else if (platform.Kind == InstallKind.App)
        {
            Line($"printf '%s\\n' {Posix(English("install.hint.app").Replace("{name}", providerName))}");
        }

        Line("printf '\\n%s\\n' 'Done. You can close this window.'");
        return script.ToString();
    }

    /// <summary>The catalogue key explaining a missing package manager.</summary>
    private static string Requirement(string manager) => manager switch
    {
        "winget" => "install.requires.winget",
        "brew" => "install.requires.brew",
        _ => "install.requires.npm"
    };

    private static void RunShell(Action<string> line, string command)
    {
        line($"printf '%s\\n\\n' {Posix("> " + command)}");
        line($"sh -c {Posix(command)}");
    }

    /// <summary>
    /// A POSIX single-quoted word. Inside one nothing is special, so the only case is the quote itself:
    /// it has to leave the quoting, be escaped, and go back in.
    /// </summary>
    public static string Posix(string text) => "'" + text.Replace("'", "'\\''") + "'";

    /// <summary>
    /// The shell script the terminal is pointed at. No byte-order mark - sh would read it as part of the
    /// shebang - LF written explicitly rather than left to Environment.NewLine, which would be right on Linux
    /// only by accident and could not be tested from anywhere else, and mode 700, because the file carries
    /// the user's own sign-in.
    /// </summary>
    public static string WriteShell(string directory, string providerId, string content)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, providerId + ".sh");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    /// <summary>The script file the terminal is pointed at, overwritten per provider. Written with a byte-order mark so Windows PowerShell 5.1 reads it as UTF-8.</summary>
    public static string Write(string directory, string providerId, string content)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, providerId + ".ps1");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    /// <summary>A PowerShell single-quoted literal: only the quote itself needs doubling.</summary>
    public static string Quote(string text) => "'" + text.Replace("'", "''") + "'";

    private static void Run(StringBuilder script, string command)
    {
        script.AppendLine($"Write-Host {Quote("> " + command)} -ForegroundColor Yellow");
        script.AppendLine("Write-Host ''");
        script.AppendLine($"& powershell.exe -NoProfile -ExecutionPolicy Bypass -Command {Quote(command)}");
    }

    private static string English(string key) => english.Value.TryGetValue(key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? key : key;
}
