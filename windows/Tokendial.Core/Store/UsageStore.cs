using Tokendial.Core.Diagnostics;
using Tokendial.Core.Model;
using Tokendial.Core.Providers;

namespace Tokendial.Core.Store;

/// <summary>
/// Polls every connected provider, keeps the last good reading, and turns each
/// failure into a status the dial can show honestly. Thread-agnostic: state
/// changes raise <see cref="Changed"/> on whatever thread made them.
/// </summary>
public sealed class UsageStore : IDisposable
{
    private readonly object gate = new();
    private readonly IReadOnlyList<IUsageProvider> providers;
    private readonly ReadingArchive archive;
    private readonly Cadence cadence;
    private readonly Func<DateTimeOffset> now;
    private readonly IAppLauncher launcher;
    private readonly Dictionary<string, Remembered> remembered;
    private readonly Dictionary<string, int> generation = new();
    private HashSet<string> disconnected;
    private HashSet<string> inFlight = new();
    private List<ProviderReading> readings;
    private DateTimeOffset? lastAttempt;
    private bool polling;
    private Timer? timer;

    public UsageStore(IReadOnlyList<IUsageProvider> providers, ReadingArchive? archive = null, IEnumerable<string>? disconnected = null,
        Cadence? cadence = null, Func<DateTimeOffset>? now = null, IAppLauncher? launcher = null)
    {
        this.providers = providers;
        this.archive = archive ?? new ReadingArchive();
        this.cadence = cadence ?? Cadence.Default;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.launcher = launcher ?? new NullLauncher();
        this.disconnected = new HashSet<string>(disconnected ?? []);
        remembered = this.archive.Load();
        if (remembered.Keys.Any(this.disconnected.Contains))
        {
            foreach (var id in this.disconnected) remembered.Remove(id);
            this.archive.Save(remembered);
        }
        readings = providers.Where(p => !this.disconnected.Contains(p.Id))
            .Select(p => remembered.TryGetValue(p.Id, out var r) ? r.Reading : Placeholder(p)).ToList();
    }

    public event Action? Changed;

    /// <summary>Whether an agent is working right now; usage cannot move while nothing runs.</summary>
    public Func<bool> IsBusy { get; set; } = () => false;

    public IReadOnlyList<ProviderReading> Readings { get { lock (gate) return readings.ToArray(); } }
    public IReadOnlySet<string> InFlight { get { lock (gate) return new HashSet<string>(inFlight); } }
    public IReadOnlySet<string> Disconnected { get { lock (gate) return new HashSet<string>(disconnected); } set => SetDisconnected(value); }
    public Task? CurrentPoll { get; private set; }

    public IReadOnlyList<ProviderSummary> Summaries
    {
        get
        {
            var off = Disconnected;
            return providers.Select(p => new ProviderSummary(p.Id, p.DisplayName, off.Contains(p.Id) ? null : p.Account(), p.SignIn, !off.Contains(p.Id))).ToList();
        }
    }

    public void Start()
    {
        PollNow();
        timer = new Timer(_ => Tick(), null, cadence.Active, cadence.Active);
    }

    public void Dispose() => timer?.Dispose();

    public void OnWake() => PollNow();

    private void Tick()
    {
        TimeSpan waited;
        lock (gate) waited = lastAttempt is DateTimeOffset last ? now() - last : TimeSpan.MaxValue;
        if (cadence.ShouldPoll(IsBusy(), waited)) PollNow();
    }

    public void PollNow()
    {
        lock (gate)
        {
            if (polling) return;
            polling = true;
            lastAttempt = now();
        }
        CurrentPoll = Task.Run(async () =>
        {
            try { await PollAsync().ConfigureAwait(false); }
            finally { lock (gate) polling = false; }
        });
    }

    public async Task PollAsync()
    {
        List<IUsageProvider> live;
        Dictionary<string, int> generations;
        lock (gate)
        {
            live = providers.Where(p => !disconnected.Contains(p.Id)).ToList();
            generations = new Dictionary<string, int>(generation);
            inFlight = new HashSet<string>(live.Select(p => p.Id));
        }
        Changed?.Invoke();
        var next = new List<ProviderReading>();
        try
        {
            foreach (var provider in live)
            {
                var reading = await ReadOne(provider, generations.GetValueOrDefault(provider.Id)).ConfigureAwait(false);
                if (reading is not null) next.Add(reading);
            }
        }
        finally
        {
            lock (gate)
            {
                inFlight = new HashSet<string>();
                readings = next.Where(r => IsCurrent(r.ProviderId, generations.GetValueOrDefault(r.ProviderId))).ToList();
            }
            Changed?.Invoke();
        }
    }

    /// <summary>Refetch one provider without spending the others' rate-limit budget.</summary>
    public Task Poll(string providerId)
    {
        var provider = providers.FirstOrDefault(p => p.Id == providerId);
        int gen;
        lock (gate)
        {
            if (provider is null || disconnected.Contains(providerId) || inFlight.Contains(providerId)) return Task.CompletedTask;
            inFlight = new HashSet<string>(inFlight) { providerId };
            gen = generation.GetValueOrDefault(providerId);
        }
        Changed?.Invoke();
        return Task.Run(async () =>
        {
            try
            {
                var reading = await ReadOne(provider, gen).ConfigureAwait(false);
                if (reading is not null)
                {
                    lock (gate)
                    {
                        var index = readings.FindIndex(r => r.ProviderId == providerId);
                        if (index >= 0) readings[index] = reading;
                        lastAttempt = now();
                    }
                    Changed?.Invoke();
                }
                await Task.Delay(350).ConfigureAwait(false);
            }
            finally
            {
                lock (gate) { var set = new HashSet<string>(inFlight); set.Remove(providerId); inFlight = set; }
                Changed?.Invoke();
            }
        });
    }

