using System.Text.Json;
using Tiler.DBus;
using Tmds.DBus.Protocol;
using Tmds.DBus.SourceGenerator;

namespace Tiler;

public static class Repositioner
{
    private const string SERVICE_NAME = "org.gnome.Shell";
    private const string OBJECT_PATH = "/org/gnome/Shell/Extensions/Windows";
    private const int GNOME_MENU_HEIGHT = 32;
    private const int VISIBLE_SCREEN_HEIGHT = 1600 - GNOME_MENU_HEIGHT;
    private const int SCREEN_WIDTH = 3840;

    internal record PositionInfo(int X, uint Width);

    public static async Task RepositionFocusedWindowAsync(Position position)
    {
        var windowManager = new OrgGnomeShellExtensionsWindows(Connection.Session, SERVICE_NAME, OBJECT_PATH);
        var windowsJson = await windowManager.ListAsync().ConfigureAwait(false);

        var windows = JsonSerializer.Deserialize(windowsJson, DbusJsonContext.Default.IEnumerableWindowInfo);

        if (windows is not null && windows.Any(w => w.IsFocused))
        {
            var focusedWindow = windows.First(w => w.IsFocused);
            var positionInfo = CalculatePosition(position);
            await windowManager.MoveResizeAsync(focusedWindow.WindowId, positionInfo.X, GNOME_MENU_HEIGHT, positionInfo.Width, VISIBLE_SCREEN_HEIGHT).ConfigureAwait(false);
        }
    }

    internal static PositionInfo CalculatePosition(Position position) => position switch
    {
        Position.Full => new(0, SCREEN_WIDTH),
        Position.LeftHalf => new(0, SCREEN_WIDTH / 2),
        Position.MiddleHalf => new(SCREEN_WIDTH / 4, SCREEN_WIDTH / 2),
        Position.RightHalf => new(SCREEN_WIDTH / 2, SCREEN_WIDTH / 2),
        Position.LeftThird => new(0, SCREEN_WIDTH / 3),
        Position.MiddleThird => new(SCREEN_WIDTH / 3, SCREEN_WIDTH / 3),
        Position.RightThird => new((int)(SCREEN_WIDTH / 1.5), SCREEN_WIDTH / 3),
        Position.LeftTwoThirds => new(0, (int)(SCREEN_WIDTH / 1.5)),
        Position.RightTwoThirds => new(SCREEN_WIDTH / 3, (int)(SCREEN_WIDTH / 1.5)),
        Position.LeftQuarter => new(0, SCREEN_WIDTH / 4),
        Position.RightQuarter => new((int)(SCREEN_WIDTH * 0.75), SCREEN_WIDTH / 4),
        _ => throw new ArgumentOutOfRangeException(nameof(position))
    };
}