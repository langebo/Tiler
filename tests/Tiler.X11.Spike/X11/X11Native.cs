using System.Runtime.InteropServices;

namespace Tiler.X11;

/// <summary>
/// libX11 entry points resolved with NativeLibrary (= dlopen) into unmanaged function
/// pointers: no DllImport stub, so Native AOT has nothing to marshal and nothing to JIT.
/// </summary>
internal static unsafe class Xlib
{
    public const int Success = 0;
    public const int AnyPropertyType = 0; // None == AnyPropertyType for XGetWindowProperty
    public const int PropModeReplace = 0;
    public const nuint CWOverrideRedirect = 1 << 9;

    private static readonly nint Library = NativeLibrary.Load("libX11.so.6");

    private static void* Export(string name) => (void*)NativeLibrary.GetExport(Library, name);

    public static readonly delegate* unmanaged<byte*, nint> OpenDisplay = (delegate* unmanaged<byte*, nint>)Export("XOpenDisplay");
    public static readonly delegate* unmanaged<nint, int> CloseDisplay = (delegate* unmanaged<nint, int>)Export("XCloseDisplay");
    public static readonly delegate* unmanaged<nint, nint> DefaultRootWindow = (delegate* unmanaged<nint, nint>)Export("XDefaultRootWindow");
    public static readonly delegate* unmanaged<nint, byte*, int, nint> InternAtom = (delegate* unmanaged<nint, byte*, int, nint>)Export("XInternAtom");
    public static readonly delegate* unmanaged<nint, nint, byte*> GetAtomName = (delegate* unmanaged<nint, nint, byte*>)Export("XGetAtomName");

    public static readonly delegate* unmanaged<nint, nint, nint, nint, nint, int, nint,
        nint*, int*, nuint*, nuint*, byte**, int> GetWindowProperty =
        (delegate* unmanaged<nint, nint, nint, nint, nint, int, nint, nint*, int*, nuint*, nuint*, byte**, int>)Export("XGetWindowProperty");

    public static readonly delegate* unmanaged<nint, nint, nint, nint, int, int, byte*, int, int> ChangeProperty =
        (delegate* unmanaged<nint, nint, nint, nint, int, int, byte*, int, int>)Export("XChangeProperty");

    public static readonly delegate* unmanaged<nint, nint, nint, int> DeleteProperty =
        (delegate* unmanaged<nint, nint, nint, int>)Export("XDeleteProperty");

    public static readonly delegate* unmanaged<nint, byte*, int*, int*, int*, int> QueryExtension =
        (delegate* unmanaged<nint, byte*, int*, int*, int*, int>)Export("XQueryExtension");

    public static readonly delegate* unmanaged<nint, nint, XWindowAttributes*, int> GetWindowAttributes =
        (delegate* unmanaged<nint, nint, XWindowAttributes*, int>)Export("XGetWindowAttributes");

    public static readonly delegate* unmanaged<nint, nint, nint*, nint*, nint**, uint*, int> QueryTree =
        (delegate* unmanaged<nint, nint, nint*, nint*, nint**, uint*, int>)Export("XQueryTree");

    public static readonly delegate* unmanaged<nint, nint, nint, int, int, int*, int*, nint*, int> TranslateCoordinates =
        (delegate* unmanaged<nint, nint, nint, int, int, int*, int*, nint*, int>)Export("XTranslateCoordinates");

    public static readonly delegate* unmanaged<nint, nint, nint*, int*, int*, uint*, uint*, uint*, uint*, int> GetGeometry =
        (delegate* unmanaged<nint, nint, nint*, int*, int*, uint*, uint*, uint*, uint*, int>)Export("XGetGeometry");

    public static readonly delegate* unmanaged<nint, nint, int, int, uint, uint, int> MoveResizeWindow =
        (delegate* unmanaged<nint, nint, int, int, uint, uint, int>)Export("XMoveResizeWindow");

    public static readonly delegate* unmanaged<nint, int, int> Sync = (delegate* unmanaged<nint, int, int>)Export("XSync");
    public static readonly delegate* unmanaged<void*, int> Free = (delegate* unmanaged<void*, int>)Export("XFree");

    public static readonly delegate* unmanaged<nint, nint, int, int, uint, uint, uint, nuint, nuint, nint> CreateSimpleWindow =
        (delegate* unmanaged<nint, nint, int, int, uint, uint, uint, nuint, nuint, nint>)Export("XCreateSimpleWindow");

    public static readonly delegate* unmanaged<nint, nint, int> MapWindow = (delegate* unmanaged<nint, nint, int>)Export("XMapWindow");
    public static readonly delegate* unmanaged<nint, nint, int> DestroyWindow = (delegate* unmanaged<nint, nint, int>)Export("XDestroyWindow");
    public static readonly delegate* unmanaged<nint, nint, nint, int, int, int> ReparentWindow =
        (delegate* unmanaged<nint, nint, nint, int, int, int>)Export("XReparentWindow");

    public static readonly delegate* unmanaged<nint, nint, nuint, XSetWindowAttributes*, int> ChangeWindowAttributes =
        (delegate* unmanaged<nint, nint, nuint, XSetWindowAttributes*, int>)Export("XChangeWindowAttributes");
}

/// <summary>libXrandr 1.5 entry points, same dlopen + function-pointer approach.</summary>
internal static unsafe class Xrandr
{
    private static readonly nint Library = NativeLibrary.Load("libXrandr.so.2");

    private static void* Export(string name) => (void*)NativeLibrary.GetExport(Library, name);

    // Doubles as the "is RANDR there" probe: returns 0 when the extension is disabled.
    public static readonly delegate* unmanaged<nint, int*, int*, int> QueryVersion =
        (delegate* unmanaged<nint, int*, int*, int>)Export("XRRQueryVersion");

    public static readonly delegate* unmanaged<nint, nint, int, int*, XRRMonitorInfo*> GetMonitors =
        (delegate* unmanaged<nint, nint, int, int*, XRRMonitorInfo*>)Export("XRRGetMonitors");

    public static readonly delegate* unmanaged<XRRMonitorInfo*, void> FreeMonitors =
        (delegate* unmanaged<XRRMonitorInfo*, void>)Export("XRRFreeMonitors");
}
