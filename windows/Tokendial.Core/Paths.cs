namespace Tokendial.Core;

/// <summary>Where Tokendial keeps what belongs to the user: settings, readings, alert state, logs.</summary>
/// <remarks>
/// Deliberately not under <c>%LOCALAPPDATA%\Tokendial</c>. That directory belongs to the installer -
/// Velopack unpacks the application into it - and writing the user's data there had three consequences,
/// all of which shipped in v0.1.0: running the app once made a fresh machine look like it already had
/// Tokendial installed, so the installer refused; Repair then tried to delete the directory, which meant
/// deleting the user's settings and history, and failed outright while the app was running; and every
/// future update would have wiped the same files, because an update replaces that directory wholesale.
/// Roaming is the conventional home for user settings and is untouched by both.
/// </remarks>
public static class Paths
{
    /// <summary>The directory Tokendial owns.</summary>
    public static string Data { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tokendial");

    /// <summary>The installer's directory, which versions up to 0.1.0 wrote into.</summary>
    private static string Installed { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tokendial");

    public static string In(params string[] parts) =>
        Path.Combine(new[] { Data }.Concat(parts).ToArray());

    private static readonly string[] Carried = ["settings.json", "readings.json", "alerts.json", "backoff.json"];

    /// <summary>
    /// Carry across what an older version left in the installer's directory. Copies rather than moves, and
    /// never overwrites, so running an old and a new build on one machine cannot lose anything; the
    /// installer clears its own directory anyway.
    /// </summary>
    public static void CarryOverLegacyState()
    {
        try
        {
            if (!Directory.Exists(Installed)) return;
            Directory.CreateDirectory(Data);
            foreach (var name in Carried)
            {
                var from = Path.Combine(Installed, name);
                var to = Path.Combine(Data, name);
                if (File.Exists(from) && !File.Exists(to)) File.Copy(from, to);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
