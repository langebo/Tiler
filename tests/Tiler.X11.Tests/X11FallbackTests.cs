using Xunit;

namespace Tiler.X11.Tests;

/// <summary>
/// The documented RANDR degradation: with the extension disabled the backend must fall back to
/// _NET_LIST_OF_MONITORS + _NET_WORKAREA. Runs on a private RANDR-disabled Xvfb, so it needs no
/// shared display and no WM.
/// </summary>
public sealed class X11FallbackTests
{
    [XvfbFact]
    public void MonitorsComeFromEwmhPropertiesWhenRandRIsDisabled()
    {
        using var server = XvfbServer.Start(1280, 1024, "-extension", "RANDR");
        using var wire = TestDisplay.Open(server.Display);
        var backend = new X11SpikeBackend(wire);

        // xrandr agrees the server has no RANDR: it fails on this display.
        Assert.Throws<InvalidOperationException>(() => X11Oracles.XrandrMonitors(server.Display));

        // No EWMH monitor list either: the whole root window is the one monitor.
        var bare = Assert.Single(backend.GetMonitors());
        Assert.Equal("fallback", backend.MonitorSource);
        Assert.Equal(new TileRect(0, 0, 1280, 1024),
            new TileRect(bare.X, bare.Y, bare.Width, bare.Height));

        // The EWMH fake a WM would write: two monitors, work area of the primary clipped by a
        // 40px top bar. Only the primary screen has a work area — the documented degradation.
        wire.SetCardinals(wire.Root, "_NET_LIST_OF_MONITORS", "CARDINAL",
            0, 0, 1280, 1024,
            1280, 0, 1280, 1024);
        wire.SetCardinals(wire.Root, "_NET_WORKAREA", "CARDINAL", 0, 40, 1280, 984);

        var monitors = backend.GetMonitors();
        Assert.Equal(2, monitors.Count);
        Assert.Equal(40, monitors[0].WorkY);
        Assert.Equal(984, monitors[0].WorkHeight);
        Assert.Equal(0, monitors[1].WorkY);
        Assert.Equal(1024, monitors[1].WorkHeight);
        Assert.Equal(1280, monitors[1].X);
    }
}
