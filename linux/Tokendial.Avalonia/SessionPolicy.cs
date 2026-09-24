namespace Tokendial.Linux;

/// <summary>The dock needs a native X11 session, including its global pointer and input regions.</summary>
public static class SessionPolicy
{
    /// <summary>Returns a catalogue key explaining why startup is refused, or null for an X11 session.</summary>
    public static string? Rejection(string? sessionType, string? waylandDisplay, string? display)
    {
        if (string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(waylandDisplay))
            return "linux.session.wayland";
        return string.IsNullOrWhiteSpace(display) ? "linux.session.noDisplay" : null;
    }
}
