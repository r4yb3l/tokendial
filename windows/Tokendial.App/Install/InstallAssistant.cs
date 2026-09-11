using Tokendial.Core;
using System.IO;
using System.Windows.Threading;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.Install;
using Tokendial.Core.Providers;

namespace Tokendial.App.Install;

/// <summary>
/// The Install → Sign in → Ready assistant behind the provider rows. Owns one watcher per provider so an
/// install keeps being followed after the settings window closes, and raises everything on the UI thread.
/// </summary>
public sealed class InstallAssistant : IDisposable
{
    private static readonly Detect Winget = new(["winget.exe"], ["%LOCALAPPDATA%/Microsoft/WindowsApps/winget.exe"]);

    private readonly IReadOnlyList<IUsageProvider> providers;
    private readonly Dispatcher dispatcher;
    private readonly ToolLocator locator = new();
    private readonly Dictionary<string, InstallWatcher> watchers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InstallOutcome> outcomes = new(StringComparer.Ordinal);
    private readonly string scripts = Paths.In("install");

    public InstallAssistant(IReadOnlyList<IUsageProvider> providers, Dispatcher dispatcher)
    {
        this.providers = providers;
        this.dispatcher = dispatcher;
    }

    /// <summary>A step was passed, a wait began or ended.</summary>
    public event Action? Changed;

    /// <summary>The tool signed in: the provider can join the dial.</summary>
    public event Action<string>? SignedIn;

    public InstallRecipe? Recipe(string providerId) => InstallCatalog.For(providerId);

    public bool WingetMissing => !locator.IsInstalled(Winget);

    /// <summary>Where the provider's tool stands right now, from the disk: a profile beyond the default only exists once its tool is installed.</summary>
    public InstallState State(ProviderSummary summary)
    {
        var provider = providers.First(p => p.Id == summary.Id);
        var recipe = Recipe(summary.Id);
        var installed = recipe?.Here is not PlatformRecipe here || ProviderFamily.IsProfile(summary.Id) || locator.IsInstalled(here.Detect);
        return InstallProgress.Of(installed, provider.Account() is not null, summary.Connected);
    }

    public bool Waiting(string providerId) => watchers.TryGetValue(providerId, out var watcher) && watcher.Running;

    public InstallOutcome? Outcome(string providerId) => outcomes.TryGetValue(providerId, out var outcome) ? outcome : null;

    /// <summary>The platform recipe with the sign-in a profile actually needs.</summary>
    public PlatformRecipe Platform(string providerId)
    {
        var platform = Recipe(providerId)!.Here!;
        if (providers.First(p => p.Id == providerId) is IProfiled profiled && ProviderFamily.IsProfile(providerId) && platform.SignIn is SignInStep signIn)
            return platform with { SignIn = signIn with { Command = profiled.SignInCommand } };
        return platform;
    }

    /// <summary>Write the script, open the terminal on it, and follow the provider until it signs in.</summary>
    public void Run(string providerId, InstallAction action)
    {
        var provider = providers.First(p => p.Id == providerId);
        var recipe = Recipe(providerId);
        if (recipe is null) return;
        var platform = Platform(providerId);
        var path = InstallScript.Write(scripts, providerId, InstallScript.Compose(provider.DisplayName, recipe, platform, action));
        if (watchers.Remove(providerId, out var previous)) previous.Dispose();
        outcomes.Remove(providerId);

        var watcher = new InstallWatcher(providerId, () => locator.IsInstalled(platform.Detect), () => provider.Account() is not null, waitsAfterTerminal: platform.Kind == InstallKind.App);
        watcher.Advanced += _ => dispatcher.BeginInvoke(() => Changed?.Invoke());
        watcher.Completed += outcome => dispatcher.BeginInvoke(() =>
        {
            outcomes[providerId] = outcome;
            Log.Ui.Info($"install {providerId}: {outcome}");
            if (outcome == InstallOutcome.Success) SignedIn?.Invoke(providerId);
            Changed?.Invoke();
        });
        if (!TerminalRunner.Start(path, watcher.TerminalClosed))
        {
            watcher.Dispose();
            return;
        }
        watchers[providerId] = watcher;
        watcher.Start();
        Log.Ui.Info($"install {providerId}: {action} via {path}");
        Changed?.Invoke();
    }

    public void Dispose()
    {
        foreach (var watcher in watchers.Values) watcher.Dispose();
        watchers.Clear();
    }
}
