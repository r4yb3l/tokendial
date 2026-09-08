using System.Windows;
using System.Windows.Controls;
using Tokendial.App.Panel;
using Tokendial.Core.I18n;
using Tokendial.Core.Providers;

namespace Tokendial.App.Windows;

/// <summary>First run: say what will be read, show which tools were found, and let the user choose before anything is polled.</summary>
public static class WelcomeWindow
{
    public static void Show(IReadOnlyList<ProviderSummary> detected, IReadOnlyList<ProviderSummary> absent, Action<IReadOnlyList<string>> connect, Action openSettings)
    {
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
            var names = Chrome.Body(string.Join(" · ", absent.Select(a => a.Name)));
            page.Children.Add(Chrome.Section(Strings.T("welcome.notSignedIn"), Strings.T("welcome.notSignedInHint"), names));
        }

        Window? window = null;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(Chrome.Button(detected.Count > 0 ? Strings.T("welcome.connectStart") : Strings.T("welcome.start"), () => { connect(chosen.ToList()); window?.Close(); }, primary: true));
        buttons.Children.Add(Chrome.Button(Strings.T("welcome.openSettings"), () => { connect(chosen.ToList()); window?.Close(); openSettings(); }));
        page.Children.Add(buttons);

        window = Chrome.Frame(Strings.T("welcome.title"), 520, 580, Chrome.Scroll(page));
        window.FlowDirection = Strings.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        window.Show();
        window.Activate();
    }
}
