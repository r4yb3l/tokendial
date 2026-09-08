using System.Windows;
using System.Windows.Controls;
using Tokendial.App.Install;
using Tokendial.App.Panel;
using Tokendial.Core.I18n;
using Tokendial.Core.Install;
using Tokendial.Core.Providers;

namespace Tokendial.App.Windows;

/// <summary>First run: say what will be read, show which tools were found, offer to install or sign in the rest, and let the user choose before anything is polled.</summary>
public static class WelcomeWindow
{
    public static void Show(IReadOnlyList<ProviderSummary> detected, IReadOnlyList<ProviderSummary> absent, InstallAssistant assistant, Action<IReadOnlyList<string>> connect, Action openSettings)
    {
        Window? window = null;
        var page = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };
        page.Children.Add(Text.Make("Tokendial", 22, Theme.TextPrimary, FontWeights.SemiBold, display: true));
        var intro = Chrome.Body(Strings.T("welcome.intro"));
        intro.Margin = new Thickness(0, 10, 0, 0);
        page.Children.Add(intro);
        var how = Chrome.Body(Strings.T("welcome.optIn"));
        how.Margin = new Thickness(0, 10, 0, 0);
        page.Children.Add(how);

        var chosen = new HashSet<string>(detected.Select(d => d.Id));
        var list = new StackPanel();
        foreach (var provider in detected)
        {
            var row = new DockPanel { LastChildFill = true };
            var mark = new MarkView(provider.Id, 18) { Fill = Theme.TextPrimary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var check = Chrome.Check(provider.Name, true, on => { if (on) chosen.Add(provider.Id); else chosen.Remove(provider.Id); }, provider.Account?.Summary ?? Strings.T("status.signedIn"));
            DockPanel.SetDock(check, Dock.Left);
            row.Children.Add(check);
            row.Children.Add(mark);
            mark.HorizontalAlignment = HorizontalAlignment.Left;
            list.Children.Add(row);
        }
        page.Children.Add(Chrome.Section(detected.Count > 0 ? Strings.T("welcome.found") : Strings.T("welcome.nothing"),
            detected.Count > 0 ? Strings.T("welcome.foundHint") : Strings.T("welcome.nothingHint"), list));

        if (absent.Count > 0)
        {
            var rows = new StackPanel();
            void Rebuild()
            {
                rows.Children.Clear();
                foreach (var provider in absent) rows.Children.Add(AbsentRow(provider, assistant, window!));
            }
            assistant.Changed += Rebuild;
            page.Children.Add(Chrome.Section(Strings.T("welcome.notSignedIn"), Strings.T("welcome.notSignedInHint"), rows));
            window = Chrome.Frame(Strings.T("welcome.title"), 560, 620, Chrome.Scroll(page));
            window.Closed += (_, _) => assistant.Changed -= Rebuild;
            Rebuild();
        }
        else window = Chrome.Frame(Strings.T("welcome.title"), 520, 580, Chrome.Scroll(page));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(Chrome.Button(detected.Count > 0 ? Strings.T("welcome.connectStart") : Strings.T("welcome.start"), () => { connect(chosen.ToList()); window?.Close(); }, primary: true));
        buttons.Children.Add(Chrome.Button(Strings.T("welcome.openSettings"), () => { connect(chosen.ToList()); window?.Close(); openSettings(); }));
        page.Children.Add(buttons);

        window.FlowDirection = Strings.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        window.Show();
        window.Activate();
    }

    /// <summary>A tool that is not signed in yet: its mark, where it stands, and the Install or Sign in button when a recipe exists.</summary>
    private static UIElement AbsentRow(ProviderSummary provider, InstallAssistant assistant, Window owner)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mark = new MarkView(provider.Id, 18) { Fill = Theme.TextDisabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        grid.Children.Add(mark);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Chrome.Label(provider.Name));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (assistant.Recipe(provider.Id) is InstallRecipe recipe)
        {
            var state = assistant.State(provider);
            if (assistant.Waiting(provider.Id)) mark.Pulse(true, Theme.AmpleColor, Theme.TextDisabledColor, Theme.TextDisabled);
            var detail = Chrome.Body(InstallSteps.Detail(provider.Id, provider.Name, recipe, state, assistant));
            detail.FontSize = 11;
            text.Children.Add(detail);
            text.Children.Add(InstallSteps.Strip(state));
            if (InstallSteps.Action(owner, provider, recipe, state, assistant, () => false) is Button next)
            {
                next.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(next, 2);
                grid.Children.Add(next);
            }
        }
        else
        {
            var detail = Chrome.Body(provider.SignIn.Explanation);
            detail.FontSize = 11;
            text.Children.Add(detail);
        }
        return grid;
    }
}
