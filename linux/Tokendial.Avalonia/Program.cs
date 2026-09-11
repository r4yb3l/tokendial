using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Tokendial.Linux.Panel;

namespace Tokendial.Linux;

public static class Program
{
    public static int Main(string[] args) =>
        AppBuilder.Configure<TokendialApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}

public sealed class TokendialApp : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The dock is the app's only window today, so closing it would end the process; the tray and the
            // settings window arrive next, and this becomes OnExplicitShutdown then.
            desktop.MainWindow = new PanelWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
