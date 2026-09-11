using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Tokendial.Core.I18n;
using Tokendial.Core.Install;
using Tokendial.Linux.Windows;

namespace Tokendial.Linux.Install;

/// <summary>
/// The command shown before it runs: what will be executed, whose it is, where it is documented, and the Run
/// button.
/// </summary>
/// <remarks>
/// Nothing is ever run without this. The line is the vendor's own, from the spec, shown in full and
/// selectable, and the button that runs it is the user's to press - which is also why Copy sits beside it,
/// for someone who would rather paste it into their own terminal and watch it there.
/// </remarks>
public static class RunSheet
{
    public static void Show(Window owner, string providerName, InstallRecipe recipe, PlatformRecipe platform,
                            InstallAction action, bool hasTerminal, Action run)
    {
        var installing = action == InstallAction.Install;
        var command = installing ? platform.Install : platform.SignIn?.Command ?? "";

        var page = new StackPanel { Margin = new Thickness(22, 20, 22, 20) };
        page.Children.Add(Chrome.Body(installing
            ? Strings.T("install.sheet.intro", ("vendor", recipe.Vendor))
            : Strings.T("install.sheet.signInIntro", ("name", providerName))));

        if (command.Length > 0) page.Children.Add(Command(command));

        var hintKey = platform.Kind == InstallKind.App ? "install.hint.app" : platform.SignIn?.Hint;
        if (hintKey is not null)
        {
            var hint = Chrome.Body(Strings.T(hintKey, ("name", providerName)));
            hint.Margin = new Thickness(0, 12, 0, 0);
            page.Children.Add(hint);
        }

        // Only when it is actually absent: telling someone who has Node that they need Node reads as a
        // warning about their own machine that is not true.
        if (installing && platform.Requires.Any(Missing))
        {
            var needs = Chrome.Body(Strings.T("install.requires.node"));
            needs.FontSize = 11;
            needs.Foreground = Panel.Theme.Critical;
            needs.Margin = new Thickness(0, 10, 0, 0);
            page.Children.Add(needs);
        }

        // The installer may ask for a password in its own terminal; Tokendial neither sees it nor asks for one.
        if (installing)
        {
            var sudo = Chrome.Body(Strings.T("install.sheet.sudo"));
            sudo.FontSize = 11;
            sudo.Margin = new Thickness(0, 10, 0, 0);
            page.Children.Add(sudo);
        }

        Window? window = null;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 18, 0, 0)
        };
        if (hasTerminal && command.Length > 0)
            buttons.Children.Add(Chrome.Button(Strings.T("install.button.run"), () => { window?.Close(); run(); }, primary: true));
        if (command.Length > 0)
            buttons.Children.Add(Chrome.Button(Strings.T("install.button.copy"), () => Copy(window, command)));
        buttons.Children.Add(Chrome.Button(Strings.T("install.sheet.docs"), () => Open(recipe.DocsUrl)));
        if (installing && platform.DownloadUrl is string download)
            buttons.Children.Add(Chrome.Button(Strings.T("install.button.download"), () => Open(download), primary: command.Length == 0));
        buttons.Children.Add(Chrome.Button(Strings.T("install.button.cancel"), () => window?.Close()));
        page.Children.Add(buttons);

        window = new Window
        {
            Title = installing
                ? Strings.T("install.sheet.title", ("name", providerName))
                : Strings.T("install.sheet.signInTitle", ("name", providerName)),
            Width = 600,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            Background = Chrome.WindowBackground,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            FlowDirection = Strings.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            Content = page
        };
        window.ShowDialog(owner);
    }

    /// <summary>
    /// The line itself, selectable and read-only, always left to right: a shell command is not prose and does
    /// not reorder in Arabic.
    /// </summary>
    private static Control Command(string command)
    {
        var box = new TextBox
        {
            Text = command,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = Chrome.Mono,
            FontSize = 12,
            Background = Brushes.Transparent,
            Foreground = Chrome.Slate100,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 10, 12, 10),
            FlowDirection = FlowDirection.LeftToRight
        };
        return new Border
        {
            Background = Chrome.Well,
            BorderBrush = Chrome.EdgeSoft,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 12, 0, 0),
            Child = box
        };
    }

    private static bool Missing(string manager) => !new ToolLocator().IsInstalled(new Detect([manager], []));

    private static void Copy(Window? window, string text)
    {
        if (window is null) return;
        _ = TopLevel.GetTopLevel(window)?.Clipboard?.SetTextAsync(text);
    }

    private static void Open(string target)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xdg-open", target) { UseShellExecute = false }); }
        catch (Exception) { }
    }
}
