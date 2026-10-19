using System.Net;
using Microsoft.Data.Sqlite;
using Tokendial.Core.Model;
using Tokendial.Core.Providers;
using Tokendial.Core.Providers.OpenCode;
using Tokendial.Core.Store;

namespace Tokendial.Tests;

/// <summary>
/// opencode v2 keeps logins in opencode.db and stopped writing auth.json, so a replaced key never reached the
/// dial. And the usage endpoint answers api_key_expired for Go keys opencode itself runs on, while it accepts
/// the OpenCode Console key: reading only the Go key told a working user their key had expired.
/// </summary>
public class OpenCodeTests : IDisposable
{
    private const string ExpiredBody = """{"error":{"type":"authentication_error","code":"api_key_expired","message":"This API key has expired. Create a new API key in OpenCode Console and replace it in your client or secret store. Signing in again will not renew this key."}}""";
    private const string UnauthorizedBody = """{"type":"error","error":{"type":"AuthError","message":"Unauthorized"}}""";
    private const string UsageBody = """{"usage":{"rolling":{"percent":12,"resetsAt":"2026-10-18T12:00:00.000Z"}}}""";

    private readonly string root = Path.Combine(Path.GetTempPath(), "tokendial-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "opencode.db");
    private string AuthFile => Path.Combine(root, "auth.json");

    public OpenCodeTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    /// <summary>The table as opencode v2.0.32 creates it, with integration rows as (id, active, created, updated, key).</summary>
    private void Store(params (string Integration, int Active, long Created, long Updated, string Key)[] rows)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
        connection.Open();
        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS `credential` (`id` text PRIMARY KEY, `integration_id` text, `label` text NOT NULL, `value` text NOT NULL,
              `connector_id` text, `method_id` text, `active` integer, `time_created` integer NOT NULL, `time_updated` integer NOT NULL)
            """;
        create.ExecuteNonQuery();
        foreach (var (integration, active, created, updated, key) in rows)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO credential VALUES ($id, $integration, 'default', $value, NULL, NULL, $active, $created, $updated)";
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$integration", integration);
            insert.Parameters.AddWithValue("$value", $$"""{"type":"key","key":"{{key}}"}""");
            insert.Parameters.AddWithValue("$active", active);
            insert.Parameters.AddWithValue("$created", created);
            insert.Parameters.AddWithValue("$updated", updated);
            insert.ExecuteNonQuery();
        }
    }

    private void Auth(string key) => File.WriteAllText(AuthFile, $$$"""{"opencode-go":{"type":"api","key":"{{{key}}}"},"openai":{"type":"api","key":"not-ours"}}""");

    private string? Read() => OpenCodeCredential.Read(AuthFile, Database);

    [Fact]
    public void AGoRowInTheDatabaseIsTheKey()
    {
        Store(("opencode-go", 1, 1792337464370, 1792337464370, "sk-db"));
        Assert.Equal("sk-db", Read());
    }

    [Fact]
    public void TheDatabaseWinsOverAnAuthFileV1LeftBehind()
    {
        Store(("opencode-go", 1, 1792337464370, 1792337464370, "sk-db"));
        Auth("sk-v1-stale");
        Assert.Equal("sk-db", Read());
    }

    /// <summary>The newest active Go row wins over a replaced one, and other vendors' rows are never claimed.</summary>
    [Fact]
    public void TheNewestActiveGoRowWinsAndOtherVendorsAreIgnored()
    {
        Store(("opencode-go", 0, 1790305105303, 1792337464370, "sk-replaced"),
              ("opencode-go", 1, 1792337464370, 1792337464370, "sk-current"),
              ("openai", 1, 1792338050439, 1792338050439, "not-ours"));
        Assert.Equal("sk-current", Read());
    }

    /// <summary>
    /// The shape on the reporting machine: a legacy Go key, a new Go key, and the Console key. Usage answered 200
    /// only to the Console key; both Go keys got api_key_expired while opencode ran on them.
    /// </summary>
    [Fact]
    public void TheConsoleKeyWinsOverEveryGoKey()
    {
        Store(("opencode-go", 0, 1790305105303, 1792337464370, "sk-legacy"),
              ("opencode-go", 1, 1792337464370, 1792337464370, "sk-go"),
              ("opencode", 1, 1792338050439, 1792338050439, "sk-console"));
        Auth("sk-v1-stale");
        Assert.Equal("sk-console", Read());
    }

    [Fact]
    public void AnInactiveConsoleKeyIsNotUsed()
    {
        Store(("opencode", 0, 1, 1, "sk-console-old"), ("opencode-go", 1, 2, 2, "sk-go"));
        Assert.Equal("sk-go", Read());
    }

    [Fact]
    public void InactiveRowsOnlyFallBackToTheAuthFile()
    {
        Store(("opencode-go", 0, 1790305105303, 1792337464370, "sk-replaced"));
        Auth("sk-v1");
        Assert.Equal("sk-v1", Read());
    }

    [Fact]
    public void WithoutADatabaseTheAuthFileIsStillRead()
    {
        Auth("sk-v1");
        Assert.Equal("sk-v1", Read());
    }

    [Fact]
    public void NeitherSourceIsNoKey() => Assert.Null(Read());

    [Fact]
    public void ADatabaseWithoutTheTableFallsBackInsteadOfThrowing()
    {
        File.WriteAllBytes(Database, []);
        Auth("sk-v1");
        Assert.Equal("sk-v1", Read());
    }

    /// <summary>Mid-write, auth.json may hold the key opencode just replaced: wait for the store, never fall back.</summary>
    [Fact]
    public async Task ABusyStoreIsTransientAndNeverFallsBackToTheAuthFile()
    {
        Store(("opencode-go", 1, 1, 1, "sk-db"));
        Auth("sk-v1-stale");
        using var writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
        writer.Open();
        using (var exclusive = writer.CreateCommand()) { exclusive.CommandText = "BEGIN EXCLUSIVE"; exclusive.ExecuteNonQuery(); }
        Assert.Throws<Sqlite.BusyException>(Read);
        using var provider = Provider(HttpStatusCode.OK, UsageBody);
        var error = await Assert.ThrowsAsync<UsageError>(() => provider.ReadAsync());
        Assert.Equal(UsageErrorKind.CredentialExpired, error.Kind);
        Assert.NotNull(provider.Account());
    }

    [Theory]
    [InlineData(ExpiredBody, true)]
    [InlineData(UnauthorizedBody, false)]
    [InlineData("not json", false)]
    [InlineData("", false)]
    public void OnlyTheApiKeyExpiredCodeReadsAsARefusedGoKey(string body, bool refused) =>
        Assert.Equal(refused, OpenCodeUsage.RefusesGoKey(body));

    private OpenCodeProvider Provider(HttpStatusCode status, string body) =>
        new(new Answering(status, body), new ReadingArchive(Path.Combine(root, "archive")), AuthFile, database: Database);

    /// <summary>The key works in opencode, so the card must point at the Console, never say the key expired.</summary>
    [Fact]
    public async Task ARefusedGoKeyAsksForTheConsoleAndNeverSaysExpired()
    {
        Store(("opencode-go", 1, 1, 1, "sk-go"));
        using var provider = Provider(HttpStatusCode.Unauthorized, ExpiredBody);
        var error = await Assert.ThrowsAsync<UsageError>(() => provider.ReadAsync());
        Assert.Equal(UsageErrorKind.Rejected, error.Kind);
        var why = Assert.IsType<ReadingStatus.Failed>(UsageStore.StatusFor(error)).Why;
        Assert.Contains("OpenCode Console", why);
        Assert.DoesNotContain("expired", why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheConsoleKeyIsTheOneSent()
    {
        Store(("opencode-go", 1, 1, 1, "sk-go"), ("opencode", 1, 2, 2, "sk-console"));
        var handler = new Answering(HttpStatusCode.OK, UsageBody);
        using var provider = new OpenCodeProvider(handler, new ReadingArchive(Path.Combine(root, "archive")), AuthFile, database: Database);
        await provider.ReadAsync();
        Assert.Equal("Bearer sk-console", handler.Authorization);
    }

    [Fact]
    public async Task AKeyThatWasNeverValidStillNeedsSignIn()
    {
        Store(("opencode-go", 1, 1, 1, "sk-bogus"));
        using var provider = Provider(HttpStatusCode.Unauthorized, UnauthorizedBody);
        var error = await Assert.ThrowsAsync<UsageError>(() => provider.ReadAsync());
        Assert.Equal(UsageErrorKind.NeedsSignIn, error.Kind);
    }

    [Fact]
    public async Task TheDatabaseKeyIsTheOneSent()
    {
        Store(("opencode-go", 1, 1, 1, "sk-db"));
        Auth("sk-v1-stale");
        var handler = new Answering(HttpStatusCode.OK, UsageBody);
        using var provider = new OpenCodeProvider(handler, new ReadingArchive(Path.Combine(root, "archive")), AuthFile, database: Database);
        await provider.ReadAsync();
        Assert.Equal("Bearer sk-db", handler.Authorization);
    }

    /// <summary>A rejected key will not come back, so the last number must not keep showing over the reason.</summary>
    [Fact]
    public async Task ARejectedKeyDropsTheLastReadingAndShowsWhy()
    {
        var calls = 0;
        var provider = new Scripted(() => calls++ == 0
            ? Task.FromResult(new ProviderReading("opencode", "OpenCode", Fidelity.Official, ReadingStatus.LiveNow, [new UsageWindow("rolling", "5h limit", 0.3)], "rolling"))
            : throw UsageError.Rejected("usage wants the Console"));
        using var store = new UsageStore([provider], new ReadingArchive(Path.Combine(root, "archive")));
        await store.PollAsync();
        await store.PollAsync();
        var reading = Assert.Single(store.Readings);
        Assert.Equal("usage wants the Console", Assert.IsType<ReadingStatus.Failed>(reading.Status).Why);
        Assert.Empty(reading.Windows);
    }

    private sealed class Scripted(Func<Task<ProviderReading>> read) : IUsageProvider
    {
        public string Id => "opencode";
        public string DisplayName => "OpenCode";
        public Task<ProviderReading> ReadAsync(CancellationToken cancellationToken = default) => read();
        public ProviderAccount? Account() => new(null, "Go", "OpenCode", null);
        public SignInRoute SignIn => new SignInRoute.Guidance("signin.opencode");
    }

    private sealed class Answering(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request, Content = new StringContent(body) });
        }
    }
}
