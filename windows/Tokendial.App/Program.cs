using Microsoft.Toolkit.Uwp.Notifications;
using Velopack;

namespace Tokendial.App;

public static class Program
{
    /// <summary>The Application User Model ID the installer stamps on the shortcuts; toasts and the taskbar group under it.</summary>
    public const string AppUserModelId = "Tokendial.Desktop";

    [STAThread]
    public static int Main(string[] args)
    {
        VelopackApp.Build().SetAppUserModelId(AppUserModelId).Run();
        using var instance = SingleInstance.Claim();
        if (instance is null)
        {
            SingleInstance.SignalRunning(args.Contains("--settings") ? SingleInstance.Signal.Settings : args.Contains("--test-alert") ? SingleInstance.Signal.TestAlert : SingleInstance.Signal.Show);
            return 0;
        }
        if (ToastNotificationManagerCompat.WasCurrentProcessToastActivated() && args.Contains("--quit-after-toast")) return 0;
        var app = new App(instance);
        return app.Run();
    }
}
