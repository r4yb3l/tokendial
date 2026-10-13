namespace Tokendial.Core.Sessions;

/// <summary>Finds what a session's process lives inside: a terminal, an editor or a desktop app, by walking up its parents.</summary>
public static class Ancestry
{
    /// <summary>
    /// The first process, starting with <paramref name="pid"/> itself, for which <paramref name="pick"/> answers.
    /// Windows recycles process ids, so a parent can point back down the chain; a visited set ends the walk there.
    /// </summary>
    public static T? FirstAnswer<T>(int pid, Func<int, int?> parentOf, Func<int, T?> pick) where T : struct
    {
        var seen = new HashSet<int>();
        for (int? current = pid; current is int id && id > 0 && seen.Add(id); current = parentOf(id))
        {
            if (pick(id) is T found) return found;
        }
        return null;
    }
}
