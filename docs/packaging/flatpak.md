# Packaging: Flatpak (primary channel, v1)

App id: `dev.blange.tiler`. Runtime: `org.gnome.Platform` (current stable SDK, e.g.
24.08 → track GNOME release cadence). Built with `flatpak-builder` from a JSON manifest
in `packaging/flatpak/tiler.json`.

## Manifest essentials

```jsonc
{
  "id": "dev.blange.tiler",
  "runtime": "org.gnome.Platform", "runtime-version": "stable",
  "sdk": "org.gnome.Sdk",
  "finish-args": [
    "--share=ipc",
    "--socket=fallback-x11",          // X11 path: real $DISPLAY access
    "--socket=wayland",               // only needed for the GTK4 dialog
    "--device=dri",
    "--talk-name=org.gnome.Shell",    // helper enable + enabled-extensions discovery
    "--talk-name=org.gnome.Shell.Extensions",
    "--talk-name=dev.blange.Tiler",   // our own helper
    "--talk-name=org.freedesktop.DBus"  // properties only; no extra names beyond the above
    "--filesystem=~/.config",         // dconf (GSettings read/write)
    "--filesystem=~/.local/share/gnome-shell/extensions",  // helper install (write)
    "--env=..."  // none
  ]
}
```
Prune in review: keep only what `doctor` proves necessary (the secrets line above is
**not** part of the set — shown to mark the "deny-by-default" rule). Session-bus
`--talk-name` for dconf's writer goes via `org.gnome.desktop.SettingsWritable` if the
GLib settings fallback is used (settings-config.md).

## Build stage

`dotnet publish -c Release -r linux-{x64,arm64} --self-contained` (AOT) **inside the
SDK** (toolchain: `mike-burr/dotnet` or the GNOME.Sdk `dotnet11` module — pick the
module that exists in the target SDK at build time; if none, add a `dotnet-sdk`
submodule to the manifest). AOT inside the Flatpak SDK is the single riskiest packaging
step → it gets its own CI job before M5 starts.

## Channel-specific behavior (important)

- **Keybinding command:** when running inside the flatpak (detected via `$FLATPAK_ID`),
  `KeybindingManager` writes `flatpak run dev.blange.tiler <position>` instead of bare
  `tiler <position>`. GNOME custom-keybindings execute in the host session, so the
  flatpak must be *findable by name* there — standard flatpak behavior, no extra grants.
  Trade-off documented: ~100–300 ms extra `flatpak run` startup per shortcut.
- **Helper install from inside the sandbox:** writes the host
  `~/.local/share/gnome-shell/extensions/` via the `--filesystem` grant above, and
  toggles `org.gnome.shell enabled-extensions` via dconf over the session bus (host
  dconf, works because we're on the real session bus). Both surface in `doctor`.
- **Updates:** extension embedded in the binary; app update re-extracts (same
  install-helper code path, non-interactive when version stamp already consented).

## Distribution

- **v1:** publish to our own flatpak remote (GitHub Releases artifact + a small
  flatpak-repo on blange.dev or GitHub Pages); docs explain `flatpak remote-add`.
  Optionally submit to Flathub once the manifest is stable (Flathub requires public
  GitHub — we have it) — **decision point at M5, not committed**.
- Release binary: `flatpak build-bundle` → `dev.blange.tiler-X.Y.Z.flatpakref` in the
  GitHub release.

## Acceptance

Fresh Fedora + GNOME (Wayland) and an X11 session:
`flatpak install` from our remote → `tiler doctor` all green → shortcuts registered and
working → dialog opens. On X11: no helper install attempted, no extension side effects.
