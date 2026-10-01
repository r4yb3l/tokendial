using Tokendial.Core.Diagnostics;

namespace Tokendial.Linux;

/// <summary>
/// One Tokendial per session.
/// </summary>
/// <remarks>
/// Installing gives the user two ways to start the same application - the menu entry and whatever file they
/// downloaded - and clicking both is the normal thing to do. Without a claim that is two docks, two trays and
/// two pollers reading the same accounts. .NET implements <see cref="FileShare"/> on Unix with an advisory
/// lock on the open file, so holding the file open for the life of the process is the whole mechanism, and
/// the kernel releases it however the process ends, including a kill.
/// <para>
/// The lock lives in the runtime directory, which the XDG base directory specification defines as per-user,
/// per-session and cleared on logout - exactly the lifetime of "is one already running".
/// </para>
/// </remarks>
public static class SingleInstance
{
    private static FileStream? held;

    /// <summary>False when another process already holds the claim, in which case this one has nothing to do.</summary>
    public static bool Claim()
    {
        var path = LockPath(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"), Path.GetTempPath(), Environment.UserName);
        try
        {
            held = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            Log.Ui.Info("another Tokendial is already running");
            return false;
        }
        catch (UnauthorizedAccessException error)
        {
            Log.Ui.Error($"cannot open the instance lock {path}, so this copy is quitting: {error.Message}");
            return false;
        }
    }

    /// <summary>
    /// The runtime directory is the user's own. The temporary directory it falls back to is shared by every
    /// account on the machine, so there the name carries the user: a lock left by another account cannot be
    /// opened from this one, and every start here would quit as though Tokendial were already running.
    /// </summary>
    internal static string LockPath(string? runtime, string temp, string user) =>
        !string.IsNullOrWhiteSpace(runtime) && Path.IsPathRooted(runtime)
            ? Path.Combine(runtime, "tokendial.lock")
            : Path.Combine(temp, $"tokendial-{user}.lock");
}
