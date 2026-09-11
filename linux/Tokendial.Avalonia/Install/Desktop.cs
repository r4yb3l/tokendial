using System.Diagnostics;
using System.Reflection;
using Tokendial.Core;
using Tokendial.Core.Diagnostics;

namespace Tokendial.Linux.Install;

/// <summary>
/// Tokendial's own install and uninstall on Linux: the menu entry, its icon, the autostart entry, and taking
/// all three back out again.
/// </summary>
/// <remarks>
/// Velopack's AppImage is deliberately installer-less - download, chmod +x, run - which is right for trying
/// it once and wrong for keeping it: there is no menu entry to launch, and nothing to remove afterwards.
/// So the parity Windows and macOS have comes from the app, the way the Windows Uninstall button already
/// does. Everything written here lives under the user's own home in the paths the XDG base directory and
/// desktop entry specifications name; nothing needs root, a package manager, or a helper such as
/// AppImageLauncher.
/// <para>
/// Inside an AppImage <c>Environment.ProcessPath</c> is the extracted squashfs mount, which is a different
/// directory on every launch - writing it into <c>Exec=</c> produces an entry that breaks the next time the
/// app starts. <c>$APPIMAGE</c> is the path of the file the user actually ran, and is what gets recorded.
/// </para>
/// </remarks>
public static class Desktop
{
    private const string Id = "tokendial";

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string ConfigHome => Env("XDG_CONFIG_HOME") ?? Path.Combine(Home, ".config");
    private static string DataHome => Env("XDG_DATA_HOME") ?? Path.Combine(Home, ".local", "share");

    private static string Applications => Path.Combine(DataHome, "applications");
    private static string Icons => Path.Combine(DataHome, "icons", "hicolor");
    private static string Binaries => Path.Combine(Home, ".local", "bin");

    private static string Entry => Path.Combine(Applications, $"{Id}.desktop");
    private static string AutostartEntry => Path.Combine(ConfigHome, "autostart", $"{Id}.desktop");
    private static string InstalledCopy => Path.Combine(Binaries, "Tokendial.AppImage");

    /// <summary>
    /// Told to the copy it starts, so the first thing the user sees after pressing Install is the dock
    /// opening by itself. Without it the window simply closes and a capsule appears at the top of a screen
    /// the user was not looking at, which reads as nothing having happened.
    /// </summary>
    public const string JustInstalled = "--just-installed";

    /// <summary>The AppImage the user ran, or null when this is a loose build rather than a packaged one.</summary>
    public static string? Image => Env("APPIMAGE");

    /// <summary>
    /// What an entry should launch. The installed copy wins over the file the user ran, because the point of
    /// copying it was to survive an emptied download folder - an entry naming a file that is no longer there
    /// starts nothing, which is the failure this whole class exists to avoid.
    /// </summary>
    private static string Target =>
        File.Exists(InstalledCopy) ? InstalledCopy : Image ?? Environment.ProcessPath ?? "";

    /// <summary>Whether the running copy can be added to the menu: only a packaged one has a file to point at.</summary>
    public static bool CanInstall => Image is not null;

    /// <summary>Whether Tokendial is in the menu, which is the whole of being installed here.</summary>
    public static bool InMenu => File.Exists(Entry);

    /// <summary>
    /// Whether the first run should offer to install. True only for a downloaded AppImage that is not in the
    /// menu and whose user has not already said they want to keep it portable.
    /// </summary>
    public static bool ShouldOffer => CanInstall && !InMenu && !Declined;

    private static string Marker => Path.Combine(Paths.Data, "portable");

    private static bool Declined => File.Exists(Marker);

    /// <summary>Remembers that the user chose to run this copy without installing it, so it stops asking.</summary>
    public static void Decline()
    {
        try
        {
            Directory.CreateDirectory(Paths.Data);
            File.WriteAllText(Marker, "");
        }
        catch (Exception error) { Log.Ui.Error($"decline: {error.Message}"); }
    }

    /// <summary>
    /// Starts the copy that was just installed and leaves it running on its own, so what the user ends up
    /// with is the installed application rather than the file they downloaded.
    /// </summary>
    public static void Launch()
    {
        var start = new ProcessStartInfo(InstalledCopy) { UseShellExecute = false, WorkingDirectory = Home };
        start.ArgumentList.Add(JustInstalled);

        // Started from the home directory on purpose. A child inherits the working directory, and inside an
        // AppImage that is the squashfs mount the parent is about to release - holding it open leaves the
        // mount and its fuse process behind for as long as the new copy runs.
        try { Process.Start(start); }
        catch (Exception error) { Log.Ui.Error($"launch: {error.Message}"); }
    }

    public static bool AutostartIsSet => File.Exists(AutostartEntry);

