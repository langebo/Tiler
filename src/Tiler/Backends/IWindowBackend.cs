using Tiler.Engine;
using Monitor = Tiler.Engine.Monitor;

namespace Tiler.Backends;

// Verbatim from docs/architecture/backends.md ## Contract.
public interface IWindowBackend
{
    // Monitors with physical coords and work areas (work area = area minus bars/docks)
    Task<IReadOnlyList<Monitor>> GetMonitorsAsync();
    // Focused window; includes monitor id + client (unframed) geometry
    Task<FocusResult?> GetFocusedWindowAsync();   // null → no focusable window
    Task ApplyRectangleAsync(ulong windowId, TileRect rect);  // absolute coords, client area
    string Name { get; }  // "x11" | "wayland"
    Task<HealthStatus> CheckAsync();  // for `tiler doctor`
}
