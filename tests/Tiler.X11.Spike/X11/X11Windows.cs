namespace Tiler.X11;

/// <summary>
/// The "tiny EWMH fake" from docs/architecture/backends.md, in Xlib instead of xdotool so no
/// window manager is needed: create plain/CSD-ish, framed and override-redirect windows, and
/// write the root properties a WM would write.
/// </summary>
internal sealed unsafe class X11Windows(X11Wire wire) : IDisposable
{
    private readonly List<nint> _created = [];

    /// <summary>A CSD-ish window: mapped straight onto root, border width 0, no frame extents.</summary>
    public nint CreatePlain(int x, int y, uint width, uint height)
    {
        var window = Create(x, y, width, height);
        Xlib.MapWindow(wire.Display, window);
        wire.Sync();
        return window;
    }

    public nint CreateOverrideRedirect(int x, int y, uint width, uint height)
    {
        var window = Create(x, y, width, height);
        var attributes = new XSetWindowAttributes { OverrideRedirect = 1 };
        Xlib.ChangeWindowAttributes(wire.Display, window, Xlib.CWOverrideRedirect, &attributes);
        Xlib.MapWindow(wire.Display, window);
        wire.Sync();
        return window;
    }

    /// <summary>
    /// A WM-style reparenting frame: the client is a child of the frame at (left, top) and
    /// carries _NET_FRAME_EXTENTS, which is how a decorated window looks to EWMH clients.
    /// </summary>
    public (nint Frame, nint Client) CreateFramed(int x, int y, uint width, uint height, int left, int top, int right, int bottom)
    {
        var client = Create(0, 0, width, height);
        var frame = Create(x, y, width + (uint)(left + right), height + (uint)(top + bottom));
        Xlib.ReparentWindow(wire.Display, client, frame, left, top);
        Xlib.MapWindow(wire.Display, client);
        Xlib.MapWindow(wire.Display, frame);
        wire.Sync();
        wire.SetCardinals(client, "_NET_FRAME_EXTENTS", "CARDINAL", left, top, right, bottom);
        return (frame, client);
    }

    public void SetActiveWindow(nint window) =>
        wire.SetCardinals(wire.Root, "_NET_ACTIVE_WINDOW", "WINDOW", window);

    public void SetTitle(nint window, string title) => wire.SetUtf8String(window, "_NET_WM_NAME", title);

    public void Dispose()
    {
        foreach (var window in _created)
        {
            Xlib.DestroyWindow(wire.Display, window);
        }

        _created.Clear();
        wire.Sync();
    }

    private nint Create(int x, int y, uint width, uint height)
    {
        var window = Xlib.CreateSimpleWindow(wire.Display, wire.Root, x, y, width, height, 0, 0, 0);
        _created.Add(window);
        return window;
    }
}
