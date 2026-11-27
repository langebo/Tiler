using Xunit;

namespace Tiler.X11.Tests;

/// <summary>
/// The X11 capability surface from docs/architecture/backends.md against the display in
/// $DISPLAY (CI: xvfb-run). Assertions read the values back with xwininfo/xrandr where the
/// backend cannot vouch for itself; the EWMH fake from the testing seams stands in for a WM.
/// </summary>
public sealed class X11BackendTests : IDisposable
{
    private readonly X11Wire? _wire;
    private readonly X11SpikeBackend? _backend;
    private readonly X11Windows? _windows;

    public X11BackendTests()
    {
        if (Environment.GetEnvironmentVariable("DISPLAY") is null)
        {
            return;
        }

        _wire = X11Wire.Open();
        _backend = new X11SpikeBackend(_wire);
        _windows = new X11Windows(_wire);
        ClearRootProperties();
    }

    // A previous test class or a stray WM on the display must not leak EWMH state into this one.
    public void Dispose()
    {
        if (_wire is not null)
        {
            ClearRootProperties();
        }

        _windows?.Dispose();
        _wire?.Dispose();
    }

    [X11Fact]
    public void FocusReadsNetActiveWindowAndReportsNoneWhenItIsUnset()
    {
        var window = Windows.CreatePlain(80, 60, 640, 480);
        Windows.SetTitle(window, "TL-105 CSD window");

        Windows.SetActiveWindow(0);
        Assert.Null(Backend.GetFocusedWindow());

        Windows.SetActiveWindow(window);
        var focused = Backend.GetFocusedWindow();
        Assert.NotNull(focused);
        Assert.Equal((ulong)window, focused.WindowId);
        Assert.Equal("TL-105 CSD window", focused.Title);
        Assert.Equal(new TileRect(80, 60, 640, 480), new TileRect(focused.X, focused.Y, focused.Width, focused.Height));
        Assert.Contains(Backend.GetMonitors(), monitor => monitor.Id == focused.MonitorId);
    }

    [X11Fact]
    public void CsdWindowGeometryMatchesXwininfo()
    {
        var window = Windows.CreatePlain(120, 80, 500, 400);

        var rect = Backend.GetClientRect((ulong)window);
        Assert.Equal(new TileRect(120, 80, 500, 400), rect);
        // No frame to derive: a CSD window tiles by its own geometry.
        Assert.Equal((120, 80), Backend.GetFrameOrigin((ulong)window));

        var oracle = X11Oracles.Xwininfo((ulong)window, Wire.DisplayName);
        Assert.Equal(oracle.X, rect.X);
        Assert.Equal(oracle.Y, rect.Y);
        Assert.Equal(oracle.Width, rect.Width);
        Assert.Equal(oracle.Height, rect.Height);
        Assert.Equal(0, oracle.BorderWidth);
    }

    [X11Fact]
    public void FramedWindowDerivesClientAreaFromFrameOriginAndFrameExtents()
    {
        var (frame, client) = Windows.CreateFramed(200, 150, 600, 400, left: 4, top: 20, right: 6, bottom: 8);

        // The fixture put the client at (200+4, 150+20) inside its frame.
        var rect = Backend.GetClientRect((ulong)client);
        Assert.Equal(new TileRect(204, 170, 600, 400), rect);
        Assert.Equal((200, 150), Backend.GetFrameOrigin((ulong)client));
        Assert.Equal(frame, Wire.Parent(client));
        Assert.Equal(new long[] { 4, 20, 6, 8 }, Backend.GetFrameExtents((ulong)client));

        var oracle = X11Oracles.Xwininfo((ulong)client, Wire.DisplayName);
        Assert.Equal(oracle.X, rect.X);
        Assert.Equal(oracle.Y, rect.Y);
        Assert.Equal(oracle.Width, rect.Width);
        Assert.Equal(oracle.Height, rect.Height);
    }

    [X11Fact]
    public void ApplyPlacesTheClientAreaExactlyForCsdAndFramedWindows()
    {
        var requested = new TileRect(320, 240, 800, 600);

        var csd = Windows.CreatePlain(10, 10, 300, 200);
        Backend.ApplyRectangle((ulong)csd, requested);
        Assert.Equal(requested, Backend.GetClientRect((ulong)csd));

        var (_, framed) = Windows.CreateFramed(50, 50, 300, 200, left: 4, top: 20, right: 6, bottom: 8);
        Backend.ApplyRectangle((ulong)framed, requested);
        Assert.Equal(requested, Backend.GetClientRect((ulong)framed));
        // The frame moved to the client rect minus its extents, not to the client rect.
        Assert.Equal((requested.X - 4, requested.Y - 20), Backend.GetFrameOrigin((ulong)framed));

        var oracle = X11Oracles.Xwininfo((ulong)framed, Wire.DisplayName);
        Assert.Equal(requested.X, oracle.X);
        Assert.Equal(requested.Y, oracle.Y);
        Assert.Equal(requested.Width, oracle.Width);
        Assert.Equal(requested.Height, oracle.Height);
    }

    [X11Fact]
    public void RandRMonitorsMatchXrandr()
    {
        var monitors = Backend.GetMonitors();

        Assert.Equal("randr", Backend.MonitorSource);
        var oracle = X11Oracles.XrandrMonitors(Wire.DisplayName);
        Assert.Equal(oracle.Count, monitors.Count);
        foreach (var reported in oracle)
        {
            var monitor = Assert.Single(monitors, candidate => candidate.Name == reported.Name);
            Assert.Equal(reported.X, monitor.X);
            Assert.Equal(reported.Y, monitor.Y);
            Assert.Equal(reported.Width, monitor.Width);
            Assert.Equal(reported.Height, monitor.Height);
        }
    }

    [X11Fact]
    public void RandRWorkAreaIsTheMonitorClippedToNetWorkArea()
    {
        var monitor = Assert.Single(Backend.GetMonitors());

        // A top bar: _NET_WORKAREA is the EWMH way to say which pixels a tile may use.
        Wire.SetCardinals(Wire.Root, "_NET_WORKAREA", "CARDINAL", 0, 32, monitor.Width, monitor.Height - 32);

        var clipped = Assert.Single(Backend.GetMonitors());
        Assert.Equal(monitor.X, clipped.X);
        Assert.Equal(monitor.Width, clipped.Width);
        Assert.Equal(32, clipped.WorkY);
        Assert.Equal(monitor.Height - 32, clipped.WorkHeight);

        Wire.DeleteProperty(Wire.Root, "_NET_WORKAREA");
    }

    [X11Fact]
    public void OverrideRedirectWindowIsNotTileable()
    {
        var popup = Windows.CreateOverrideRedirect(0, 0, 200, 100);

        var error = Assert.Throws<BackendException>(() => Backend.EnsureTileable((ulong)popup));
        Assert.Equal(BackendError.WindowNotTileable, error.Error);
        Assert.Equal(4, error.ExitCode);
    }

    private X11Wire Wire => _wire!;

    private X11SpikeBackend Backend => _backend!;

    private X11Windows Windows => _windows!;

    private void ClearRootProperties()
    {
        foreach (var property in new[] { "_NET_ACTIVE_WINDOW", "_NET_WORKAREA", "_NET_LIST_OF_MONITORS" })
        {
            _wire!.DeleteProperty(_wire.Root, property);
        }
    }
}
