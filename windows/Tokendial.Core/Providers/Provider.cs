using Tokendial.Core.Model;

namespace Tokendial.Core.Providers;

public enum UsageErrorKind
{
    /// <summary>No usable credential: the owning tool has to sign in.</summary>
    NeedsSignIn,
    /// <summary>The credential aged out; the owning tool refreshes it next time it runs. The last reading stays, dimmed.</summary>
    CredentialExpired,
    /// <summary>Told to slow down.</summary>
    RateLimited,
    /// <summary>The account is readable and meters nothing.</summary>
    NothingMetered,
    /// <summary>The endpoint answered with something we do not understand.</summary>
    BadResponse
}

public sealed class UsageError : Exception
{
    private UsageError(UsageErrorKind kind, string message) : base(message)
    {
        Kind = kind;
    }

    public UsageErrorKind Kind { get; }
    public int Status { get; private init; }
    public TimeSpan RetryAfter { get; private init; }

    public static UsageError NeedsSignIn() => new(UsageErrorKind.NeedsSignIn, "needs sign-in");
    public static UsageError CredentialExpired() => new(UsageErrorKind.CredentialExpired, "credential expired");
    public static UsageError RateLimited(TimeSpan retryAfter) => new(UsageErrorKind.RateLimited, $"rate limited for {retryAfter.TotalSeconds:F0}s") { RetryAfter = retryAfter };
    public static UsageError NothingMetered(string why) => new(UsageErrorKind.NothingMetered, why);
    public static UsageError BadResponse(int status) => new(UsageErrorKind.BadResponse, $"bad response ({status})") { Status = status };
}

/// <summary>Whose account a provider is reading, for the settings row.</summary>
public sealed record ProviderAccount(string? Label, string? Plan, string Source, Uri? ManageUrl)
{
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Label is not null) parts.Add(Label);
            if (Plan is not null) parts.Add(System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Plan.ToLowerInvariant()));
            parts.Add($"via {Source}");
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>What the user can do when a provider has no credential: open the owning app, or read a sentence.</summary>
public abstract record SignInRoute
{
    public sealed record OpenApp(string AppKey, string Name) : SignInRoute;
    public sealed record Guidance(string Text) : SignInRoute;

    public string Explanation => this switch
    {
        OpenApp app => $"Sign in with {app.Name} to read this account.",
        Guidance g => g.Text,
        _ => ""
    };
}

/// <summary>One assistant's usage source. Adapters read a credential another tool holds and call that tool's own endpoint.</summary>
public interface IUsageProvider
{
    string Id { get; }
    string DisplayName { get; }
    Task<ProviderReading> ReadAsync(CancellationToken cancellationToken = default);
    ProviderAccount? Account();
    SignInRoute SignIn { get; }
    void ForgetCredential() { }
}

/// <summary>Launches the desktop app that owns a credential, when it is installed.</summary>
public interface IAppLauncher
{
    bool IsInstalled(string appKey);
    bool Open(string appKey);
}

public sealed record ProviderSummary(string Id, string Name, ProviderAccount? Account, SignInRoute SignIn, bool Connected);