    /// <summary>
    /// Copies the AppImage somewhere it will survive a cleared Downloads folder, writes the icon in the two
    /// sizes a menu and a HiDPI menu ask for, and writes the entry that names them.
    /// </summary>
    public static bool Install()
    {
        if (Image is not { } image) return false;
        try
        {
            var target = image;
            if (!string.Equals(image, InstalledCopy, StringComparison.Ordinal))
            {
                Directory.CreateDirectory(Binaries);
                File.Copy(image, InstalledCopy, overwrite: true);
                File.SetUnixFileMode(InstalledCopy,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                target = InstalledCopy;
            }

            WriteIcon(256);
            WriteIcon(512);
            Directory.CreateDirectory(Applications);
            File.WriteAllText(Entry, EntryText(target, autostart: false));
            Refresh();
            return true;
        }
        catch (Exception error)
        {
            Log.Ui.Error($"install: {error.Message}");
            return false;
        }
    }

    /// <summary>
    /// Takes out everything <see cref="Install"/> put in, and the user's data only when they said so. The
    /// AppImage is unlinked last and can be the one currently running: its runtime holds the file open, so
    /// the session continues until the app quits.
    /// </summary>
    public static void Remove(bool alsoData)
    {
        Delete(Entry);
        Delete(AutostartEntry);
        Delete(Path.Combine(Icons, "256x256", "apps", $"{Id}.png"));
        Delete(Path.Combine(Icons, "512x512", "apps", $"{Id}.png"));
        Delete(InstalledCopy);
        Delete(Marker);
        if (alsoData)
        {
            try { if (Directory.Exists(Paths.Data)) Directory.Delete(Paths.Data, recursive: true); }
            catch (Exception error) { Log.Ui.Error($"remove data: {error.Message}"); }
        }
        Refresh();
    }

    /// <summary>Launch at login, which on a freedesktop session is an entry in autostart and nothing else.</summary>
    public static void SetAutostart(bool on)
    {
        try
        {
            if (!on) { Delete(AutostartEntry); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(AutostartEntry)!);
            File.WriteAllText(AutostartEntry, EntryText(Target, autostart: true));
        }
        catch (Exception error) { Log.Ui.Error($"autostart: {error.Message}"); }
    }

    /// <summary>
    /// Makes the autostart entry match the setting. Written again on every launch for the same reason the
    /// Windows Run key is: an update changes the path it holds, and an entry pointing at the old one starts
    /// nothing.
    /// </summary>
    public static void SyncAutostart(bool on)
    {
        if (on) SetAutostart(true);
        else if (AutostartIsSet) SetAutostart(false);
    }

    private static string EntryText(string exec, bool autostart)
    {
        var lines = new List<string>
        {
            "[Desktop Entry]",
            "Type=Application",
            "Version=1.0",
            "Name=Tokendial",
            "Comment=Usage dials for AI coding assistants",
            $"Exec={Quote(exec)}",
            $"Icon={Id}",
            "Terminal=false",
            // One main category only: desktop-file-validate warns that two of them can list the application
            // twice in the menu. The searchable words go in Keywords, which is what that field is for.
            "Categories=Utility;",
            "Keywords=tokens;usage;limit;quota;claude;codex;copilot;gemini;ai;",
            "StartupNotify=false",
            // Cinnamon and GNOME match a window to its entry by class; without this the dock and the settings
            // window are a second, nameless icon in the window list.
            "StartupWMClass=Tokendial"
        };
        if (autostart)
        {
            lines.Add("X-GNOME-Autostart-enabled=true");
            lines.Add("Hidden=false");
        }
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>
    /// Exec quoting as the desktop entry specification defines it: the value is a double-quoted string in
    /// which a backslash and a double quote are themselves escaped with a backslash.
    /// </summary>
    private static string Quote(string path) => "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void WriteIcon(int size)
    {
        var directory = Path.Combine(Icons, $"{size}x{size}", "apps");
        Directory.CreateDirectory(directory);
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream($"tokendial-{size}.png")
            ?? throw new FileNotFoundException($"tokendial-{size}.png");
        using var file = File.Create(Path.Combine(directory, $"{Id}.png"));
        source.CopyTo(file);
    }

    private static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception error) { Log.Ui.Error($"delete {path}: {error.Message}"); }
    }

    /// <summary>
    /// Asks the desktop to notice. Both tools are optional and absent on a minimal install, which is why
    /// neither failing is treated as the install failing - the entry is on disk either way and the menu
    /// picks it up on the next session.
    /// </summary>
    private static void Refresh()
    {
        Run("update-desktop-database", Applications);
        Run("gtk-update-icon-cache", "-f", "-t", Icons);
    }

    private static void Run(string tool, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            Process.Start(start)?.WaitForExit(5000);
        }
        catch (Exception) { }
    }

    private static string? Env(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value) ? null : value;
    }
}
