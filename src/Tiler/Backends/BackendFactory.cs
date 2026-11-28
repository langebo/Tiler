using Tiler.Engine;

namespace Tiler.Backends;

public enum BackendKind
{
    X11,
    Wayland,
}

public static class SessionDetector
{
    public static BackendKind? Resolve(string? sessionType) => sessionType?.Trim().ToLowerInvariant() switch
    {
        "x11" => BackendKind.X11,
        "wayland" => BackendKind.Wayland,
        _ => null,
    };
}

// The single place that decides which protocol is active; `$DISPLAY` is never consulted, so a
// Wayland session cannot silently fall back to X11.
public sealed class BackendFactory(Func<IWindowBackend>? x11 = null, Func<IWindowBackend>? wayland = null)
{
    public IWindowBackend Create(string? sessionType = null)
    {
        var provider = SessionDetector.Resolve(sessionType) switch
        {
            BackendKind.X11 => x11,
            BackendKind.Wayland => wayland,
            _ => null,
        };

        return provider?.Invoke() ?? throw new NoBackend(sessionType);
    }
}
