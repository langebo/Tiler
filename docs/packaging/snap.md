# Packaging: Snap (post-v1, deferred)

Decision: **not part of v1.** Revisit after Flatpak + OBS are stable. This file records
the constraints so the revisit is a decision, not a discovery.

## Hard constraints

- **Wayland: no.** Snap's seccomp profile blocks Wayland sockets; a snap on a GNOME
  Wayland session runs on XWayland at best — and our *helper extension* lives in the
  host shell regardless of which backend our binary uses. Net effect: a snap install
  would be the **X11-backend-only** flavor of Tiler. Selling that honestly is possible
  ("works today; full Wayland via flatpak/distro package"), but it is a permanent
  second-class channel on the most common GNOME session.
- Helper extension from inside strict confinement: the snap cannot write the host's
  `~/.local/share/gnome-shell/extensions` (no home-write in strict mode; classic
  confinement is a no for a settings-adjacent tool). So a snap install means either
  "user installs `tiler-helper` from the distro/flatpak channel" or X11-only.

## If we do it anyway (revisit checklist)

1. Confinement: `strict` + plugs `x11`, `home` (for dconf — partial), `desktop`,
   `desktop-legacy`, `dotnet` (core20+ has a dotnet plug; check current status at time).
2. Keybinding command string gets a third channel variant:
   `snap run tiler <position>`.
3. `doctor` must explain the X11-only limitation clearly, or we don't ship.
4. GNOME dconf/GSettings writes from strict confinement are unreliable — the settings
   dialog would be broken; only CLI + keybindings would ship in the snap.

Verdict to confirm at revisit: is an X11-only, CLI-only snap worth maintaining a third
release artifact? Default answer: **no, archive this file and skip.**
