namespace Tiler.Engine;

// Verbatim from docs/architecture/backends.md ## Contract; coordinates are screen-absolute.
public record Monitor(uint Id, int X, int Y, int Width, int Height,
                      int WorkX, int WorkY, int WorkWidth, int WorkHeight, string Name);

public record FocusResult(ulong WindowId, string Title, uint MonitorId,
                          int X, int Y, int Width, int Height);

public record TileRect(int X, int Y, int Width, int Height);  // absolute, client-area coords

public record HealthStatus(bool Ok, string Detail);
