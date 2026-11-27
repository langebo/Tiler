using System.Text.Json;
using Tmds.DBus.Protocol;
using Tmds.DBus.SourceGenerator;

const string SERVICE_NAME = "dev.blange.Tiler";
const string OBJECT_PATH = "/dev/blange/Tiler";

try
{
    var tiler = new DevBlangeTiler(Connection.Session, SERVICE_NAME, OBJECT_PATH);

    var (windowId, title) = await tiler.GetFocusedAsync();
    if (windowId == 0)
        throw new InvalidOperationException("no focused window — focus an ordinary window first");
    Console.WriteLine($"FOCUSED ok id={windowId} title={title}");

    using var workarea = JsonDocument.Parse(await tiler.GetWorkareaAsync());
    var work = workarea.RootElement;
    var (x, y, w, h) = (work.GetProperty("x").GetInt32(), work.GetProperty("y").GetInt32(),
        work.GetProperty("w").GetInt32(), work.GetProperty("h").GetInt32());
    // Bar-excluded means the work area is not the full screen: a top bar pushes y down.
    if (y <= 0)
        throw new InvalidOperationException($"work area starts at y={y}: top bar not excluded");
    Console.WriteLine($"WORKAREA ok x={x} y={y} w={w} h={h}");

    int halfWidth = w / 2;
    await tiler.MoveResizeAsync(windowId, x, y, (uint)halfWidth, (uint)h);
    Console.WriteLine($"TILED ok id={windowId} x={x} y={y} w={halfWidth} h={h}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"FAILED {exception.Message}");
    return 1;
}
