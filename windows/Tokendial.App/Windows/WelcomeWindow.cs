using System.Windows;
using System.Windows.Controls;
using Tokendial.Core.Providers;

namespace Tokendial.App.Windows;

/// <summary>First run: say what will be read, show which tools were found, and let the user choose before anything is polled.</summary>
public static class WelcomeWindow
{
    public static void Show(IReadOnlyList<ProviderSummary> detected, IReadOnlyList<ProviderSummary> absent, Action<IReadOnlyList<string>> connect, Action openSettings)
    {
        var page = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };
        page.Children.Add(Panel.Text.Make("Tokendial", 22, Panel.Theme.TextPrimary, FontWeights.SemiBold, display: true));
        var intro = Chrome.Body("A capsule at the top of the screen with one dial per coding assistant, showing how much of each usage limit you have spent, plus a notification when you cross a threshold, when a window is about to reset, when a limit is reached, and when an agent has been waiting on you.");
        intro.Margin = new Thickness(0, 10, 0, 0);
        page.Children.Add(intro);
        var how = Chrome.Body("Each provider is opt-in. Tokendial reads the sign-in its own tool already stores on this PC and calls that tool's usage endpoint; it never writes credentials and never sends them anywhere else.");
        how.Margin = new Thickness(0, 10, 0, 0);
        page.Children.Add(how);

        var chosen = new HashSet<string>(detected.Select(d => d.Id));
        var list = new StackPanel();
        foreach (var provider in detected)
            list.Children.Add(Chrome.Check(provider.Name, true, on => { if (on) chosen.Add(provider.Id); else chosen.Remove(provider.Id); }, provider.Account?.Summary ?? "Signed in"));
        page.Children.Add(Chrome.Section(detected.Count > 0 ? "Found on this PC" : "Nothing found yet",
            detected.Count > 0 ? "Connect the ones you want on the dial." : "Sign in to a supported tool and connect it later from Settings.", list));

        if (absent.Count > 0)
        {
            var names = Chrome.Body(string.Join(" · ", absent.Select(a => a.Name)));
            page.Children.Add(Chrome.Section("Not signed in", "Available once their tool is signed in.", names));
        }

        Window? window = null;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(Chrome.Button(detected.Count > 0 ? "Connect and start" : "Start", () => { connect(chosen.ToList()); window?.Close(); }, primary: true));
        buttons.Children.Add(Chrome.Button("Open settings", () => { connect(chosen.ToList()); window?.Close(); openSettings(); }));
        page.Children.Add(buttons);

        window = Chrome.Frame("Welcome to Tokendial", 520, 560, Chrome.Scroll(page));
        window.Show();
        window.Activate();
    }
}
