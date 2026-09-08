using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Tokendial.Core.Providers;
using Tokendial.Core.Providers.Antigravity;
using Tokendial.Core.Providers.Codex;
using Tokendial.Core.Providers.Cursor;

namespace Tokendial.Core.Sessions;

/// <summary>Claude Code writes ~/.claude/sessions/&lt;pid&gt;.json the moment its state changes; the directory is watched, and a slow timer catches processes that died silently.</summary>
public sealed class ClaudeSessions : PolledMonitor
{
    private readonly string directory;
    private readonly Func<int, DateTimeOffset?, bool> alive;
    private FileSystemWatcher? watcher;
    private Timer? debounce;

    public ClaudeSessions(string providerId, string directory, Func<int, DateTimeOffset?, bool>? alive = null)
        : base(providerId, TimeSpan.FromSeconds(5))
    {
        this.directory = directory;
        this.alive = alive ?? Liveness.IsAlive;
        if (!Directory.Exists(directory)) return;
        try
        {
            watcher = new FileSystemWatcher(directory, "*.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            FileSystemEventHandler bump = (_, _) => (debounce ??= new Timer(_ => Poll())).Change(120, Timeout.Infinite);
            watcher.Changed += bump;
            watcher.Created += bump;
            watcher.Deleted += bump;
            watcher.Renamed += (s, e) => bump(s, e);
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception) { watcher = null; }
    }

    protected override IReadOnlyList<AgentSession> Read() => Read(directory, alive);

    public static IReadOnlyList<AgentSession> Read(string directory, Func<int, DateTimeOffset?, bool> alive)
    {
        if (!Directory.Exists(directory)) return [];
        var found = new List<AgentSession>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            string text;
            try { text = File.ReadAllText(file); } catch (IOException) { continue; }
            var parsed = Parse(text);
            if (parsed is null) continue;
            var (pid, started, session) = parsed.Value;
            if (alive(pid, started)) found.Add(session);
        }
        return found.OrderByDescending(s => s.Since).ToList();
    }

    /// <summary>Decoded leniently: an unknown field must never cost a session.</summary>
    public static (int Pid, DateTimeOffset? StartedAt, AgentSession Session)? Parse(string json)
    {
        using var document = Json.Parse(json);
        if (document is null) return null;
        var root = document.RootElement;
        if (root.Num("pid") is not double pid || root.Str("cwd") is not string cwd) return null;
        var state = (root.Str("tempo"), root.Str("status")) switch
        {
            ("blocked", _) or (_, "waiting") => SessionState.Waiting,
            ("active", _) or (_, "busy") => SessionState.Working,
            _ => SessionState.Idle
        };
        var folder = cwd.TrimEnd('/', '\\');
        folder = folder[(Math.Max(folder.LastIndexOf('/'), folder.LastIndexOf('\\')) + 1)..];
        var surface = root.Str("entrypoint") switch
        {
            "claude-desktop" or "claude-desktop-3p" => "Desktop",
            "claude-vscode" => "VS Code",
            "local-agent" => "Agent",
            _ => "Terminal"
        };
        var started = root.EpochMillis("startedAt") ?? ProcStart(root.Str("procStart"));
        var since = root.EpochMillis("statusUpdatedAt") ?? root.EpochMillis("updatedAt") ?? DateTimeOffset.UtcNow;
        return ((int)pid, started, new AgentSession($"claude.{(int)pid}", root.Str("name") ?? folder, $"{surface} · {folder}", state, root.Str("waitingFor") ?? root.Str("needs"), since));
    }

