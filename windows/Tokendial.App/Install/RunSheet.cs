using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tokendial.App.Panel;
using Tokendial.App.Windows;
using Tokendial.Core.I18n;
using Tokendial.Core.Install;

namespace Tokendial.App.Install;

/// <summary>The command shown before it runs: what will be executed, whose it is, where it is documented, and the Run button.</summary>
public static class RunSheet
{
    private const string StoreAppInstaller = "ms-windows-store://pdp/?productid=9NBLGGH4NNS1";

    public static void Show(Window owner, string providerName, InstallRecipe recipe, PlatformRecipe platform, InstallAction action, bool wingetMissing, Action run)
    {
        var installing = action == InstallAction.Install;
        var command = installing ? platform.Install : platform.SignIn?.Command ?? "";
        var page = new StackPanel { Margin = new Thickness(22, 20, 22, 20) };
        page.Children.Add(Chrome.Body(installing ? Strings.T("install.sheet.intro", ("vendor", recipe.Vendor)) : Strings.T("install.sheet.signInIntro", ("name", providerName))));

        var box = new TextBox
        {
            Text = command,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            Background = Chrome.Control,
            Foreground = Theme.TextPrimary,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 12, 0, 0),
            FlowDirection = FlowDirection.LeftToRight
        };
        page.Children.Add(new Border { Background = Chrome.Control, CornerRadius = new CornerRadius(6), Child = box });

        var hintKey = platform.Kind == InstallKind.App ? "install.hint.app" : platform.SignIn?.Hint;
        if (hintKey is not null)
        {
            var hint = Chrome.Body(Strings.T(hintKey, ("name", providerName)));
            hint.Margin = new Thickness(0, 12, 0, 0);
            page.Children.Add(hint);
        }
        if (installing && platform.NeedsWinget)
        {
            if (wingetMissing)
            {
                var missing = Chrome.Body(Strings.T("install.requires.winget"));
                missing.Foreground = Theme.Critical;
                missing.Margin = new Thickness(0, 12, 0, 0);
                page.Children.Add(missing);
                var store = Chrome.Button("Microsoft Store", () => SettingsWindow.Open(StoreAppInstaller));
                store.Margin = new Thickness(0, 8, 8, 0);
                store.HorizontalAlignment = HorizontalAlignment.Left;
                page.Children.Add(store);
            }
            var uac = Chrome.Body(Strings.T("install.sheet.uac"));
            uac.FontSize = 11;
            uac.Margin = new Thickness(0, 10, 0, 0);
            page.Children.Add(uac);
        }

        Window? window = null;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(Chrome.Button(Strings.T("install.button.run"), () => { window?.Close(); run(); }, primary: true));
        buttons.Children.Add(Chrome.Button(Strings.T("install.button.copy"), () => Clipboard.SetText(command)));
        buttons.Children.Add(Chrome.Button(Strings.T("install.sheet.docs"), () => SettingsWindow.Open(recipe.DocsUrl)));
        if (installing && platform.DownloadUrl is string download)
            buttons.Children.Add(Chrome.Button(Strings.T("install.button.download"), () => SettingsWindow.Open(download)));
        page.Children.Add(buttons);

        var title = installing ? Strings.T("install.sheet.title", ("name", providerName)) : Strings.T("install.sheet.signInTitle", ("name", providerName));
        window = Chrome.Frame(title, 600, 380, page);
        window.MinHeight = 200;
        window.SizeToContent = SizeToContent.Height;
        window.Owner = owner;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.ResizeMode = ResizeMode.NoResize;
        window.FlowDirection = Strings.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        window.ShowDialog();
    }
}
