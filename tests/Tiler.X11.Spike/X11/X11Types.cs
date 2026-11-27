using System.Runtime.InteropServices;

namespace Tiler.X11;

// Backend contract records from docs/architecture/backends.md, kept local to the spike:
// TL-203 moves them into the shipping backend.
internal record Monitor(uint Id, int X, int Y, int Width, int Height,
                        int WorkX, int WorkY, int WorkWidth, int WorkHeight, string Name);

internal record FocusResult(ulong WindowId, string Title, uint MonitorId,
                            int X, int Y, int Width, int Height);

internal record TileRect(int X, int Y, int Width, int Height);

internal enum BackendError
{
    NoDisplay,
    NoFocusedWindow,
    WindowNotTileable,
    ApplyFailed,
}

// Exit codes are the CLI contract (docs/features/cli.md); the spike reuses them.
internal sealed class BackendException(BackendError error, string message) : Exception(message)
{
    public BackendError Error { get; } = error;

    public int ExitCode => Error switch
    {
        BackendError.NoFocusedWindow => 2,
        BackendError.NoDisplay => 3,
        BackendError.WindowNotTileable => 4,
        _ => 1,
    };
}

// Must match Xlib's XWindowAttributes on LP64: offsets are asserted by the xwininfo
// cross-check in the tests (Width/Height/BorderWidth/OverrideRedirect read correctly).
[StructLayout(LayoutKind.Sequential)]
internal struct XWindowAttributes
{
    public int X, Y;
    public int Width, Height;
    public int BorderWidth;
    public int Depth;
    public nint Visual;
    public nint Root;
    public int Class;
    public int BitGravity, WinGravity, BackingStore;
    public nuint BackingPlanes, BackingPixel;
    public int SaveUnder;
    public nint Colormap;
    public int MapInstalled, MapState;
    public nint AllEventMasks, YourEventMask, DoNotPropagateMask;
    public int OverrideRedirect;
    public nint Screen;
}

// Must match Xlib's XSetWindowAttributes on LP64; only OverrideRedirect is used.
[StructLayout(LayoutKind.Sequential)]
internal struct XSetWindowAttributes
{
    public nint BackgroundPixmap;
    public nuint BackgroundPixel;
    public nint BorderPixmap;
    public nuint BorderPixel;
    public int BitGravity, WinGravity, BackingStore;
    public nuint BackingPlanes, BackingPixel;
    public int SaveUnder;
    public nint EventMask, DoNotPropagateMask;
    public int OverrideRedirect;
    public nint Colormap, Cursor;
}

// XRRMonitorInfo from libXrandr 1.5 (Xrandr.h).
[StructLayout(LayoutKind.Sequential)]
internal struct XRRMonitorInfo
{
    public nint Name;
    public int Primary;
    public int Automatic;
    public int NOutput;
    public int X, Y, Width, Height;
    public int MWidth, MHeight;
    public nint Outputs;
}
