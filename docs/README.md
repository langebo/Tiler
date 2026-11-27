# Tiler docs

What v1 is, what it is not, and the spike decisions that fixed it. Specs in this folder are
the authoritative description of intended behavior; where the shipped binary differs, the
divergence is reported, not silently absorbed.

## Docs index

| Area | Docs |
|---|---|
| Architecture | [overview](architecture/overview.md) · [backends](architecture/backends.md) |
| Features | [window positions](features/window-positions.md) · [runtime geometry](features/runtime-geometry.md) · [CLI](features/cli.md) · [keybindings](features/keybindings.md) · [settings & config](features/settings-config.md) · [settings dialog](features/settings-dialog.md) · [Wayland helper](features/wayland-helper.md) |
| Packaging | [Flatpak](packaging/flatpak.md) · [OBS](packaging/obs.md) · [snap](packaging/snap.md) · [CI/release](packaging/ci-release.md) |

## Decisions (spike gate, closed)

The three blocking spikes are done and their outcomes are locked into v1 scope. Each row points
at the section holding the measurements, not a summary of them.

| Decision | Outcome | Scope consequence | Recorded in |
|---|---|---|---|
| TL-104 — GTK4 inside a Native AOT binary | **Pass** on linux-x64 and linux-arm64 | Settings dialog ships on both architectures with the spec's scope; `GirCore.Gtk-4.0` 0.8.1 pinned; GTK4 stays a shared system library, so `tiler gui` probes for `libgtk-4.so.1` before loading any GTK type and prints "GUI not available on this system" instead of crashing | [settings-dialog.md § M0 spike result](features/settings-dialog.md#m0-spike-result--pass) |
| TL-105 — X11 backend inside a Native AOT binary | **Proven**, no scope loss | X11 backend as specced; native interop locked to `NativeLibrary.Load` + `GetExport` function pointers, no `DllImport` | [backends.md § X11Backend](architecture/backends.md#x11backend-pinvoke-libx11--xrandr) |
| TL-106 — own Wayland helper extension | **Proven** on GNOME Shell 51.1 / Wayland | Own minimal extension plus one-time consent is the Wayland approach. Known limitation: a freshly installed extension is only scanned at shell start, so on Wayland the reload is a log out/in — `tiler install-helper` says exactly that. The mutter API stability table feeds TL-304 | [backends.md § WaylandBackend spike results](architecture/backends.md#waylandbackend-spike-results-tl-106) |
| Settings access (TL-401 gate) — provisional | GirCore GLib bindings, inferred from TL-104's AOT-clean pass in the same package family | TL-401 confirms at implementation; direct D-Bus to `org.gnome.desktop.SettingsWritable` remains the fallback only if GirCore GLib proves AOT-unfriendly | this table |

**Gate rule, in force from TL-107 on:** no feature task is pulled from Backlog while it has an
unsatisfied ⛔ dependency.

## What v1 is

- **Hybrid window manipulation, picked by session type.** On X11 Tiler talks to the X server
  itself (no extension, no helper). On Wayland it uses its own small GNOME extension as the
  helper — installed by `tiler install-helper` after a single consent prompt, no third-party
  extension involved. Both paths are proven end to end.
- **Runtime geometry.** Monitors and their work areas (screen minus panels/docks) are read from
  the running desktop, including multi-monitor rules. No hardcoded screen.
- **GSettings config, app-managed keybindings.** One schema for settings, and Tiler keeps its
  own `custom-keybindings` entries in sync — no manual setup.
- **A GTK4 settings dialog on both architectures** (`tiler gui`, opt-in subcommand). It needs
  the system's GTK4; when GTK4 is missing the command says so and the CLI keeps working.
- **Two install channels:** Flatpak and OBS distro packages (Fedora, openSUSE Tumbleweed).
- **GNOME current − 2** releases supported on both the X11 and the extension path.

## What v1 is not

- **No third-party GNOME extension dependency** — our own helper is the only Wayland
  requirement.
- **No snap** — post-v1, and X11-only anyway (recorded in [snap.md](packaging/snap.md)).
- **No per-edge margins, custom zones, per-monitor overrides or per-app rules** — post-v1; the
  schema is additive so they arrive without migration.
- **No automated GUI tests** — the dialog is covered by a manual matrix per release; the
  accelerator→GSettings path underneath it is unit-tested.
- **No desktop environments other than GNOME.**
- **No dialog on machines without GTK4** — the CLI is unaffected there.
