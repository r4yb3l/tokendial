using System.Runtime.InteropServices;
using Tokendial.Core.Sessions;

namespace Tokendial.App.Interop;

/// <summary>The handful of Win32 calls WPF does not wrap: non-activating windows, cursor and monitor queries in physical pixels, notification state.</summary>
public static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOZORDER = 0x0004;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO { public int Size; public RECT Monitor; public RECT Work; public uint Flags; }

    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);

    public sealed record Screen(RECT Bounds, RECT Work, double Scale);

    /// <summary>The monitor under the taskbar's origin, which is where a top-edge capsule belongs.</summary>
    public static Screen PrimaryScreen() => ScreenAt(new POINT(), MONITOR_DEFAULTTOPRIMARY);

    /// <summary>The monitor holding a point in physical pixels, or the nearest one when the point is off every screen.</summary>
    public static Screen ScreenAt(POINT point, uint fallback = MONITOR_DEFAULTTONEAREST)
    {
        var monitor = MonitorFromPoint(point, fallback);
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        var scale = GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;
        return new Screen(info.Monitor, info.Work, scale);
    }

    public static void MakeUnobtrusive(IntPtr hwnd)
    {
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST);
    }

    public static void Place(IntPtr hwnd, int x, int y, int width, int height, bool show) =>
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE | (show ? SWP_SHOWWINDOW : 0));

    public static POINT Cursor()
    {
        GetCursorPos(out var point);
        return point;
    }

    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    /// <summary>
    /// Brings forward the window a process runs inside: its own, or the first ancestor's that has one, which is the
    /// terminal, editor or desktop app that started it. Explorer ends the walk, since it is everything's parent and
    /// its windows are never the answer. False when nothing was found or Windows refused the switch.
    /// </summary>
    public static bool BringForward(int pid)
    {
        var processes = Processes();
        var windows = TopLevelWindows();
        var target = Ancestry.FirstAnswer(pid,
            id => processes.TryGetValue(id, out var p) && !p.Exe.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) ? p.Parent : null,
            id => processes.TryGetValue(id, out var p) && !p.Exe.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) && windows.TryGetValue(id, out var hwnd) ? hwnd : (IntPtr?)null);
        if (target is not IntPtr window) return false;
        if (IsIconic(window)) ShowWindow(window, SW_RESTORE);
        return SetForegroundWindow(window);
    }

    private const uint TH32CS_SNAPPROCESS = 0x2;
    private const uint GW_OWNER = 4;
    private const int SW_RESTORE = 9;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public int Size; public uint Usage; public int ProcessId; public IntPtr DefaultHeapId; public uint ModuleId; public uint Threads;
        public int ParentProcessId; public int PriClassBase; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

    private static Dictionary<int, (int Parent, string Exe)> Processes()
    {
        var found = new Dictionary<int, (int, string)>();
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == new IntPtr(-1)) return found;
        try
        {
            var entry = new PROCESSENTRY32 { Size = Marshal.SizeOf<PROCESSENTRY32>() };
            for (var more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
                found[entry.ProcessId] = (entry.ParentProcessId, entry.ExeFile);
        }
        finally { CloseHandle(snapshot); }
        return found;
    }

    /// <summary>Each process's frontmost visible, unowned, titled window: the kind the taskbar would show.</summary>
    private static Dictionary<int, IntPtr> TopLevelWindows()
    {
        var found = new Dictionary<int, IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == IntPtr.Zero && GetWindowTextLength(hwnd) > 0
                && (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) == 0)
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                found.TryAdd(pid, hwnd);
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>True when Windows would queue a toast silently: quiet hours, presentation, full-screen game, or a busy state.</summary>
    public static bool NotificationsMuted()
    {
        if (SHQueryUserNotificationState(out var state) != 0) return false;
        return state is not (5 or 1);
    }
}
