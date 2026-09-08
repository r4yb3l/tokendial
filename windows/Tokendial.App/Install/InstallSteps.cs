using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using Tokendial.App.Panel;
using Tokendial.App.Windows;
using Tokendial.Core.I18n;
using Tokendial.Core.Install;
using Tokendial.Core.Providers;

namespace Tokendial.App.Install;

/// <summary>The Install → Sign in → Ready strip and the one button that moves it forward, shared by the settings and welcome rows.</summary>
public static class InstallSteps
{
    private static readonly (string Key, InstallState Reached)[] Steps =
    [
        ("install.step.install", InstallState.Installed),
        ("install.step.signIn", InstallState.SignedIn),
        ("install.step.ready", InstallState.Connected)
    ];

    /// <summary>Filled dots for steps already passed, a bright one for the current step, dim ones ahead.</summary>
    public static UIElement Strip(InstallState state)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        for (var i = 0; i < Steps.Length; i++)
        {
            var (key, reached) = Steps[i];
            var done = state >= reached;
            var current = !done && (i == 0 || state >= Steps[i - 1].Reached);
            if (i > 0) strip.Children.Add(new Border { Width = 16, Height = 1, Background = done ? Chrome.Accent : Theme.Hairline, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
            strip.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = done ? Chrome.Accent : current ? Theme.TextPrimary : Theme.Transparent,
                Stroke = done ? Chrome.Accent : current ? Theme.TextPrimary : Theme.TextDisabled,
                StrokeThickness = 1.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            });
            var label = Text.Make(Strings.T(key), 11, done || current ? Theme.TextPrimary : Theme.TextDisabled);
            label.VerticalAlignment = VerticalAlignment.Center;
            strip.Children.Add(label);
        }
        return strip;
    }

    /// <summary>"Anthropic · Not installed", or the wait, or what to do after a terminal closed without a sign-in.</summary>
    public static string Detail(string providerId, string name, InstallRecipe recipe, InstallState state, InstallAssistant assistant)
    {
        if (assistant.Waiting(providerId)) return Strings.T("install.waiting", ("name", name));
        if (state < InstallState.SignedIn && assistant.Outcome(providerId) is InstallOutcome.TimedOut or InstallOutcome.TerminalClosed) return Strings.T("install.state.notSeen");
        var stateText = state switch
        {
            InstallState.NotInstalled => Strings.T("install.state.notInstalled"),
            InstallState.Installed => Strings.T("install.state.installed"),
            _ => Strings.T("install.state.signedIn")
        };
        return Strings.T("install.state.by", ("vendor", recipe.Vendor), ("state", stateText));
    }

    /// <summary>The next action, or null once the tool is signed in.</summary>
    public static Button? Action(Window owner, ProviderSummary summary, InstallRecipe recipe, InstallState state, InstallAssistant assistant, Func<bool> openApp)
    {
        var platform = assistant.Platform(summary.Id);
        switch (state)
        {
            case InstallState.NotInstalled:
                return Chrome.Button(Strings.T("install.button.install"),
                    () => RunSheet.Show(owner, summary.Name, recipe, platform, InstallAction.Install, assistant.WingetMissing, () => assistant.Run(summary.Id, InstallAction.Install)), primary: true);
            case InstallState.Installed when platform.Kind == InstallKind.Cli:
                return Chrome.Button(Strings.T("install.button.signIn"),
                    () => RunSheet.Show(owner, summary.Name, recipe, platform, InstallAction.SignIn, false, () => assistant.Run(summary.Id, InstallAction.SignIn)), primary: true);
            case InstallState.Installed:
                return Chrome.Button(Strings.T("settings.open", ("name", summary.Name)), () => openApp(), primary: true);
            default:
                return null;
        }
    }
}
