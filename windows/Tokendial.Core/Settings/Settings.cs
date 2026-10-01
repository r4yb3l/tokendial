using System.Text.Json;
using System.Text.Json.Serialization;
using Tokendial.Core.Alerts;

namespace Tokendial.Core.Settings;

public enum PanelMode
{
    ExpandOnHover,
    AlwaysExpanded,
    Hidden
}

public enum DockEdge
{
    Top,
    Bottom,
    Left,
    Right
}

public enum Appearance
{
    System,
    Dark,
    Light
}

public enum AlertDelivery
{
    TokendialBanners,
    WindowsToasts,
    WindowsThenBanners
}

/// <summary>Everything the user can change. Saved as one JSON file; unknown fields survive a round trip through an older build only as far as System.Text.Json allows, so the schema number guards migrations.</summary>
public sealed class Settings
{
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;
    public PanelMode Panel { get; set; } = PanelMode.ExpandOnHover;
    public DockEdge Edge { get; set; } = DockEdge.Top;

    /// <summary>
    /// Which monitor the dock lives on, by the platform's own name for it - an XRandR output on Linux,
    /// a device path on Windows, the localized name on macOS. Null follows the primary monitor, which is
    /// what every installation keeps on upgrade. A name that is no longer attached falls back to the
    /// primary one and is honoured again as soon as that display returns.
    /// </summary>
    public string? Display { get; set; }
    public HashSet<string> Disconnected { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Provider ids this install has seen. A provider added by an update joins connected only when its tool is signed in.</summary>
    public HashSet<string> Known { get; set; } = new(StringComparer.Ordinal);
    public bool LaunchAtLogin { get; set; }
    /// <summary>One request a day to GitHub Releases for a newer version; the download waits for the user to restart.</summary>
    public bool CheckForUpdates { get; set; } = true;
    public bool FirstRunDone { get; set; }
    public List<int> Thresholds { get; set; } = [50, 80, 95];
    public bool AlertThresholds { get; set; } = true;
    public bool AlertResetSoon { get; set; } = true;
    public bool AlertWaiting { get; set; } = true;
    public bool AlertLimit { get; set; } = true;
    public AlertDelivery Delivery { get; set; } = AlertDelivery.TokendialBanners;
    public int ResetLeadMinutes { get; set; } = 10;
    public int WaitingDebounceSeconds { get; set; } = 20;
    public string? LastSeenVersion { get; set; }
    /// <summary>A code from Strings.Languages, or null to follow the system.</summary>
    public string? Language { get; set; }
    public Appearance Appearance { get; set; } = Appearance.Dark;

    [JsonIgnore]
    public AlertConfig AlertConfig => AlertConfig.Default with
    {
        Thresholds = Thresholds.Where(t => t is > 0 and <= 100).Distinct().OrderBy(t => t).ToList(),
        ResetLead = TimeSpan.FromMinutes(Math.Clamp(ResetLeadMinutes, 1, 120)),
        WaitingDebounce = TimeSpan.FromSeconds(Math.Clamp(WaitingDebounceSeconds, 5, 600))
    };

    public bool Wants(AlertKind kind) => kind switch
    {
        AlertKind.Threshold => AlertThresholds,
        AlertKind.ResetSoon or AlertKind.ResetDone => AlertResetSoon,
        AlertKind.Waiting => AlertWaiting,
        AlertKind.Limit => AlertLimit,
        _ => true
    };

    public Settings Clone() => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this, Json))!;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string DefaultFile => Paths.In("settings.json");

    /// <summary>
    /// The saved settings, or the defaults. The defaults are saved over the file the next time anything
    /// changes, so a file that cannot be parsed - a value only a newer build knows, a truncated write - is
    /// moved aside to <c>settings.json.bad</c> first rather than lost.
    /// </summary>
    public static Settings Load(string? file = null)
    {
        file ??= DefaultFile;
        string text;
        try
        {
            if (!File.Exists(file)) return new Settings();
            text = ReadWithOneRetry(file);
        }
        catch (Exception error)
        {
            Diagnostics.Log.Ui.Error($"settings: {error.Message}");
            return new Settings();
        }
        try
        {
            var loaded = JsonSerializer.Deserialize<Settings>(text, Json) ?? new Settings();
            loaded.Schema = CurrentSchema;
            return loaded;
        }
        catch (JsonException error)
        {
            Diagnostics.Log.Ui.Error($"settings: {error.Message}");
            SetAside(file);
            return new Settings();
        }
        catch (Exception error)
        {
            Diagnostics.Log.Ui.Error($"settings: {error.Message}");
            return new Settings();
        }
    }

    /// <summary>An antivirus scan or a sync client can hold the file for a moment at login; one short wait outlasts most.</summary>
    private static string ReadWithOneRetry(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (IOException)
        {
            Thread.Sleep(250);
            return File.ReadAllText(file);
        }
    }

    private static void SetAside(string file)
    {
        try
        {
            File.Move(file, file + ".bad", overwrite: true);
        }
        catch (Exception error)
        {
            Diagnostics.Log.Ui.Error($"settings: could not set aside: {error.Message}");
        }
    }

    public void Save(string? file = null)
    {
        file ??= DefaultFile;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, file, overwrite: true);
    }
}
