namespace Tokendial.Core.Providers.Codex;

/// <summary>One Codex configuration directory: ~/.codex, or ~/.codex-&lt;slug&gt; for a second account run with CODEX_HOME.</summary>
public sealed record CodexProfile(string? Slug, string Directory)
{
    private static readonly string[] Markers = ["auth.json", "sessions", "config.toml", "state_5.sqlite"];

    public string Id => Slug is null ? "codex" : $"codex-{Slug}";
    public string DisplayName => Slug is null ? "Codex" : $"Codex ({Slug})";
    public string AuthFile => Path.Combine(Directory, "auth.json");
    public string StateFile => Path.Combine(Directory, "state_5.sqlite");
    /// <summary>The desktop app keeps one database whatever CODEX_HOME says, so only the default profile reads it.</summary>
    public string? DesktopFile => Slug is null ? Path.Combine(Directory, "sqlite", "codex-dev.db") : null;
    public string SignInCommand => Slug is null ? "codex login" : $"$env:CODEX_HOME='~/.codex-{Slug}'; codex login";

    public static CodexProfile Default(string? home = null) => new(null, Path.Combine(home ?? Http.Home, ".codex"));

    public static IReadOnlyList<CodexProfile> Discover(string? home = null) =>
        Profiles.Discover(home ?? Http.Home, ".codex", Markers).Select(p => new CodexProfile(p.Slug, p.Directory)).ToList();
}
