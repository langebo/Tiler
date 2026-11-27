namespace Tiler.X11;

/// <summary>
/// Spike X11 backend: the five operations from docs/architecture/backends.md, in the shapes
/// the engine's IWindowBackend will need. Synchronous on purpose — TL-203 wraps it in the
/// Task-based interface; an AOT spike gains nothing from async.
/// </summary>
internal sealed unsafe class X11SpikeBackend(X11Wire wire)
{
    public string Name => "x11";

    /// <summary>"randr" when RRGetMonitors answered, "fallback" when the EWMH properties did.</summary>
    public string MonitorSource { get; private set; } = "unknown";

    /// <summary>Server RANDR version, or "none" when the extension is disabled (fallback path).</summary>
    public string RandRVersion { get; private set; } = "none";

    public IReadOnlyList<Monitor> GetMonitors()
    {
        if (ReadRandRMonitors() is { Count: > 0 } monitors)
        {
            MonitorSource = "randr";
            return monitors;
        }

        MonitorSource = "fallback";
        return ReadEwmhMonitors();
    }

    /// <summary>null means "no focusable window" (focus on root/None).</summary>
    public FocusResult? GetFocusedWindow()
    {
        var active = wire.ReadLongs(wire.Root, wire.Atom("_NET_ACTIVE_WINDOW"), wire.Atom("WINDOW"));
        if (active.Length == 0 || active[0] == 0)
        {
            return null;
        }

        var window = (nint)active[0];
        var rect = GetClientRect((ulong)window);
        return new FocusResult((ulong)window, Title(window), MonitorFor(GetMonitors(), rect), rect.X, rect.Y, rect.Width, rect.Height);
    }

    /// <summary>Client area (the part a tile covers) in screen-absolute coordinates.</summary>
    public TileRect GetClientRect(ulong windowId)
    {
        var window = (nint)windowId;
        var attributes = wire.Attributes(window);
        var (x, y) = wire.TranslateToRoot(window);
        return new TileRect(x, y, attributes.Width, attributes.Height);
    }

    /// <summary>
    /// The spec's <c>XGetWindowReparentData</c> is not a libX11 function; this is the same
    /// answer built from XQueryTree + XTranslateCoordinates: the WM frame's origin on root.
    /// </summary>
    public (int X, int Y) GetFrameOrigin(ulong windowId)
    {
        var window = (nint)windowId;
        var parent = wire.Parent(window);
        return wire.TranslateToRoot(parent == wire.Root ? window : parent);
    }

    /// <summary>Frame extents as (left top right bottom); all zero for a CSD window with no frame.</summary>
    public long[] GetFrameExtents(ulong windowId) =>
        wire.ReadLongs((nint)windowId, wire.Atom("_NET_FRAME_EXTENTS"), wire.Atom("CARDINAL"));

    public void EnsureTileable(ulong windowId)
    {
        if (wire.Attributes((nint)windowId).OverrideRedirect != 0)
        {
            throw new BackendException(BackendError.WindowNotTileable, $"window 0x{windowId:x} is override-redirect: not tileable");
        }
    }

    /// <summary>Places the client area exactly at <paramref name="rect"/>; absolute coordinates.</summary>
    public void ApplyRectangle(ulong windowId, TileRect rect)
    {
        EnsureTileable(windowId);
        var window = (nint)windowId;
        var extents = GetFrameExtents(windowId);
        if (extents.Length == 4 && (extents[0] | extents[1] | extents[2] | extents[3]) != 0)
        {
            var (left, top, right, bottom) = (extents[0], extents[1], extents[2], extents[3]);
            var frame = wire.Parent(window);
            wire.MoveResize(frame,
                rect.X - (int)left, rect.Y - (int)top,
                (uint)(rect.Width + left + right), (uint)(rect.Height + top + bottom));
            // shortcut: no window manager under Xvfb to resize the client after a frame resize;
            // a real WM does this itself, so set the client size explicitly too.
            wire.MoveResize(window, (int)left, (int)top, (uint)rect.Width, (uint)rect.Height);
            return;
        }

        wire.MoveResize(window, rect.X, rect.Y, (uint)rect.Width, (uint)rect.Height);
    }

