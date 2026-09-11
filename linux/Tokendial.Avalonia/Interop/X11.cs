using System.Runtime.InteropServices;

namespace Tokendial.Linux.Interop;

/// <summary>
/// The parts of X11 the dock needs and Avalonia does not expose. Deliberately a separate connection from
/// Avalonia's: every call here is a property write or a pointer query, neither needs to be ordered against
/// Avalonia's event loop, and Xlib connections are not thread-safe, so sharing one would be the bug rather
/// than the saving. X11 lets any client set properties on a window it knows the id of, which is what makes
/// this possible at all.
/// </summary>
public static unsafe class X11
{
    private const string Xlib = "libX11.so.6";
    private const string Xext = "libXext.so.6";

    [DllImport(Xlib)] private static extern nint XOpenDisplay(nint name);
    [DllImport(Xlib)] private static extern int XFlush(nint display);
    [DllImport(Xlib)] private static extern int XSync(nint display, bool discard);
    [DllImport(Xlib)] private static extern nint XInternAtom(nint display, string name, bool onlyIfExists);
    [DllImport(Xlib)] private static extern nint XDefaultRootWindow(nint display);
    [DllImport(Xlib)] private static extern int XChangeProperty(nint display, nint window, nint property, nint type,
        int format, int mode, byte[] data, int elements);
    [DllImport(Xlib)] private static extern int XDeleteProperty(nint display, nint window, nint property);
    [DllImport(Xlib)] private static extern int XGetWindowProperty(nint display, nint window, nint property,
        nint offset, nint length, bool delete, nint requestedType, out nint actualType, out int actualFormat,
        out nint items, out nint bytesAfter, out nint data);
    [DllImport(Xlib)] private static extern int XFree(nint data);
    [DllImport(Xlib)] private static extern nint XGetWMHints(nint display, nint window);
    [DllImport(Xlib)] private static extern int XSetWMHints(nint display, nint window, nint hints);
    [DllImport(Xlib)] private static extern nint XAllocWMHints();
    [DllImport(Xlib)] private static extern bool XQueryPointer(nint display, nint window, out nint root, out nint child,
        out int rootX, out int rootY, out int winX, out int winY, out uint mask);
    [DllImport(Xlib)] private static extern nint XGetSelectionOwner(nint display, nint selection);
    [DllImport(Xext)] private static extern int XShapeCombineRectangles(nint display, nint window, int kind,
        int xOffset, int yOffset, XRectangle[] rectangles, int count, int operation, int ordering);

    [StructLayout(LayoutKind.Sequential)]
    public struct XRectangle { public short X, Y; public ushort Width, Height; }

    // XWMHints, only as far as the field this needs. InputHint is bit 0; `input` follows `flags`.
    [StructLayout(LayoutKind.Sequential)]
    private struct XWMHints
    {
        public nint Flags;
        public int Input;
        public int InitialState;
        public nint IconPixmap, IconWindow;
        public int IconX, IconY;
        public nint IconMask, WindowGroup;
    }

    private const int PropModeReplace = 0;
    private const int ShapeBounding = 0, ShapeInput = 2;
    private const int ShapeSet = 0;
    private const int Unsorted = 0;
    private const nint InputHint = 1;
    private const nint XA_ATOM = 4, XA_CARDINAL = 6;

    public static nint Display { get; private set; }

    public static bool Open()
    {
        if (Display != 0) return true;
        Display = XOpenDisplay(0);
        return Display != 0;
    }

    private static nint Atom(string name) => XInternAtom(Display, name, false);

    private static void PutAtoms(nint window, string property, params string[] values)
    {
        var data = new byte[values.Length * 8];
        for (var i = 0; i < values.Length; i++)
            BitConverter.GetBytes((long)Atom(values[i])).CopyTo(data, i * 8);
        XChangeProperty(Display, window, Atom(property), XA_ATOM, 32, PropModeReplace, data, values.Length);
    }

    private static void PutCardinal(nint window, string property, long value) =>
        XChangeProperty(Display, window, Atom(property), XA_CARDINAL, 32, PropModeReplace, BitConverter.GetBytes(value), 1);

