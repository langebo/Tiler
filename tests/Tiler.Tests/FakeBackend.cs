using Tiler.Backends;
using Tiler.Engine;
using Monitor = Tiler.Engine.Monitor;

namespace Tiler.Tests;

internal sealed class FakeBackend : IWindowBackend
{
    public IReadOnlyList<Monitor> Monitors { get; set; } = [];
    public FocusResult? Focus { get; set; }
    public HealthStatus Health { get; set; } = new(true, "fake");
    public List<(ulong WindowId, TileRect Rect)> Applied { get; } = [];

    public string Name => "fake";

    public Task<IReadOnlyList<Monitor>> GetMonitorsAsync() => Task.FromResult(Monitors);

    public Task<FocusResult?> GetFocusedWindowAsync() => Task.FromResult(Focus);

    public Task ApplyRectangleAsync(ulong windowId, TileRect rect)
    {
        Applied.Add((windowId, rect));
        return Task.CompletedTask;
    }

    public Task<HealthStatus> CheckAsync() => Task.FromResult(Health);
}
