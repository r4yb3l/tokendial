using System.Runtime.InteropServices;

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
    public static Screen PrimaryScreen()
    {
        var monitor = MonitorFromPoint(new POINT { X = 0, Y = 0 }, 1);
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

    /// <summary>True when Windows would queue a toast silently: quiet hours, presentation, full-screen game, or a busy state.</summary>
    public static bool NotificationsMuted()
    {
        if (SHQueryUserNotificationState(out var state) != 0) return false;
        return state is not (5 or 1);
    }
}
