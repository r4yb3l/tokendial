using Tokendial.Core.Model;
using Tokendial.Core.Providers;
using Tokendial.Core.Store;

namespace Tokendial.Tests;

public class UsageStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tokendial-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private sealed class FakeProvider(string id, Func<Task<ProviderReading>> read) : IUsageProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public Task<ProviderReading> ReadAsync(CancellationToken cancellationToken = default) => read();
        public ProviderAccount? Account() => new(id, null, "test", null);
        public SignInRoute SignIn => new SignInRoute.OpenApp(id, id);
    }

    private static ProviderReading Live(string id, double used) =>
        new(id, id, Fidelity.Official, ReadingStatus.LiveNow, [new UsageWindow("session", "Current session", used)], "session");

    /// <summary>
    /// Connecting a provider while a poll runs used to lose it twice over: the running poll replaced the
    /// readings with the list it had started from, dropping the new dial, and the poll the connection asked
    /// for was refused because one was already running.
    /// </summary>
    [Fact]
    public async Task AProviderConnectedDuringAPollKeepsItsDialAndIsReadAfterIt()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new FakeProvider("claude", async () =>
        {
            started.TrySetResult();
            await release.Task;
            return Live("claude", 0.4);
        });
        var added = new FakeProvider("codex", () => Task.FromResult(Live("codex", 0.2)));
        using var store = new UsageStore([slow, added], new ReadingArchive(directory), disconnected: ["codex"]);

        store.PollNow();
        var first = store.CurrentPoll!;
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        store.Connect("codex");
        release.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains(store.Readings, r => r.ProviderId == "codex");
        Assert.NotSame(first, store.CurrentPoll);
        await store.CurrentPoll!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(["claude", "codex"], store.Readings.Select(r => r.ProviderId));
        Assert.All(store.Readings, r => Assert.IsType<ReadingStatus.Live>(r.Status));
    }
}
