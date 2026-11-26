# Architecture Overview

## Layers

```
┌──────────────────────────────────────────────────────────────────┐
│ Entry points (one AOT binary, `tiler`)                          │
│   tiler <position>        CLI (keybindings call this)           │
│   tiler gui               settings dialog (GTK4)                │
│   tiler install-helper    Wayland extension install (consent UI) │
│   tiler doctor            diagnostics (backend, helper, config)  │
├──────────────────────────────────────────────────────────────────┤
│ Engine (pure C#, display-agnostic)                              │
│   Position math · Monitor model · Tile computation · Routing    │
├──────────────────────────────────────────────────────────────────┤
│ Backends (IWindowBackend)                                       │
│   X11Backend          P/Invoke libX11 (+Xrandr)                 │
│   WaylandBackend      D-Bus proxy → own GNOME extension helper   │
├──────────────────────────────────────────────────────────────────┤
│ Cross-cutting                                                   │
│   Config (GSettings dev.blange.tiler) · KeybindingManager       │
│   (GSettings wm.keybindings) · Environment detection            │
│   (XDG_SESSION_TYPE) · Logging (stderr, --verbose)              │
└──────────────────────────────────────────────────────────────────┘
```

## Process model

- **No daemon in v1.** Every CLI invocation is a short-lived process: detect environment →
  select backend → resolve focused window + monitor → compute tile → apply → exit.
  This keeps startup cost low (AOT) and is what a keybinding-spawned process should be.
- The settings dialog (`tiler gui`) is the only long-lived process; it can additionally
  persist while the user edits shortcuts.
- `install-helper` writes extension files and GSettings, then exits. Verification
  (probe of the helper's D-Bus name) happens on next CLI run, not in the installer.

## Environment selection

1. `XDG_SESSION_TYPE` is the primary switch: `x11` → X11Backend, `wayland` → WaylandBackend.
2. Failures fall back with a clear error (exit code 3, see cli.md) — **no silent
   cross-protocol fallback** (e.g. a Wayland session with a leftover `$DISPLAY` must not
   tile via X11, it can't).

## Solution layout (proposed)

```
Tiler.sln
├── src/
│   ├── Tiler/                  CLI entry, engine, keybinding mgr (the binary)
│   │   ├── Engine/             Position, Monitor, TileCalculator, Routing
│   │   ├── Backends/
│   │   │   ├── IWindowBackend.cs
│   │   │   ├── X11/            P/Invoke marshalling, _NET atom handling
│   │   │   └── Wayland/        D-Bus proxy (Tmds source gen), helper checks
│   │   ├── Config/             GSettings access
│   │   ├── Keybindings/
│   │   ├── Gui/                GTK4 dialog (conditional; see settings-dialog.md)
│   │   └── Dbus/               current SNI/D-Bus proxy sources (move to Backends/Wayland/ in E1)
│   └── Tiler.Helper/           (packaging-only) the GNOME extension (C/JS, not C#)
├── tests/
│   ├── Tiler.Tests/            xunit: math, config diffing, keybindings
│   └── Tiler.X11.Tests/        Xvfb integration
└── packaging/                  flatpak/, obs/, snap/
```

The extension itself is JavaScript (GNOME Shell ESM) + metadata; it is **not** a C#
project — it ships in `src/Tiler.Helper/` for versioning convenience only.

## Key invariants

- All tile math is pure: `(Monitor, Position) → Rectangle`, unit-testable, no backend calls.
- Backends never compute geometry; they only (a) report monitors + focused window and
  (b) apply a rectangle.
- The helper extension is the *only* component that touches mutter internals; it exposes
  a small stable D-Bus API (see features/wayland-helper.md) so the C# side never changes
  when mutter churns underneath.