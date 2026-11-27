# Feature: Wayland Helper (minimal GNOME extension)

The only Wayland dependency: our own small extension that exposes window operations over
session D-Bus, so the AOT binary can tile without a third-party extension (Window Calls
is no longer required; a fresh GNOME install needs nothing but `tiler`).

## Identity & interface

- UUID: `tiler@blange.dev` (stable forever; never change).
- Ships in `src/Tiler.Helper/`: `extension.js` (ESM), `metadata.json`, `schemas/` if it
  needs its own (it does **not** — it is stateless; see below).
- D-Bus: well-known name `dev.blange.Tiler`, object `/dev/blange/Tiler`,
  interface `dev.blange.Tiler`:

| Method | Signature | Purpose |
|---|---|---|
| `List` | → `s` (JSON) | all windows: `{id, title, focused, monitor, x, y, width, height}` (client coords) |
| `GetFocused` | → `(u s)` | focused window id + title, `(0,"")` if none |
| `GetMonitors` | → `s` (JSON) | `{id, x, y, width, height, workX, workY, workW, workH, name}` |
| `MoveResize` | `(u i i u u)` | id, x, y, w, h — mutter `move_resize_frame(user_op, root_x_nw, root_y_nw, w, h)` on the window, work-area absolute. It takes root-NW **frame** coordinates, so the extension converts the client-area rectangle with `get_frame_rect`/`get_client_rect` first |
| `Version` | → `s` | extension/app compat version (semver) |

JSON over D-Bus (as today's Window-Calls integration does) is deliberate: it keeps the
generated proxy trivial and lets the shape evolve without ABI churn; payloads are small
(one desktop).

## mutter surface (the compatibility liability)

Uses the same documented-in-practice mutter globals as Window Calls:
`global.get_window_manager()`, `global.get_display()`, `Mutter.Window`,
`Meta.Window.get_work_area_for_monitor(current_monitor)` (per-monitor work area — this is
what gives us bar-free rectangles on Wayland), `global.window_focus`.
**Policy:** support a rolling window of GNOME current − 2 (e.g. when GNOME 49 ships:
support 47–49). The extension pins its supported releases in the `shell-version` list in
`metadata.json`; per-release verification via the manual matrix (README testing strategy).
mutter internals churn is the single biggest long-maintenance cost of the whole project —
budget for it, don't hide it.

## Install / consent flow (v1 decision: auto, one consent)

`tiler install-helper` (or the dialog's Helper section):
1. Wayland check (`XDG_SESSION_TYPE=wayland`); non-Wayland → "not needed here", exit 0.
2. Consent: TTY/CLI prompt (or dialog button). Text names exactly what will be done.
   `--yes` skips the prompt (used by packages' postinst if we ever pre-install).
3. Consent recorded in GSettings (`helper-consent-given`) — never re-prompted.
4. **Install:** extract the extension (embedded in the AOT binary as an embedded
   resource — one binary, no sidecar files) to
   `~/.local/share/gnome-shell/extensions/tiler@blange.dev/` (user-local, no root,
   survives upgrade: reinstall overwrites, old version dir removed).
5. **Enable:** add UUID to `org.gnome.shell enabled-extensions` (GSettings) **and** call
   `org.gnome.Shell.Extensions.EnableExtension` on `/org/gnome/Shell` (the manager lives in a
   D-Bus-activated service on GNOME 51). A freshly copied extension is only scanned at shell
   start, and that call is then a no-op, so surface a precise reload instruction (one-time,
   clearly explained): on Wayland that is log out and back in — `alt+F2` → `restart` exists
   on X11 only, and `ReloadExtension` is deprecated.
6. **Verify:** wait for bus name `dev.blange.Tiler` (≤ 10 s); success → done; timeout →
   remediation message (the reload instruction from step 5) and exit 3.
7. **Uninstall:** mirror of 4–5 (remove dir, remove from `enabled-extensions`,
   DisableExtension), keep consent record so a future reinstall doesn't re-prompt.
   `doctor` reports full state at all times.

Flatpak note: the flatpak **app** performs this install into the *host* user profile
via `--filesystem=home`-scoped paths? No — a sandboxed flatpak cannot write the host's
gnome-shell dir. Resolution: the flatpak manifest grants
`--filesystem=~/.local/share/gnome-shell/extensions:ro` is insufficient (needs write) →
we grant `--filesystem=~/.local/share/gnome-shell/extensions` (write) **and** the
dconf write to `org.gnome.shell`. Documented sandbox surface (packaging/flatpak.md).

## Security posture (stated, for the consent text)

The extension can move/resize/close any window (mutter power). It executes **no user
input, no network, no shell commands** — it only calls mutter methods with values
received from our D-Bus methods, and only accepts calls from the session bus
(`dbus_interface` registered on the session bus, standard for extensions). Source is
reviewable, small, and versioned in this repo.

## Testing

- `extension.js` unit tests: run under `node` with a stubbed `global/mutter` mock for
  the D-Bus method handlers (JSON shapes, edge cases) in CI.
- Live matrix (manual, at M2 + each GNOME release): install → enable → `tiler doctor`
  green → all 11 positions on 1 and 2 monitors.
