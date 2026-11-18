# Tiler — Documentation

**Tiler** is an installable Linux application that tiles the focused window to
predefined zones on any GNOME desktop (Wayland **and** X11), with app-managed
keybindings, a small settings dialog, runtime screen detection, and distribution
via Flatpak and OBS.

The v1 task board (epics, milestones, tasks) lives in the "Tiler" GitHub project —
this directory holds engineering documentation only.

## Documents

```
docs/
├── README.md                     ← this file
├── architecture/
│   ├── overview.md               Layered architecture, process model, directory layout
│   └── backends.md               IWindowBackend contract, X11 & Wayland-impl details, geometry providers
├── features/
│   ├── window-positions.md       Position set + geometry math
│   ├── runtime-geometry.md       Monitor/work-area auto-detection, multi-monitor rules
│   ├── cli.md                    Command surface, exit codes, environment behavior
│   ├── keybindings.md            App-managed GNOME custom-keybindings lifecycle
│   ├── settings-config.md        GSettings schema + storage semantics
│   ├── settings-dialog.md        v1 GTK4 UI scope
│   └── wayland-helper.md         Minimal GNOME extension spec + install/consent flow
└── packaging/
    ├── flatpak.md                Manifest, sandbox surface, flatpak-specific keybinding command
    ├── obs.md                    OBS project, spec files, dist targets
    ├── snap.md                   Later: constraints and caveats
    └── ci-release.md             GitHub Actions pipelines and release process
```

## Testing strategy

- **Unit (CI, every PR):** position math, geometry/rounding, keybinding GSettings diffing, config loading — pure functions, no display needed.
- **Integration (CI, Xvfb):** X11 backend against Xvfb + EWMH fake (`_NET_WORKAREA`, `_NET_LIST_OF_MONITORS`); assert tiled coordinates.
- **Wayland:** requires real GNOME (mutter) — manual smoke matrix per supported GNOME release; not automated in CI (no headless mutter).
- **Packaging (CI, on tag):** flatpak-builder bundle builds; OBS source upload verifies with a build on Tumbleweed.

## Global constraints

- Native AOT, self-contained single binary; no JIT, no `dotnet` runtime on user machines.
- Works headless-CLI even without a GUI library present (dialog is an opt-in subcommand).
- No third-party GNOME extension dependency (our own helper is the only Wayland requirement).
- Config and keybinding state live in standard GSettings locations (Flatpak-compatible).
- GNOME support window: current − 2 stable releases (tracked, bumped per release; both extension and X11 paths must support the whole window).
