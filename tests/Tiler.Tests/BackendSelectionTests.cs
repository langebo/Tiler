using Tiler.Backends;
using Tiler.Engine;
using Xunit;
using Monitor = Tiler.Engine.Monitor;

namespace Tiler.Tests;

public class BackendSelectionTests
{
    [Theory]
    [InlineData("x11", BackendKind.X11)]
    [InlineData("X11", BackendKind.X11)]
    [InlineData("  x11\t", BackendKind.X11)]
    [InlineData("wayland", BackendKind.Wayland)]
    [InlineData("WayLand", BackendKind.Wayland)]
    [InlineData(" wayland\n", BackendKind.Wayland)]
    public void ResolveMapsSessionTypeToKind(string sessionType, BackendKind expected) =>
        Assert.Equal(expected, SessionDetector.Resolve(sessionType));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mir")]
    [InlineData("tty")]
    [InlineData("x11wayland")]
    public void ResolveRejectsUnsupportedSessionType(string? sessionType) =>
        Assert.Null(SessionDetector.Resolve(sessionType));

    [Fact]
    public void FactoryReturnsProviderOfDetectedKind()
    {
        var x11 = new FakeBackend();
        var wayland = new FakeBackend();
        var factory = new BackendFactory(() => x11, () => wayland);

        Assert.Same(x11, factory.Create("x11"));
        Assert.Same(wayland, factory.Create("wayland"));
    }

    [Fact]
    public void FactoryThrowsNoBackendForUnsetSession()
    {
        var error = Assert.Throws<NoBackend>(() => new BackendFactory(() => new FakeBackend(), () => new FakeBackend()).Create(null));

        Assert.Equal(6, error.ExitCode);
        Assert.Null(error.SessionTypeValue);
        Assert.Contains("XDG_SESSION_TYPE is unset", error.Detail);
    }

    [Fact]
    public void FactoryThrowsNoBackendForUnknownSession()
    {
        var error = Assert.Throws<NoBackend>(() => new BackendFactory(() => new FakeBackend(), () => new FakeBackend()).Create(" mir "));

        Assert.Equal(6, error.ExitCode);
        Assert.Equal(" mir ", error.SessionTypeValue);
        Assert.Contains("\" mir \"", error.Detail);
    }

    [Fact]
    public void FactoryThrowsNoBackendWhenKindHasNoProvider()
    {
        var error = Assert.Throws<NoBackend>(() => new BackendFactory(wayland: () => new FakeBackend()).Create("x11"));

        Assert.Equal(6, error.ExitCode);
        Assert.Equal("x11", error.SessionTypeValue);
    }

    [Fact]
    public void ResolveIgnoresDisplayVariable()
    {
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        try
        {
            Environment.SetEnvironmentVariable("DISPLAY", ":0");
            Assert.Equal(BackendKind.Wayland, SessionDetector.Resolve("wayland"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", display);
        }
    }

    [Fact]
    public async Task FakeBackendRoundTripsModelThroughInterface()
    {
        var monitor = new Monitor(1, 0, 0, 1920, 1080, 0, 32, 1920, 1048, "DP-1");
        var backend = new FakeBackend
        {
            Monitors = [monitor],
            Focus = new FocusResult(4242, "title", 1, 0, 32, 800, 600),
        };

        Assert.Equal("fake", backend.Name);
        Assert.Equal(monitor, Assert.Single(await backend.GetMonitorsAsync()));
        Assert.Equal(4242UL, (await backend.GetFocusedWindowAsync())!.WindowId);
        Assert.True((await backend.CheckAsync()).Ok);

        await backend.ApplyRectangleAsync(4242, new TileRect(0, 32, 960, 1048));
        Assert.Equal((4242UL, new TileRect(0, 32, 960, 1048)), Assert.Single(backend.Applied));
    }

    [Fact]
    public void TypedErrorsCarryTheirContext()
    {
        var notTileable = new WindowNotTileable(123, "override-redirect");
        Assert.Equal(4, notTileable.ExitCode);
        Assert.Equal(123UL, notTileable.WindowId);
        Assert.Equal("override-redirect", notTileable.Reason);

        Assert.Equal(3, new NoDisplay(null).ExitCode);
        Assert.Equal(2, new NoFocusedWindow("x11").ExitCode);
        Assert.Equal(3, new HelperUnavailable("dev.blange.Tiler", "run tiler install-helper").ExitCode);

        var apply = new ApplyFailed(7, new TileRect(1, 2, 3, 4), "min-size guard");
        Assert.Equal(4, apply.ExitCode);
        Assert.Equal(new TileRect(1, 2, 3, 4), apply.Rect);
        Assert.Equal("min-size guard", apply.Reason);
    }
}
