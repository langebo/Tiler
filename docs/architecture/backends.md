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
- Client geometry: frame = `XQueryTree` parent, frame origin =
  `XTranslateCoordinates(frame, root)`; read `_NET_FRAME_EXTENTS` (`left top right bottom`)
  to derive client area. CSD windows (no reparenting frame, `border_width == 0` and no frame
  extents) tile by their own geometry. (`XGetWindowReparentData` is not a libX11 function —
  see the spike note below.)
- Monitor list: prefer **RANDR** `RRGetMonitors` (monitor geometry + active state). RANDR does
  not report work areas, so work area = monitor rect ∩ `_NET_WORKAREA` (what GDK does), full
  monitor when the property is absent. Fallback without RANDR: `_NET_LIST_OF_MONITORS` +
  `_NET_WORKAREA` (work area of the *primary screen only* — per-monitor work area is then full
  monitor). Documented degradation, acceptable: X11 multi-monitor with per-monitor docks is rare.
- Apply: `XMoveResizeWindow` (or `XMoveResizeWindow` on the frame, adjusting for
  `_NET_FRAME_EXTENTS`) on the client window; use `XSync`/event round-trip to confirm.
- Skip unmanaged/override-redirect windows (dialogs, docklets): `XGetWindowAttributes`
  `override_redirect` → error "window not tileable" (exit 4).

Work-area semantics: the top bar / docks are excluded because `_NET_WORKAREA` is the WM's
work area (clipped to the monitor rect). This replaces the hardcoded 32px offset.

Spike results (TL-105): the whole surface above was proven under Xvfb from a net11 Native AOT
binary — `libX11.so.6` / `libXrandr.so.2` resolved with `NativeLibrary.Load` + `GetExport` and
called through function pointers only (no `DllImport`, so no marshalling stub to fall back to).
Focus, CSD/framed geometry, `XMoveResizeWindow` and RANDR monitors were read back with
`xwininfo`/`xrandr`; the `_NET_LIST_OF_MONITORS` path was exercised with RANDR disabled. This
lives in `tests/Tiler.X11.Tests` (CI, under `xvfb-run`) plus a temporary `tests/Tiler.X11.Spike`
AOT exe run by hand (`dotnet publish` + `xvfb-run`, see its README), both to be deleted when the
real backend lands.

Details that deviated from the text above: `XGetWindowReparentData` does not exist (real calls
now listed above); RANDR has no work-area field, so work areas come from `_NET_WORKAREA`;
`_NET_LIST_OF_MONITORS` has no authoritative layout — read as flat CARDINAL x/y/width/height
rects per monitor.

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

## WaylandBackend spike results (TL-106)

Spike sources: `tests/Tiler.Wayland.Spike/` (throwaway, not in `Tiler.sln`) — a 78-line ESM
`extension.js` + `metadata.json`, and a C# driver using the same `Tmds.DBus.SourceGenerator`
proxy pattern as `src/Tiler/Dbus/Windows.xml` (a standalone csproj, so the generator wiring is
proven outside the shipping project too). The extension exports `dev.blange.Tiler` on
`/dev/blange/Tiler` with `GetFocused → (u s)`, `GetWorkarea → s` (spike-only probe standing in
for the shipping `GetMonitors`) and `MoveResize (u i i u u)`, applies a tile by unmaximizing
first and then calling `move_resize_frame`.

Verified on GNOME Shell 51.1 / Wayland: the files install to
`~/.local/share/gnome-shell/extensions/tiler@blange.dev/`, `node --check` passes, the bus name
`dev.blange.Tiler` was unowned before install, and the D-Bus surface marshals end to end — a
stand-in GJS process exporting the identical XML object under that name was driven by the C#
proxy (`FOCUSED ok id=4242`, `WORKAREA ok x=0 y=32 w=1920 h=1048`, `TILED ok … w=960 h=1048`,
exit 0). Not verified from the automated run, still owed by one manual log-out/log-in: the
mutter-side behaviour (focus, real work area, the visible tile). The running shell only scans
extension directories at startup, so a freshly copied extension cannot be enabled live —
`gnome-extensions enable` answers `Extension "tiler@blange.dev" does not exist`, the manager's
`EnableExtension` returns `(false,)`, `ReloadExtension` fails with
`NotSupported: ReloadExtension is deprecated and does not work`, `org.gnome.Shell.Eval` returns
`(false, '')` (unsafe mode off) and editing `org.gnome.shell enabled-extensions` does not load
it either. On GNOME 51 the manager interface itself is `org.gnome.Shell.Extensions` on
`/org/gnome/Shell` (manager runs in a D-Bus-activated systemd user service; the XML ships in
`gnome-shell-dbus-interfaces.gresource`), which is also why the interface is absent from
`/org/gnome/Shell/Extensions`.

