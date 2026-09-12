using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Tokendial.Core.I18n;

namespace Tokendial.Linux.Windows;

/// <summary>A startup explanation that creates no store, installer, tray or provider readers.</summary>
public sealed class SessionNotice : Application
{
    public override void Initialize() => Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new Window
            {
                Title = Strings.T("app.name"),
                Width = 480,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Chrome.WindowBackground,
                FlowDirection = Strings.RightToLeft ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight
            };
            window.Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 18,
                Children =
                {
                    Chrome.Heading(Strings.T("linux.session.title")),
                    Chrome.Body(Strings.T("linux.session.wayland")),
                    Chrome.Button(Strings.T("dialog.ok"), () => desktop.Shutdown(1), primary: true)
                }
            };
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
