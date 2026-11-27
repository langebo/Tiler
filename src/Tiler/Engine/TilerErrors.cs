namespace Tiler.Engine;

// Typed engine errors: the CLI renders them once as `tiler: <headline>: <detail>` and exits
// with ExitCode. Subclasses carry the raw context so tests and later callers need no parsing.
public abstract class TilerException(int exitCode, string headline, string detail)
    : Exception($"{headline}: {detail}")
{
    public int ExitCode { get; } = exitCode;
    public string Headline { get; } = headline;
    public string Detail { get; } = detail;
}

public sealed class NoBackend(string? sessionTypeValue) : TilerException(
    6,
    "no backend for this session",
    $"XDG_SESSION_TYPE is {VariableState.Describe(sessionTypeValue)}; set XDG_SESSION_TYPE to x11 or wayland")
{
    public string? SessionTypeValue { get; } = sessionTypeValue;
}

public sealed class NoDisplay(string? display) : TilerException(
    3,
    "no X11 display",
    $"DISPLAY is {VariableState.Describe(display)}; run tiler inside an X11 session")
{
    public string? Display { get; } = display;
}

public sealed class NoFocusedWindow(string? sessionType) : TilerException(
    2,
    "no focused window",
    $"{(sessionType is null or "" ? "unknown" : sessionType)} session reported no focused window")
{
    public string? SessionType { get; } = sessionType;
}

public sealed class WindowNotTileable(ulong windowId, string reason) : TilerException(
    4,
    "window not tileable",
    $"window {windowId}: {reason}")
{
    public ulong WindowId { get; } = windowId;
    public string Reason { get; } = reason;
}

public sealed class HelperUnavailable(string busName, string remediation) : TilerException(
    3,
    "tiler helper not active",
    $"D-Bus name {busName} is not owned; {remediation}")
{
    public string BusName { get; } = busName;
    public string Remediation { get; } = remediation;
}

public sealed class ApplyFailed(ulong windowId, TileRect rect, string reason) : TilerException(
    4,
    "window geometry rejected",
    $"window {windowId} at x={rect.X} y={rect.Y} {rect.Width}x{rect.Height}: {reason}")
{
    public ulong WindowId { get; } = windowId;
    public TileRect Rect { get; } = rect;
    public string Reason { get; } = reason;
}

// unset, empty and set are three distinct environment states; collapsing two of them sends
// whoever debugs the exit code looking at the wrong cause.
file static class VariableState
{
    public static string Describe(string? value) => value switch
    {
        null => "unset",
        "" => "empty",
        _ => $"set to \"{value}\"",
    };
}
