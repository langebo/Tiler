# Feature: Runtime Geometry & Multi-Monitor

Replaces the hardcoded `SCREEN_WIDTH = 3840`, `VISIBLE_SCREEN_HEIGHT = 1600 - 32` and the
fixed 32px top offset in `Repositioner.cs` with runtime detection.

## Monitor model

Every backend reports `Monitor { Id, X, Y, Width, Height, WorkX, WorkY, WorkWidth,
WorkHeight, Name }` (see architecture/backends.md). All coordinates are screen-absolute.

- `Width/Height`: full physical monitor extent.
- `WorkX…WorkHeight`: the area free of top bar / docks / reserved panels.
  - **Wayland:** mutter's per-monitor work area, exposed by our helper
    (`display.get_monitors()` + work-area query).
  - **X11:** RANDR `RRMonitor.work*` when per-monitor; degraded fallback to
    `_NET_WORKAREA` (primary screen only) documented in backends.md.

## Rules

1. **Tile target = monitor containing the focused window's center point**
   (`cx = wx + ww/2`, `cy = wy + wh/2`; containment test against full monitor rect).
   Rationale: a maximized window spans the full work area — its *center* still lies on
   the right monitor even if borders overlap a neighbor; top-left corner fails there.
2. **Single monitor:** everything reduces to today's behavior, computed instead of
   hardcoded. `Full` = entire work area.
3. **Multiple monitors:** commands always tile within the window's own monitor (v1
   decision). No cross-monitor movement; a `--monitor` targeting flag is post-v1.
4. **Disconnected/changing monitors:** geometry is resolved at command time, never cached
   across invocations (CLI process is short-lived, so this is free). The settings dialog
   re-queries on monitor-change signals (`org.freedesktop.DisplayManager`/randr events)
   while open.
5. **Primary monitor** carries no special meaning in v1.
6. Mixed-DPI: each monitor's geometry is used in its own physical pixels as reported by
   the backend; no logical/physical scaling conversion is done by the engine
   (mutter/X server report what they apply). Documented as a known limitation; per-monitor
   DPI is a post-v1 concern.
7. Ultrawide / non-16:9: no aspect assumptions anywhere — the grid model in
   window-positions.md handles any ratio.

## Failure modes

| Condition | Behavior |
|---|---|
| Focused window's monitor no longer exists (unplug mid-call) | Error exit 5, message names the stale state; user re-issues |
| Work area smaller than 100px (weird dock config) | Same min-size guard as positions: reject, exit 4 |
| Window center not contained in any monitor (multihead gap layouts) | Fallback: monitor of the top-left corner; if still none, error exit 5 |

## Verification acceptance (v1)

- Dual-monitor Xvfb (1920×1080 + 2560×1440, offset): tiling from each monitor produces
  correct screen-absolute rects (integration test).
- Wayland dual-monitor (manual matrix): top bar excluded from `Full`, per-monitor
  work areas respected.
- Disconnecting the focused monitor and re-issuing the command works without restart
  (inherent: no daemon).
