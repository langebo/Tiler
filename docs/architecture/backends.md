# Backends Spec

## Contract

```csharp
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

public record Monitor(uint Id, int X, int Y, int Width, int Height,
                      int WorkX, int WorkY, int WorkWidth, int WorkHeight, string Name);

public record FocusResult(ulong WindowId, string Title, uint MonitorId,
                          int X, int Y, int Width, int Height);

public record TileRect(int X, int Y, int Width, int Height);  // absolute, client-area coords
```

- `TileRect` is computed by the engine from `(Monitor, Position)` — the backend just applies it.
- Coordinates are **screen-absolute** (multi-monitor aware), not per-monitor relative.
- `windowId` is opaque to the engine: X11 window id (uint) vs. Wayland mutter window id (uint) —
  both fit `ulong`. The same value that a backend reported goes back into `ApplyRectangleAsync`.

## X11Backend (P/Invoke libX11 + Xrandr)

Library: `libX11.so.6` + `libXrandr.so.2` via native interop (no libXext/XCB; both
are standard X11 userland deps, packaged per distro). Link at runtime, resolve
`dlopen("libX11.so.6")` so AOT stays self-contained.

Atom/protocol operations (all EWMH + core):
- Display: `XOpenDisplay` / `$DISPLAY`; screen = `DefaultScreen`.
- Focus: `_NET_ACTIVE_WINDOW` on root (returns 0 → no focus).
- Client geometry: `XGetWindowReparentData` gives frame origin; read `_NET_FRAME_EXTENTS`
  (`left top right bottom`) to derive client area. CSD windows (no reparenting frame,
  `border_width == 0` and no frame extents) tile by their own geometry.
- Monitor list: prefer **RANDR** `RRGetMonitors` (gives work area per monitor since
  Xrandr 1.4, and active state); fallback `_NET_LIST_OF_MONITORS` + `_NET_WORKAREA`
  (work area of the *primary screen only* — per-monitor work area is then full monitor).
  Documented degradation, acceptable: X11 multi-monitor with per-monitor docks is rare.
- Apply: `XMoveResizeWindow` (or `XMoveResizeWindow` on the frame, adjusting for
  `_NET_FRAME_EXTENTS`) on the client window; use `XSync`/event round-trip to confirm.
- Skip unmanaged/override-redirect windows (dialogs, docklets): `XGetWindowAttributes`
  `override_redirect` → error "window not tileable" (exit 4).

Work-area semantics: the top bar / docks are excluded because RANDR reports the monitor's
`workX/workY/workWidth/workHeight`. This replaces the hardcoded 32px offset.

## WaylandBackend (D-Bus → own helper extension)

- Talks to our extension's D-Bus service (see features/wayland-helper.md) using the Tmds
  source generator (proxy from an XML interface file — same pattern as today's
  `Dbus/Windows.xml`).
- Interface: `dev.blange.Tiler` object `/dev/blange/Tiler` (methods below).
- Health: on start, check the bus name `dev.blange.Tiler` is owned. Not present →
  structured error with remediation (run `tiler install-helper`; extension may need a
  shell restart) → exit code 3.
- All rectangles passed are work-area-clamped client coordinates in monitor-absolute space;
  the extension translates to mutter's `Mutter.Window.move_resize_full(...)` calls.

## Geometry provider note

Both backends populate the same `Monitor` model, so the engine is identical across
protocols. Multi-monitor rules (which monitor a tile lands on, how "full" works, etc.)
live in features/runtime-geometry.md.

## Errors

Backends surface typed errors the CLI maps to exit codes (see cli.md):
`NoDisplay`, `NoFocusedWindow`, `WindowNotTileable`, `HelperUnavailable`, `ApplyFailed`.

## Testing seams

- X11: integration tests run under **Xvfb**; a tiny EWMH fake (xdotool/xprop from CI)
  sets `_NET_WORKAREA`, `_NET_ACTIVE_WINDOW`, `_NET_FRAME_EXTENTS`,
  `_NET_LIST_OF_MONITORS`. RANDR tests use a real Xvfb screen config (`-screen 0 1920x1080`).
- Wayland: no headless mutter in CI → unit tests for the D-Bus marshalling layer only;
  the live matrix is manual (see README testing strategy).
