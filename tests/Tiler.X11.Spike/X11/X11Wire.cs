using System.Runtime.InteropServices;
using System.Text;

namespace Tiler.X11;

/// <summary>
/// One open X display plus the thin property/geometry helpers the backend and the EWMH
/// fake share. Everything is synchronous: a spike needs no async and Xlib is blocking.
/// </summary>
internal sealed unsafe class X11Wire : IDisposable
{
    private readonly Dictionary<string, nint> _atoms = [];

    private X11Wire(nint display, nint root, string displayName)
    {
        Display = display;
        Root = root;
        DisplayName = displayName;
    }

    public nint Display { get; }

    public nint Root { get; }

    public string DisplayName { get; }

    public static X11Wire Open(string? displayName = null)
    {
        nint display;
        if (displayName is null)
        {
            display = Xlib.OpenDisplay(null);
        }
        else
        {
            var name = Marshal.StringToCoTaskMemUTF8(displayName);
            try
            {
                display = Xlib.OpenDisplay((byte*)name);
            }
            finally
            {
                Marshal.FreeCoTaskMem(name);
            }
        }

        if (display == 0)
        {
            throw new BackendException(BackendError.NoDisplay,
                $"no X display at '{displayName ?? Environment.GetEnvironmentVariable("DISPLAY")}': XOpenDisplay failed; run under X11 or xvfb-run -a");
        }

        return new X11Wire(display, Xlib.DefaultRootWindow(display), displayName ?? Environment.GetEnvironmentVariable("DISPLAY") ?? "");
    }

    public void Dispose() => Xlib.CloseDisplay(Display);

    public void Sync() => Xlib.Sync(Display, 0);

    public nint Atom(string name)
    {
        if (_atoms.TryGetValue(name, out var atom))
        {
            return atom;
        }

        using var encoded = new Utf8String(name);
        atom = Xlib.InternAtom(Display, encoded.Pointer, 0);
        _atoms[name] = atom;
        return atom;
    }

    public string AtomName(nint atom)
    {
        var name = Xlib.GetAtomName(Display, atom);
        if (name is null)
        {
            return "";
        }

        try
        {
            return Marshal.PtrToStringUTF8((nint)name) ?? "";
        }
        finally
        {
            Xlib.Free(name);
        }
    }

    /// <summary>Reads a format-32 property as the longs Xlib hands back on LP64.</summary>
    public long[] ReadLongs(nint window, nint property, nint requiredType)
    {
        nint actualType;
        int format;
        nuint count;
        nuint bytesAfter;
        byte* data;
        if (Xlib.GetWindowProperty(Display, window, property, 0, 1024, 0, requiredType,
                &actualType, &format, &count, &bytesAfter, &data) != Xlib.Success || data is null)
        {
            return [];
        }

        try
        {
            if (format != 32)
            {
                return [];
            }

            var values = new long[count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = BitConverter.ToInt64(new ReadOnlySpan<byte>(data + i * sizeof(long), sizeof(long)));
            }

            return values;
        }
        finally
        {
            Xlib.Free(data);
        }
    }

    public byte[] ReadBytes(nint window, nint property)
    {
        nint actualType;
        int format;
        nuint count;
        nuint bytesAfter;
        byte* data;
        if (Xlib.GetWindowProperty(Display, window, property, 0, 1024, 0, Xlib.AnyPropertyType,
                &actualType, &format, &count, &bytesAfter, &data) != Xlib.Success || data is null)
        {
            return [];
        }

        try
        {
            return format == 8 ? new ReadOnlySpan<byte>(data, (int)count).ToArray() : [];
        }
        finally
        {
            Xlib.Free(data);
        }
    }

    public void SetCardinals(nint window, string property, string type, params long[] values)
    {
        fixed (long* data = values)
        {
            Xlib.ChangeProperty(Display, window, Atom(property), Atom(type), 32, Xlib.PropModeReplace, (byte*)data, values.Length);
        }

        Sync();
    }

    public void SetUtf8String(nint window, string property, string value)
    {
        fixed (byte* data = Encoding.UTF8.GetBytes(value))
        {
            Xlib.ChangeProperty(Display, window, Atom(property), Atom("UTF8_STRING"), 8, Xlib.PropModeReplace, data, value.Length);
        }

        Sync();
    }

    public void DeleteProperty(nint window, string property)
    {
        Xlib.DeleteProperty(Display, window, Atom(property));
        Sync();
    }

    public XWindowAttributes Attributes(nint window)
    {
        XWindowAttributes attributes;
        if (Xlib.GetWindowAttributes(Display, window, &attributes) == 0)
        {
            throw new BackendException(BackendError.WindowNotTileable, $"XGetWindowAttributes failed for 0x{window:x}: window is gone");
        }

        return attributes;
    }

    public nint Parent(nint window)
    {
        nint root;
        nint parent;
        nint* children;
        uint count;
        if (Xlib.QueryTree(Display, window, &root, &parent, &children, &count) == 0)
        {
            throw new BackendException(BackendError.WindowNotTileable, $"XQueryTree failed for 0x{window:x}: window is gone");
        }

        if (children != null)
        {
            Xlib.Free(children);
        }

        return parent;
    }

    /// <summary>(0,0) of the window's inside area in root coordinates; border width is accounted for by the server.</summary>
    public (int X, int Y) TranslateToRoot(nint window)
    {
        int x;
        int y;
        nint child;
        if (Xlib.TranslateCoordinates(Display, window, Root, 0, 0, &x, &y, &child) == 0)
        {
            throw new BackendException(BackendError.ApplyFailed, $"XTranslateCoordinates failed for 0x{window:x}");
        }

        return (x, y);
    }

    public TileRect Geometry(nint window)
    {
        nint root;
        int x;
        int y;
        uint width;
        uint height;
        uint borderWidth;
        uint depth;
        if (Xlib.GetGeometry(Display, window, &root, &x, &y, &width, &height, &borderWidth, &depth) == 0)
        {
            throw new BackendException(BackendError.WindowNotTileable, $"XGetGeometry failed for 0x{window:x}: window is gone");
        }

        var origin = window == Root ? (X: 0, Y: 0) : TranslateToRoot(window);
        return new TileRect(origin.X, origin.Y, (int)width, (int)height);
    }

    public void MoveResize(nint window, int x, int y, uint width, uint height)
    {
        Xlib.MoveResizeWindow(Display, window, x, y, width, height);
        Sync();
    }
}

/// <summary>Unmanaged null-terminated UTF-8 bytes for the char* Xlib arguments.</summary>
internal readonly unsafe struct Utf8String : IDisposable
{
    private readonly nint _bytes;

    public Utf8String(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        _bytes = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, _bytes, bytes.Length);
    }

    public byte* Pointer => (byte*)_bytes;

    public void Dispose() => Marshal.FreeHGlobal(_bytes);
}