    /// <summary>
    /// Ask the window manager to treat this as furniture: above everything, in no task list, in no switcher.
    /// Measured on Cinnamon: the DOCK type alone is enough to hold the window above a maximised one - the
    /// state atoms were deleted entirely and the stacking order did not change - but they are set anyway
    /// because other window managers are not Muffin.
    /// </summary>
    public static void MakeDock(nint window) => MakeAlwaysOnTop(window, "_NET_WM_WINDOW_TYPE_DOCK");

    /// <summary>
    /// The window type a notification is supposed to declare. Window managers place and stack these above
    /// everything, including a fullscreen window, which is what a banner has to do to be worth drawing.
    /// </summary>
    public static void MakeNotification(nint window) => MakeAlwaysOnTop(window, "_NET_WM_WINDOW_TYPE_NOTIFICATION");

    private static void MakeAlwaysOnTop(nint window, string type)
    {
        PutAtoms(window, "_NET_WM_WINDOW_TYPE", type);
        PutAtoms(window, "_NET_WM_STATE", "_NET_WM_STATE_ABOVE", "_NET_WM_STATE_SKIP_TASKBAR", "_NET_WM_STATE_SKIP_PAGER");
        XFlush(Display);
    }

    /// <summary>
    /// The ICCCM "no input" focus model, which is what WS_EX_NOACTIVATE means here. Avalonia's
    /// ShowActivated=false does not do this on Linux (AvaloniaUI#17186), so it is set by hand: the input
    /// hint tells the window manager never to give this window the keyboard, a user time of zero tells it
    /// not to activate the window on map, and WM_TAKE_FOCUS is removed so it is never offered focus either.
    /// </summary>
    public static void RefuseFocus(nint window)
    {
        var hints = XGetWMHints(Display, window);
        if (hints == 0) hints = XAllocWMHints();
        if (hints != 0)
        {
            var h = (XWMHints*)hints;
            h->Flags |= InputHint;
            h->Input = 0;
            XSetWMHints(Display, window, hints);
            XFree(hints);
        }
        PutCardinal(window, "_NET_WM_USER_TIME", 0);
        XDeleteProperty(Display, window, Atom("WM_PROTOCOLS"));
        XFlush(Display);
    }

    /// <summary>
    /// X11 has no per-pixel input region, so click-through is this or nothing: the input shape is set to the
    /// rectangles that should receive clicks, and everything outside them passes straight through to whatever
    /// is behind. The bounding shape is left alone so the compositor still draws the whole window.
    /// </summary>
    public static void InputShape(nint window, params XRectangle[] rectangles)
    {
        XShapeCombineRectangles(Display, window, ShapeInput, 0, 0, rectangles, rectangles.Length, ShapeSet, Unsorted);
        XFlush(Display);
    }

    /// <summary>The global cursor, in physical pixels. `sameScreen` is false under XWayland when the pointer is over another surface, and then the coordinates are meaningless.</summary>
    public static (int X, int Y, bool SameScreen) Pointer()
    {
        var root = XDefaultRootWindow(Display);
        var same = XQueryPointer(Display, root, out _, out _, out var x, out var y, out _, out _, out _);
        return (x, y, same);
    }

    /// <summary>Whether a compositing manager owns the screen. Without one there is no per-pixel alpha at all.</summary>
    public static bool Composited() => XGetSelectionOwner(Display, Atom("_NET_WM_CM_S0")) != 0;

    /// <summary>The work area the window manager publishes: the screen minus panels. Four cardinals per desktop.</summary>
    public static (int X, int Y, int W, int H)? WorkArea()
    {
        var root = XDefaultRootWindow(Display);
        if (XGetWindowProperty(Display, root, Atom("_NET_WORKAREA"), 0, 4, false, XA_CARDINAL,
                out _, out _, out var items, out _, out var data) != 0 || data == 0 || (long)items < 4)
            return null;
        var p = (long*)data;
        var area = ((int)p[0], (int)p[1], (int)p[2], (int)p[3]);
        XFree(data);
        return area;
    }

    public static void Sync() => XSync(Display, false);
}
