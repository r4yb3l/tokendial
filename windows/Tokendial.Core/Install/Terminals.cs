namespace Tokendial.Core.Install;

/// <summary>
/// A terminal emulator and the arguments that make it run one script. <c>{script}</c> stands for the path.
/// </summary>
/// <param name="ForksAway">
/// True when the program returns immediately instead of living as long as the window it opened. The
/// client-server terminals do this: the command hands the request to an existing process and exits within
/// milliseconds, so the caller must not treat its exit as "the user closed the terminal" - that would end
/// the wait before anything had been installed.
/// </param>
public sealed record Terminal(string Command, IReadOnlyList<string> Template, bool ForksAway)
{
    public IReadOnlyList<string> Arguments(string script) =>
        Template.Select(part => part.Replace("{script}", script)).ToList();
}

/// <summary>
/// Which terminal to open on a freedesktop desktop, decided in Core so it can be tested anywhere.
/// </summary>
/// <remarks>
/// There is no single answer: a distribution picks one, a user overrides it, and a minimal system has none at
/// all. The last case is a real configuration and answers null rather than a guess - the sheet then offers the
/// command to copy, which is what it already does when a user would rather run it themselves.
/// </remarks>
public static class Terminals
{
    /// <summary>
    /// The distribution's own choice first, then the desktops' own, then the ones people install on purpose.
    /// A terminal is only reached for if the ones before it are not there.
    /// </summary>
    private static readonly Terminal[] Known =
    [
        new("x-terminal-emulator", ["-e", "sh {script}"], ForksAway: true),
        new("gnome-terminal", ["--", "sh", "{script}"], ForksAway: true),
        new("mate-terminal", ["--", "sh", "{script}"], ForksAway: true),
        new("tilix", ["-e", "sh {script}"], ForksAway: true),
        new("xfce4-terminal", ["--disable-server", "-x", "sh", "{script}"], ForksAway: false),
        new("konsole", ["-e", "sh", "{script}"], ForksAway: false),
        new("kitty", ["sh", "{script}"], ForksAway: false),
        new("alacritty", ["-e", "sh", "{script}"], ForksAway: false),
        new("wezterm", ["start", "--", "sh", "{script}"], ForksAway: false),
        new("ghostty", ["-e", "sh", "{script}"], ForksAway: false),
        new("xterm", ["-e", "sh", "{script}"], ForksAway: false)
    ];

    /// <summary>
    /// The terminal to use, or null when the system has none. <c>$TOKENDIAL_TERMINAL</c> and
    /// <c>$TERMINAL</c> win over everything, in that order: a user who has said which terminal they want has
    /// said it, and whether it forks is then unknown, so it is assumed - the cautious answer, since the only
    /// cost is not noticing the window closed.
    /// </summary>
    public static Terminal? Resolve(Func<string, string?> env, Func<string, bool> exists)
    {
        foreach (var name in new[] { "TOKENDIAL_TERMINAL", "TERMINAL" })
        {
            if (env(name) is { Length: > 0 } chosen && exists(chosen))
                return Known.FirstOrDefault(t => t.Command == chosen) ?? new Terminal(chosen, ["-e", "sh {script}"], ForksAway: true);
        }
        return Known.FirstOrDefault(t => exists(t.Command));
    }

    /// <summary>The terminals this knows about, for a settings page or a diagnostic to list.</summary>
    public static IReadOnlyList<string> Names => Known.Select(t => t.Command).ToList();
}