| mutter / shell API | used for | documentation | stability class | signal for TL-304 |
|---|---|---|---|---|
| `global.get_window_actors()` | find the focused window actor; all lookup is by actor | GNOME Shell JS global (`js/ui/main.js`), in no published API doc — but every window extension uses it | internal-churn-risk (shipped, undocumented) | most load-bearing undocumented dependency; keep every use in one helper and re-verify per release |
| `global.display.get_current_monitor()` | which monitor's work area to probe | `Meta.Display.get_current_monitor`, introspectable in the installed `Meta-51.typelib`; no upstream docs | internal-churn-risk | already on the spec's list; per-release check |
| `meta_window.has_focus()` | find the focused window | `Meta.Window.has_focus` in `Meta-51.typelib` (mutter `src/core/window.c`) | STABLE-in-practice | keep |
| `meta_window.get_id()` | window id in the `(u s)` reply and back into `MoveResize` | `Meta.Window.get_id` in `Meta-51.typelib` | STABLE-in-practice (a Meta window id, not an XID — opaque to the engine anyway) | keep |
| `meta_window.get_title()` | title in the `GetFocused` reply | `Meta.Window:title` property | STABLE-in-practice | keep |
| `meta_window.get_work_area_for_monitor(i)` | bar-excluded rectangle (AC3) | `Meta.Window` method in `Meta-51.typelib`; `get_work_area_current_monitor` and `get_work_area_all_monitors` also exist; no upstream docs | internal-churn-risk (three near-identical accessors) | pin the accessor, re-verify per release |
| `meta_window.maximized_horizontally` / `maximized_vertically` | decide whether to unmaximize first | `Meta.Window` GObject booleans | STABLE-in-practice | keep |
| `meta_window.unmaximize(3)` | clear both maximize axes before tiling | `Meta.Window.unmaximize`, but the installed typelib exposes `unmaximize(self)` — no flags argument; the `3` that window-calls passes is tolerated by GJS, not by the introspected signature | internal-churn-risk | do not copy the magic `3`; verify `unmaximize()` per release |
| `meta_window.move_resize_frame(true, x, y, w, h)` | apply the tile | `move_resize_frame(self, user_op:bool, root_x_nw:int, root_y_nw:int, w:int, h:int)` in `Meta-51.typelib` | internal-churn-risk (only apply path) | biggest item: it takes root-north-west **frame** coordinates, so the backend contract's client-area rectangles need a `get_frame_rect`/`get_client_rect` adjustment |
| `Gio.DBusExportedObject.wrapJSObject(...)` + `.export(connection, path)` | publish the D-Bus object | documented GJS override over GIO | STABLE | keep; it does **not** claim the well-known name |
| `Gio.bus_own_name(...)` | own `dev.blange.Tiler` (AC1) | documented GIO API | STABLE | mandatory: without it the exported object is reachable only on the unique name — verified, `ListNames` showed nothing until the call was added |

Divergences from `docs/features/wayland-helper.md` found while running the spike (the spec is
not edited here; TL-301/TL-304 consume these):

- `MoveResize` “mutter `move_resize_full`” — **no such method**: `Meta-51` exposes
  `move_resize_frame(user_op, root_x_nw, root_y_nw, w, h)` (and `move_frame`), verified by
  introspecting the installed typelib. It also takes frame (root-NW) coordinates, not the
  client area the backend contract promises.
- `monitor.get_workarea()` — not what the spike used; the bar-free rectangle came from
  `Meta.Window.get_work_area_for_monitor(current_monitor)` (window-calls' own idiom).
- Enable step “call `org.gnome.Shell.EnableExtension` where available” — that interface name
  does not exist; it is `org.gnome.Shell.Extensions.EnableExtension` on `/org/gnome/Shell`, and
  it is a no-op for a fresh install (the shell has not scanned it). The spec's
  “alt+F2 → `restart`” instruction is X11-only; on Wayland the only reload is log out/in.
- “The extension pins `api-version` in `metadata.json`” — GNOME 51 extension metadata has no
  `api-version` key; the version pin is `shell-version` (a list).
- `global.window_focus`, `global.get_window_manager()` and the shipping
  `List`/`GetMonitors`/`Version` methods were not exercised by this spike.

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
