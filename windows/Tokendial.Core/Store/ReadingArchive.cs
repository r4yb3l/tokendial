using System.Text.Json;
using System.Text.Json.Serialization;
using Tokendial.Core.Model;

namespace Tokendial.Core.Store;

public sealed record Remembered(ProviderReading Reading, DateTimeOffset TakenAt);

/// <summary>
/// The last good reading per provider and each provider's rate-limit deadline,
/// kept across launches. A dated number beats a blank dial, and a relaunch
/// during a penalty should wait rather than spend an attempt.
/// </summary>
public sealed class ReadingArchive
{
    private sealed record Row(string ProviderId, string DisplayName, Fidelity Fidelity, List<UsageWindow> Windows, string? HeadlineId, DateTimeOffset TakenAt);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly object gate = new();
    private readonly string readingsFile;
    private readonly string backoffFile;
    private readonly Func<DateTimeOffset> now;

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tokendial");

    public ReadingArchive(string? directory = null, Func<DateTimeOffset>? now = null)
    {
        Directory = directory ?? DefaultDirectory;
        readingsFile = Path.Combine(Directory, "readings.json");
        backoffFile = Path.Combine(Directory, "backoff.json");
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public string Directory { get; }

    public Dictionary<string, Remembered> Load()
    {
        lock (gate)
        {
            var rows = Read<List<Row>>(readingsFile) ?? [];
            return rows.ToDictionary(r => r.ProviderId, r => new Remembered(
                new ProviderReading(r.ProviderId, r.DisplayName, r.Fidelity, new ReadingStatus.Stale(r.TakenAt), r.Windows, r.HeadlineId), r.TakenAt));
        }
    }

    public void Save(IReadOnlyDictionary<string, Remembered> readings)
    {
        lock (gate)
        {
            Write(readingsFile, readings.Values.OrderBy(r => r.Reading.ProviderId, StringComparer.Ordinal)
                .Select(r => new Row(r.Reading.ProviderId, r.Reading.DisplayName, r.Reading.Fidelity, r.Reading.Windows.ToList(), r.Reading.HeadlineId, r.TakenAt))
                .ToList());
        }
    }

    public void Forget(string providerId)
    {
        lock (gate)
        {
            var readings = Load();
            readings.Remove(providerId);
            Save(readings);
        }
    }

    public DateTimeOffset? BackoffUntil(string providerId)
    {
        lock (gate)
        {
            var table = Read<Dictionary<string, DateTimeOffset>>(backoffFile) ?? new();
            return table.TryGetValue(providerId, out var until) && until > now() ? until : null;
        }
    }

    public void SetBackoff(string providerId, DateTimeOffset? until)
    {
        lock (gate)
        {
            var table = Read<Dictionary<string, DateTimeOffset>>(backoffFile) ?? new();
            if (until is DateTimeOffset u) table[providerId] = u;
            else table.Remove(providerId);
            Write(backoffFile, table);
        }
    }

    private static T? Read<T>(string file) where T : class
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Write<T>(string file, T value)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var temporary = file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json));
            File.Move(temporary, file, overwrite: true);
        }
        catch (Exception)
        {
        }
    }
}
