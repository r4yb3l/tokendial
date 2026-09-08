using Microsoft.Toolkit.Uwp.Notifications;

namespace Tokendial.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
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