    /// <summary>A UTC ctime string with a space-padded day.</summary>
    public static DateTimeOffset? ProcStart(string? text)
    {
        if (text is null) return null;
        var collapsed = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return DateTimeOffset.TryParseExact(collapsed, "ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
    }

    public new void Dispose()
    {
        watcher?.Dispose();
        debounce?.Dispose();
        base.Dispose();
    }
}

/// <summary>Cursor's composerHeaders rows, polled: a run is live while the editor that started it runs and the conversation is still being written.</summary>
public sealed class CursorSessions : PolledMonitor
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);
    private readonly string store;

    public CursorSessions(string? store = null) : base("cursor", TimeSpan.FromSeconds(2))
    {
        this.store = store ?? CursorCredential.DefaultStore;
    }

    protected override IReadOnlyList<AgentSession> Read() => Read(store, EditorStart(), StaleAfter, DateTimeOffset.UtcNow);

    /// <summary>The oldest Cursor.exe is the editor; renderers come and go. No readable start time counts as "since forever".</summary>
    public static DateTimeOffset? EditorStart()
    {
        var processes = Process.GetProcessesByName("Cursor");
        try
        {
            if (processes.Length == 0) return null;
            DateTimeOffset? oldest = null;
            foreach (var p in processes)
            {
                try { var s = new DateTimeOffset(p.StartTime.ToUniversalTime()); if (oldest is null || s < oldest) oldest = s; } catch (Exception) { }
            }
            return oldest ?? DateTimeOffset.MinValue;
        }
        finally { foreach (var p in processes) p.Dispose(); }
    }

    public static IReadOnlyList<AgentSession> Read(string store, DateTimeOffset? editorStart, TimeSpan staleAfter, DateTimeOffset now)
    {
        var rows = Sqlite.Column(store, "SELECT value FROM composerHeaders WHERE isArchived = 0 ORDER BY recency DESC LIMIT 40") ?? [];
        return rows.Select(r => Parse(r, editorStart, staleAfter, now)).Where(s => s is not null).Cast<AgentSession>().OrderByDescending(s => s.Since).ToList();
    }

    public static AgentSession? Parse(string header, DateTimeOffset? editorStart, TimeSpan staleAfter, DateTimeOffset now)
    {
        using var document = Json.Parse(header);
        if (document is null) return null;
        var root = document.RootElement;
        var id = root.Str("composerId");
        if (id is null) return null;
        var blocked = root.Bool("hasBlockingPendingActions") == true || root.Bool("hasPendingPlan") == true;
        var runStart = root.EpochMillis("unfinishedRunAt");
        var lastWrite = root.EpochMillis("conversationCheckpointLastUpdatedAt") ?? root.EpochMillis("lastUpdatedAt");
        var running = runStart is not null && editorStart is DateTimeOffset launched && (lastWrite ?? runStart.Value) >= launched && now - (lastWrite ?? runStart.Value) <= staleAfter;
        if (!blocked && !running) return null;
        var since = (running ? runStart : null) ?? lastWrite ?? root.EpochMillis("createdAt") ?? now;
        return new AgentSession($"cursor.{id}", root.Str("name") ?? "Untitled chat", root.Str("subtitle") ?? "Cursor",
            blocked ? SessionState.Waiting : SessionState.Working, blocked ? "needs your input" : null, since);
    }
}

/// <summary>Codex has no status field: a rollout or catalogue row written within 8 s means a turn is running.</summary>
public sealed class CodexSessions : PolledMonitor
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(8);
    private readonly string stateFile;
    private readonly string desktopFile;

    public CodexSessions(string? stateFile = null, string? desktopFile = null) : base("codex", TimeSpan.FromSeconds(2))
    {
        this.stateFile = stateFile ?? CodexStores.StateFile;
        this.desktopFile = desktopFile ?? CodexStores.DesktopFile;
    }

    protected override IReadOnlyList<AgentSession> Read() => Read(stateFile, desktopFile, StaleAfter, DateTimeOffset.UtcNow);

    public static IReadOnlyList<AgentSession> Read(string stateFile, string desktopFile, TimeSpan staleAfter, DateTimeOffset now)
    {
        var candidates = new List<(DateTimeOffset At, string Id, string Name)>();
        if (CodexStores.NewestRollout(stateFile) is string rollout)
        {
            try { candidates.Add((new DateTimeOffset(File.GetLastWriteTimeUtc(rollout)), $"codex.{Path.GetFileName(rollout)}", "Codex")); } catch (IOException) { }
        }
        if (CodexStores.NewestDesktopThread(desktopFile) is var (at, title)) candidates.Add((at, "codex.desktop", title));
        if (candidates.Count == 0) return [];
        var newest = candidates.MaxBy(c => c.At);
        return now - newest.At > staleAfter ? [] : [new AgentSession(newest.Id, newest.Name, "Working", SessionState.Working, null, newest.At)];
    }
}