    public void Disconnect(string providerId)
    {
        var set = Disconnected.ToHashSet();
        set.Add(providerId);
        SetDisconnected(set);
    }

    public void Connect(string providerId)
    {
        var set = Disconnected.ToHashSet();
        if (!set.Remove(providerId)) return;
        SetDisconnected(set);
        var provider = providers.FirstOrDefault(p => p.Id == providerId);
        if (provider is not null && provider.Account() is null) OpenSource(providerId);
    }

    public bool OpenSource(string providerId)
    {
        var provider = providers.FirstOrDefault(p => p.Id == providerId);
        return provider?.SignIn is SignInRoute.OpenApp app && launcher.IsInstalled(app.AppKey) && launcher.Open(app.AppKey);
    }

    public void Reauthorize(string providerId)
    {
        providers.FirstOrDefault(p => p.Id == providerId)?.ForgetCredential();
        _ = Poll(providerId);
    }

    private void SetDisconnected(IReadOnlySet<string> value)
    {
        lock (gate)
        {
            if (disconnected.SetEquals(value)) return;
            var changed = new HashSet<string>(disconnected);
            changed.SymmetricExceptWith(value);
            foreach (var id in changed) generation[id] = generation.GetValueOrDefault(id) + 1;
            disconnected = new HashSet<string>(value);
            readings = readings.Where(r => !disconnected.Contains(r.ProviderId)).ToList();
            foreach (var id in disconnected)
            {
                if (remembered.Remove(id)) archive.Forget(id);
            }
            foreach (var id in changed.Where(id => !disconnected.Contains(id)))
            {
                var provider = providers.FirstOrDefault(p => p.Id == id);
                if (provider is not null && readings.All(r => r.ProviderId != id)) readings.Add(Placeholder(provider));
            }
            readings = providers.Where(p => readings.Any(r => r.ProviderId == p.Id)).Select(p => readings.First(r => r.ProviderId == p.Id)).ToList();
        }
        Changed?.Invoke();
        PollNow();
    }

    private bool IsCurrent(string providerId, int gen)
    {
        lock (gate) return !disconnected.Contains(providerId) && generation.GetValueOrDefault(providerId) == gen;
    }

    private async Task<ProviderReading?> ReadOne(IUsageProvider provider, int gen)
    {
        if (!IsCurrent(provider.Id, gen)) return null;
        try
        {
            var fresh = await provider.ReadAsync().ConfigureAwait(false);
            if (!IsCurrent(provider.Id, gen)) return null;
            lock (gate)
            {
                remembered[provider.Id] = new Remembered(fresh, now());
                archive.Save(remembered);
            }
            Log.Usage.Debug($"{provider.Id}: {fresh.Windows.Count} window(s)");
            return fresh;
        }
        catch (Exception error)
        {
            if (!IsCurrent(provider.Id, gen)) return null;
            Log.Usage.Error($"{provider.Id}: {error.Message}");
            return Degrade(provider, error);
        }
    }

    /// <summary>Never invents a number: re-show the last good reading, dimmed when old, or a dial with no reading.</summary>
    private ProviderReading Degrade(IUsageProvider provider, Exception error)
    {
        var status = StatusFor(error);
        lock (gate)
        {
            if (Supersedes(status))
            {
                if (remembered.Remove(provider.Id)) archive.Save(remembered);
                return Placeholder(provider) with { Status = status };
            }
            if (!remembered.TryGetValue(provider.Id, out var previous)) return Placeholder(provider) with { Status = status };
            var age = now() - previous.TakenAt;
            return previous.Reading with { Status = age > cadence.StaleAfter ? new ReadingStatus.Stale(previous.TakenAt) : previous.Reading.Status };
        }
    }

    /// <summary>A sign-out or an unmetered plan makes the old number untrue, not merely old.</summary>
    public static bool Supersedes(ReadingStatus status) => status is ReadingStatus.NeedsSignIn or ReadingStatus.Unsupported;

    public static ReadingStatus StatusFor(Exception error) => error switch
    {
        UsageError { Kind: UsageErrorKind.NeedsSignIn } => ReadingStatus.SignIn,
        UsageError { Kind: UsageErrorKind.CredentialExpired } => new ReadingStatus.Stale(DateTimeOffset.UtcNow),
        UsageError { Kind: UsageErrorKind.RateLimited } => new ReadingStatus.Stale(DateTimeOffset.UtcNow),
        UsageError { Kind: UsageErrorKind.NothingMetered } e => new ReadingStatus.Unsupported(e.Message),
        UsageError { Kind: UsageErrorKind.BadResponse } e => new ReadingStatus.Failed($"HTTP {e.Status}"),
        _ => new ReadingStatus.Failed(error.Message)
    };

    private static ProviderReading Placeholder(IUsageProvider p) =>
        new(p.Id, p.DisplayName, Fidelity.Official, new ReadingStatus.Stale(DateTimeOffset.MinValue), []);

    private sealed class NullLauncher : IAppLauncher
    {
        public bool IsInstalled(string appKey) => false;
        public bool Open(string appKey) => false;
    }
}
