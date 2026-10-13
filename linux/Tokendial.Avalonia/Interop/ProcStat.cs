namespace Tokendial.Linux.Interop;

/// <summary>A process's parent, read from <c>/proc/&lt;pid&gt;/stat</c>: what "take me there" walks up.</summary>
public static class ProcStat
{
    /// <summary>
    /// The parent id in a stat line. The second field is the command name in parentheses, and that name may
    /// itself hold spaces and parentheses - <c>(tmux: server)</c>, <c>(a) b)</c> - so the fields are counted
    /// from the last closing parenthesis rather than split from the start. State comes next, then the parent.
    /// </summary>
    public static int? Parent(string stat)
    {
        var close = stat.LastIndexOf(')');
        if (close < 0) return null;
        var fields = stat[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 1 && int.TryParse(fields[1], out var parent) ? parent : null;
    }

    /// <summary>
    /// The parent of a running process, or null at init and for anything that has gone. Init is where the
    /// walk stops: every orphan, daemon and session manager ends up under it, and none of those is the window
    /// a waiting agent lives in.
    /// </summary>
    public static int? ParentOf(int pid)
    {
        if (pid <= 1) return null;
        try { return Parent(File.ReadAllText($"/proc/{pid}/stat")) is int parent and > 1 ? parent : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
