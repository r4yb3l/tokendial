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
/// The PowerShell the visible terminal runs. Every line comes from the recipe or from this fixed template:
/// the vendor's command is printed before it runs, runs in a child process so its own exit cannot cut the
/// script short, and a failure stops everything after it. The words are English on purpose: the console
/// cannot shape Arabic, and the user already read the translated sentence in the sheet.
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