    private List<Monitor>? ReadRandRMonitors()
    {
        RandRVersion = "none";
        // XQueryExtension is the silent probe; XRRQueryVersion warns on stderr when RANDR is out.
        using var extension = new Utf8String("RANDR");
        int opcode;
        int firstEvent;
        int firstError;
        if (Xlib.QueryExtension(wire.Display, extension.Pointer, &opcode, &firstEvent, &firstError) == 0)
        {
            return null;
        }

        // RRGetMonitors needs Xrandr >= 1.4.
        int major;
        int minor;
        if (Xrandr.QueryVersion(wire.Display, &major, &minor) == 0 || major * 100 + minor < 104)
        {
            return null;
        }

        RandRVersion = $"{major}.{minor}";
        int count;
        var info = Xrandr.GetMonitors(wire.Display, wire.Root, 1, &count);
        if (info is null || count <= 0)
        {
            return null;
        }

        try
        {
            var screenWorkArea = ReadWorkArea();
            var monitors = new List<Monitor>(count);
            for (var i = 0; i < count; i++)
            {
                var monitor = info[i];
                var geometry = new TileRect(monitor.X, monitor.Y, monitor.Width, monitor.Height);
                // RANDR reports monitor geometry, not an EWMH work area: the work area is that
                // geometry clipped to _NET_WORKAREA (what GDK does); no property -> full monitor.
                var work = Intersect(geometry, screenWorkArea) ?? geometry;
                monitors.Add(new Monitor((uint)monitor.Name, monitor.X, monitor.Y, monitor.Width, monitor.Height,
                    work.X, work.Y, work.Width, work.Height, wire.AtomName(monitor.Name)));
            }

            return monitors;
        }
        finally
        {
            Xrandr.FreeMonitors(info);
        }
    }

    private List<Monitor> ReadEwmhMonitors()
    {
        var desktopWorkArea = ReadWorkArea();
        var monitors = new List<Monitor>();

        // shortcut: _NET_LIST_OF_MONITORS is a draft EWMH property with no published layout
        // and no producer in a bare Xvfb; read flat CARDINAL rects (x, y, width, height) each.
        var list = wire.ReadLongs(wire.Root, wire.Atom("_NET_LIST_OF_MONITORS"), Xlib.AnyPropertyType);
        for (var i = 0; i + 3 < list.Length; i += 4)
        {
            var geometry = new TileRect((int)list[i], (int)list[i + 1], (int)list[i + 2], (int)list[i + 3]);
            // Documented degradation: _NET_WORKAREA is per screen, so only the first monitor
            // gets it; every other monitor's work area is its full geometry.
            var work = monitors.Count == 0 ? Intersect(geometry, desktopWorkArea) ?? geometry : geometry;
            monitors.Add(new Monitor((uint)monitors.Count, geometry.X, geometry.Y, geometry.Width, geometry.Height,
                work.X, work.Y, work.Width, work.Height, $"monitor{monitors.Count}"));
        }

        if (monitors.Count > 0)
        {
            return monitors;
        }

        var root = wire.Geometry(wire.Root);
        var rootWork = Intersect(root, desktopWorkArea) ?? root;
        return [new Monitor(0, root.X, root.Y, root.Width, root.Height, rootWork.X, rootWork.Y, rootWork.Width, rootWork.Height, "root")];
    }

    private TileRect? ReadWorkArea()
    {
        // _NET_WORKAREA holds one rect per desktop; the spike uses desktop 0.
        var values = wire.ReadLongs(wire.Root, wire.Atom("_NET_WORKAREA"), wire.Atom("CARDINAL"));
        return values.Length >= 4
            ? new TileRect((int)values[0], (int)values[1], (int)values[2], (int)values[3])
            : null;
    }

    private string Title(nint window)
    {
        var utf8 = wire.ReadBytes(window, wire.Atom("_NET_WM_NAME"));
        if (utf8.Length > 0)
        {
            return System.Text.Encoding.UTF8.GetString(utf8);
        }

        // WM_NAME is ICCCM compound text/latin-1; good enough for a spike title.
        return System.Text.Encoding.Latin1.GetString(wire.ReadBytes(window, wire.Atom("WM_NAME")));
    }

    private static uint MonitorFor(IReadOnlyList<Monitor> monitors, TileRect rect)
    {
        var (centerX, centerY) = (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        foreach (var monitor in monitors)
        {
            if (centerX >= monitor.X && centerX < monitor.X + monitor.Width &&
                centerY >= monitor.Y && centerY < monitor.Y + monitor.Height)
            {
                return monitor.Id;
            }
        }

        return monitors.Count > 0 ? monitors[0].Id : 0;
    }

    private static TileRect? Intersect(TileRect a, TileRect? b)
    {
        if (b is not { } other)
        {
            return null;
        }

        var x = Math.Max(a.X, other.X);
        var y = Math.Max(a.Y, other.Y);
        var right = Math.Min(a.X + a.Width, other.X + other.Width);
        var bottom = Math.Min(a.Y + a.Height, other.Y + other.Height);
        return right > x && bottom > y ? new TileRect(x, y, right - x, bottom - y) : null;
    }
}
