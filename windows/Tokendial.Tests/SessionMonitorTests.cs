using Tokendial.Core.Sessions;

namespace Tokendial.Tests;

public class SessionMonitorTests
{
    private sealed class Recording : PolledMonitor
    {
        public Recording() : base("test", TimeSpan.FromHours(1)) { }

        public bool Disposed { get; private set; }

        protected override IReadOnlyList<AgentSession> Read() => [];

        public override void Dispose()
        {
            Disposed = true;
            base.Dispose();
        }
    }

    /// <summary>
    /// The hub only knows its monitors as IActivityMonitor. Claude's monitor hid Dispose with <c>new</c>, so
    /// disposing it there ran the base timer's cleanup alone and left its file watcher and debounce timer alive.
    /// </summary>
    [Fact]
    public void DisposingTheHubReachesEachMonitorsOwnCleanup()
    {
        var monitor = new Recording();
        new ActivityHub([monitor]).Dispose();
        Assert.True(monitor.Disposed);
    }
}
