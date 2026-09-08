namespace Tokendial.App;

/// <summary>One Tokendial per session. A second launch wakes the first and quits.</summary>
public sealed class SingleInstance : IDisposable
{
    public enum Signal { Show, Settings, TestAlert }

    private const string MutexName = @"Local\Tokendial.Desktop.Instance";
    private const string ShowEvent = @"Local\Tokendial.Desktop.Show";
    private const string SettingsEvent = @"Local\Tokendial.Desktop.Settings";
    private const string TestAlertEvent = @"Local\Tokendial.Desktop.TestAlert";

    private readonly Mutex mutex;
    private readonly EventWaitHandle show = new(false, EventResetMode.AutoReset, ShowEvent);
    private readonly EventWaitHandle settings = new(false, EventResetMode.AutoReset, SettingsEvent);
    private readonly EventWaitHandle testAlert = new(false, EventResetMode.AutoReset, TestAlertEvent);
    private readonly CancellationTokenSource stop = new();

    private SingleInstance(Mutex mutex) => this.mutex = mutex;

    public static SingleInstance? Claim()
    {
        var mutex = new Mutex(true, MutexName, out var created);
        if (created) return new SingleInstance(mutex);
        mutex.Dispose();
        return null;
    }

    public static void SignalRunning(Signal signal)
    {
        try
        {
            using var handle = EventWaitHandle.OpenExisting(signal switch { Signal.Settings => SettingsEvent, Signal.TestAlert => TestAlertEvent, _ => ShowEvent });
            handle.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { }
    }

    /// <summary>Raised on a pool thread; the caller marshals to its dispatcher.</summary>
    public void Listen(Action<Signal> onSignal)
    {
        var token = stop.Token;
        new Thread(() =>
        {
            var handles = new WaitHandle[] { show, settings, testAlert, token.WaitHandle };
            while (!token.IsCancellationRequested)
            {
                var index = WaitHandle.WaitAny(handles);
                if (index == 0) onSignal(Signal.Show);
                else if (index == 1) onSignal(Signal.Settings);
                else if (index == 2) onSignal(Signal.TestAlert);
            }
        }) { IsBackground = true, Name = "single-instance" }.Start();
    }

    public void Dispose()
    {
        stop.Cancel();
        show.Dispose();
        settings.Dispose();
        testAlert.Dispose();
        try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
        mutex.Dispose();
    }
}
