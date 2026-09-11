using Microsoft.Data.Sqlite;
using Tokendial.Core.Diagnostics;

namespace Tokendial.Core.Providers;

/// <summary>
/// Read-only access to a database another app owns in WAL mode: plain
/// read-only first (needs the -shm sidecar the owner keeps while running),
/// immutable next (works after the owner quit and checkpointed), a shared-read
/// copy last (for a file an indexer briefly locked). Never pooled, so the
/// owner can clean up its sidecars.
/// </summary>
public static class Sqlite
{
    private static readonly string CacheDirectory = Paths.In("cache");
    private static int swept;

    /// <summary>A copy holds another tool's session token; one left behind by a crash must not outlive the next start.</summary>
    public static void SweepCache()
    {
        if (Interlocked.Exchange(ref swept, 1) == 1 || !Directory.Exists(CacheDirectory)) return;
        foreach (var stale in Directory.EnumerateFiles(CacheDirectory))
        {
            try { File.WriteAllBytes(stale, []); File.Delete(stale); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public static IReadOnlyList<string>? Column(string path, string sql, string? parameter = null) =>
        Read(path, connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (parameter is not null) command.Parameters.AddWithValue("$p", parameter);
            using var reader = command.ExecuteReader();
            var rows = new List<string>();
            while (reader.Read()) if (!reader.IsDBNull(0)) rows.Add(reader.GetString(0));
            return (IReadOnlyList<string>)rows;
        });

    public static IReadOnlyList<string?[]>? Rows(string path, string sql) =>
        Read(path, connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            var rows = new List<string?[]>();
            while (reader.Read())
            {
                var row = new string?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString();
                rows.Add(row);
            }
            return (IReadOnlyList<string?[]>)rows;
        });

    /// <summary>
    /// A path as a SQLite file URI. A Unix path already starts with a slash, so gluing it onto "file:///"
    /// produced file:////home/..., which names a host of nothing and an absolute path - not the same file.
    /// </summary>
    private static string FileUri(string path)
    {
        var slashes = path.Replace('\\', '/').Replace(" ", "%20");
        return slashes.StartsWith('/') ? "file://" + slashes : "file:///" + slashes;
    }

    private static T? Read<T>(string path, Func<SqliteConnection, T> query) where T : class
    {
        if (!File.Exists(path)) return null;
        foreach (var connectionString in new[]
                 {
                     Builder(path).ToString(),
                     Builder(FileUri(path) + "?immutable=1").ToString()
                 })
        {
            try
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                return query(connection);
            }
            catch (Exception error) when (error is SqliteException or IOException)
            {
                Log.Usage.Debug($"sqlite {Path.GetFileName(path)}: {error.Message}");
            }
        }
        return ReadCopy(path, query);
    }

    private static SqliteConnectionStringBuilder Builder(string source) => new()
    {
        DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 1
    };

    private static T? ReadCopy<T>(string path, Func<SqliteConnection, T> query) where T : class
    {
        SweepCache();
        var copy = Path.Combine(CacheDirectory, $"{Guid.NewGuid():N}.sqlite");
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            CopyShared(path, copy);
            if (File.Exists(path + "-wal")) CopyShared(path + "-wal", copy + "-wal");
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Pooling = false, DefaultTimeout = 1 }.ToString());
            connection.Open();
            return query(connection);
        }
        catch (Exception error)
        {
            Log.Usage.Error($"sqlite copy of {Path.GetFileName(path)}: {error.Message}");
            return null;
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                try { if (File.Exists(copy + suffix)) { File.WriteAllBytes(copy + suffix, []); File.Delete(copy + suffix); } } catch (Exception) { }
            }
        }
    }

    private static void CopyShared(string from, string to)
    {
        using var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var target = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(target);
    }
}
