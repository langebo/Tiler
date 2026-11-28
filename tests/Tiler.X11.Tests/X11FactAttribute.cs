using Xunit;

namespace Tiler.X11.Tests;

/// <summary>
/// Runs on the display in $DISPLAY, which must be a bare Xvfb: the fixture plays the window
/// manager itself (EWMH fake, no reparenting), so a display with a real WM would just fight it.
/// Skips when there is no display or it has a window manager, so a bare `dotnet test` on a
/// desktop still passes.
/// </summary>
public sealed class X11FactAttribute : FactAttribute
{
    public X11FactAttribute()
    {
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        if (string.IsNullOrEmpty(display))
        {
            Skip = "No X display: run the X11 tests under xvfb-run -a dotnet test.";
        }
        else if (WindowManagerPresent(display))
        {
            Skip = $"Display {display} has a window manager; these tests need a bare Xvfb: xvfb-run -a dotnet test.";
        }
    }

    private static bool WindowManagerPresent(string display)
    {
        try
        {
            using var wire = TestDisplay.Open(display);
            return wire.ReadLongs(wire.Root, wire.Atom("_NET_SUPPORTING_WM_CHECK"), Xlib.AnyPropertyType).Length > 0;
        }
        catch (BackendException)
        {
            // A display we cannot reach is not a reason to skip: the test body reports it as a
            // failure, and a skip here would turn a broken display into a green run.
            return false;
        }
    }
}

/// <summary>For tests that bring their own Xvfb and only need the binary to be installed.</summary>
public sealed class XvfbFactAttribute : FactAttribute
{
    public XvfbFactAttribute()
    {
        if (!XvfbServer.IsAvailable)
        {
            Skip = "Xvfb is not installed; skipping the RANDR-disabled X11 test.";
        }
    }
}
