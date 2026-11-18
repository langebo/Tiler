# Tiler

Tile the focused window to predefined zones on any GNOME desktop — Wayland **and** X11 —
with app-managed keybindings, a small settings dialog, and runtime screen detection.

App id `dev.blange.tiler` · binary `tiler` · D-Bus name `dev.blange.Tiler`

## Current state

Quick-and-dirty proof of concept: a single .NET 8 Native AOT CLI binary
(self-contained, linux-x64) that moves the focused window via D-Bus to the GNOME
"Windows" extension.

- `tiler <position>` — `Full`, `LeftHalf`, `MiddleHalf`, `RightHalf`, `LeftThird`,
  `MiddleThird`, `RightThird`, `LeftTwoThirds`, `RightTwoThirds`, `LeftQuarter`, `RightQuarter`
- Zones are computed for a **hardcoded 3840×1600 screen** (minus a 32px top bar) —
  no runtime monitor detection yet
- No settings UI, no keybinding management, no packaging

Build: `dotnet publish -r linux-x64` (AOT).

## The plan

v1 turns this into a real, installable application:

- **Hybrid window manipulation** — native X11 (P/Invoke) on X11; the GNOME extension
  becomes a small dedicated Wayland helper (auto-install with one-time consent)
- **Runtime geometry** — monitor/work-area auto-detection, multi-monitor rules
- **Config & keybindings** — GSettings schema, app-managed `custom-keybindings`
  (zero manual setup)
- **Settings dialog** — small GTK4 UI (opt-in `tiler gui`)
- **Packaging** — Flatpak + OBS first, snap later; GitHub Actions CI/release

The task board (epics, milestones, tasks) lives in the
["Tiler" GitHub project](https://github.com/orgs/langebo/projects) — this repo holds
code and engineering documentation.

## Documentation

| Area | Doc |
|---|---|
| Architecture | [overview](docs/architecture/overview.md) · [backends](docs/architecture/backends.md) |
| Features | [window positions](docs/features/window-positions.md) · [runtime geometry](docs/features/runtime-geometry.md) · [CLI](docs/features/cli.md) · [keybindings](docs/features/keybindings.md) · [config](docs/features/settings-config.md) · [settings dialog](docs/features/settings-dialog.md) · [Wayland helper](docs/features/wayland-helper.md) |
| Packaging | [Flatpak](docs/packaging/flatpak.md) · [OBS](docs/packaging/obs.md) · [snap](docs/packaging/snap.md) · [CI/release](docs/packaging/ci-release.md) |

## Global constraints

- Native AOT, self-contained single binary; no JIT, no `dotnet` runtime on user machines.
- Works headless-CLI even without a GUI library present (dialog is an opt-in subcommand).
- No third-party GNOME extension dependency (our own helper is the only Wayland requirement).
- Config and keybinding state live in standard GSettings locations (Flatpak-compatible).
- GNOME support window: current − 2 stable releases (both extension and X11 paths must support it).

## Testing strategy

- **Unit (CI, every PR):** position math, geometry/rounding, keybinding GSettings diffing, config loading — pure functions, no display needed.
- **Integration (CI, Xvfb):** X11 backend against Xvfb + EWMH fake (`_NET_WORKAREA`, `_NET_LIST_OF_MONITORS`); assert tiled coordinates.
- **Wayland:** requires real GNOME (mutter) — manual smoke matrix per supported GNOME release; not automated in CI (no headless mutter).
- **Packaging (CI, on tag):** flatpak-builder bundle builds; OBS source upload verifies with a build on Tumbleweed.