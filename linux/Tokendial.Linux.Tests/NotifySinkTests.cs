using Tokendial.Core.Alerts;
using Tokendial.Linux.Alerts;

namespace Tokendial.Linux.Tests;

/// <summary>
/// The answer the router falls back on: whether this alert, not the one before it, reached a daemon.
/// Delivery to a real notification daemon is a hardware check.
/// </summary>
public sealed class NotifySinkTests
{
    private static readonly Alert Sample = new(AlertKind.Threshold, "claude", "five_hour", 80, DateTimeOffset.UtcNow.AddMinutes(30));

    [Fact]
    public async Task NoSessionBusReportsTheAlertUndelivered()
    {
        using var sink = new NotifySink(_ => null, _ => null, address: () => null);
        var failed = new TaskCompletionSource();

        sink.Deliver(Sample, () => failed.TrySetResult());

        await failed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(sink.Available);
    }

    [Fact]
    public async Task UnreachableBusReportsTheAlertUndelivered()
    {
        var socket = Path.Combine(Path.GetTempPath(), $"tokendial-no-bus-{Guid.NewGuid():N}");
        using var sink = new NotifySink(_ => null, _ => null, address: () => $"unix:path={socket}");
        var failed = new TaskCompletionSource();

        sink.Deliver(Sample, () => failed.TrySetResult());

        await failed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(sink.Available);
    }
}