/// <summary>Antigravity's newest transcript, busy while written within 45 s.</summary>
public sealed class AntigravitySessions : PolledMonitor
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(45);
    private readonly string root;

    public AntigravitySessions(string? root = null) : base("antigravity", TimeSpan.FromSeconds(2))
    {
        this.root = root ?? AntigravityTranscripts.DefaultRoot;
    }

    protected override IReadOnlyList<AgentSession> Read() => Read(root, StaleAfter, DateTimeOffset.UtcNow);

    public static IReadOnlyList<AgentSession> Read(string root, TimeSpan staleAfter, DateTimeOffset now)
    {
        if (!Directory.Exists(root)) return [];
        (DateTimeOffset At, string Trajectory)? newest = null;
        foreach (var trajectory in Directory.EnumerateDirectories(root))
        {
            var transcript = AntigravityTranscripts.TranscriptOf(trajectory);
            if (!File.Exists(transcript)) continue;
            var at = new DateTimeOffset(File.GetLastWriteTimeUtc(transcript));
            if (newest is null || at > newest.Value.At) newest = (at, trajectory);
        }
        if (newest is null || now - newest.Value.At > staleAfter) return [];
        return [new AgentSession($"antigravity.{Path.GetFileName(newest.Value.Trajectory)}", "Antigravity", "Working", SessionState.Working, null, newest.Value.At)];
    }
}

/// <summary>Grok: active_sessions.json rows whose pid lives and whose updates.jsonl was written within 45 s.</summary>
public sealed class GrokSessions : PolledMonitor
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(45);
    public static string DefaultActive => Http.Under(".grok", "active_sessions.json");
    public static string DefaultRoot => Http.Under(".grok", "sessions");
    private readonly string active;
    private readonly string root;
    private readonly Func<int, DateTimeOffset?, bool> alive;

    public GrokSessions(string? active = null, string? root = null, Func<int, DateTimeOffset?, bool>? alive = null) : base("grok", TimeSpan.FromSeconds(2))
    {
        this.active = active ?? DefaultActive;
        this.root = root ?? DefaultRoot;
        this.alive = alive ?? Liveness.IsAlive;
    }

    protected override IReadOnlyList<AgentSession> Read() => Read(active, root, StaleAfter, DateTimeOffset.UtcNow, alive);

    public static IReadOnlyList<AgentSession> Read(string active, string root, TimeSpan staleAfter, DateTimeOffset now, Func<int, DateTimeOffset?, bool> alive)
    {
        if (!File.Exists(active)) return [];
        using var document = Json.Parse(File.ReadAllText(active));
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Array) return [];
        var found = new List<AgentSession>();
        foreach (var row in document.RootElement.EnumerateArray())
        {
            var id = row.Str("session_id");
            if (id is null) continue;
            if (row.Num("pid") is double pid && !alive((int)pid, row.Date("opened_at"))) continue;
            var cwd = row.Str("cwd");
            var directory = SessionDirectory(id, cwd, root);
            if (directory is null) continue;
            var updates = Path.Combine(directory, "updates.jsonl");
            if (!File.Exists(updates)) continue;
            var at = new DateTimeOffset(File.GetLastWriteTimeUtc(updates));
            if (now - at > staleAfter) continue;
            var name = cwd is null ? "Grok" : Path.GetFileName(cwd.TrimEnd('/', '\\'));
            found.Add(new AgentSession($"grok.{id}", string.IsNullOrEmpty(name) ? "Grok" : name, "Grok", SessionState.Working, null, at));
        }
        return found;
    }

    public static string? SessionDirectory(string id, string? cwd, string root)
    {
        if (cwd is not null && Directory.Exists(Path.Combine(root, PercentEncode(cwd), id))) return Path.Combine(root, PercentEncode(cwd), id);
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateDirectories(root).Where(d => !Path.GetFileName(d).StartsWith('.')).Select(d => Path.Combine(d, id)).FirstOrDefault(Directory.Exists);
    }

    public static string PercentEncode(string text)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~') sb.Append(c); else sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }
}
