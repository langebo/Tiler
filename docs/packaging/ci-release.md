# CI & Release (GitHub + Actions)

Repo: public GitHub (blange/Tiler or blange.dev mirror — same content). All packaging
artifacts are reproducible-ish: source tarball generated from the tagged commit,
deterministic build flags, commit SHA embedded in the binary (`tiler --version`).

## Pipelines

### `ci.yml` — every PR + push
1. **build**: `dotnet build` (net11), both RIDs `linux-x64`/`linux-arm64`
   (arm64 via `ubuntu-24.04-arm` runner or QEMU-free cross AOT — AOT cross-compiles
   without emulation; verify in M0).
2. **test**: xunit (unit + Xvfb integration). Xvfb setup:
   `sudo apt-get install xvfb x11-xserver-utils xdotool` + `xvfb-run` wrapper.
3. **aot**: full `dotnet publish -c Release` per RID; assert binary size < budget
   (warn-only, tracked over time) and `tiler doctor` runs clean under Xvfb.
4. **flatpak-sdk**: (from M5) flatpak-builder dry build in the GNOME SDK — the
   AOT-inside-SDK risk gets a standing CI job before M5 work starts.
5. **helper**: `node --test` on `extension.js` D-Bus handlers with the mutter stub.

### `release.yml` — on tag `vX.Y.Z`
Gate: branch `main` is green, tag matches `vX.Y.Z` (semver only).
1. Build AOT binaries (x64, arm64) + source tarball (spec-ready layout) +
   `.flatpakref` via flatpak build-bundle + zip of the extension (also embedded in the
   binary, but shipped standalone for manual installs).
2. **OBS upload**: create/replace source package in `home:blange:tiler` via OBS
   REST API (token in repo secrets), trigger builds for Fedora-current, Fedora-prev,
   TW; poll to completion (timeout 2 h); job result posted as a workflow summary.
   **Red OBS build = release not published** (step fails → no GitHub release).
3. Publish GitHub Release with assets + generated notes (release-please).
4. Refresh our flatpak remote (repo metadata + `--update-metadata`); document URL in
   README install section.

## Versioning & cadence

- SemVer. Patch: fixes; Minor: feature (new position is *not* a minor bump — positions
  are config-visible); Major: schema or D-Bus interface change (helper `Version` check
  is our compat gate).
- GNOME release tracking: when a new GNOME stable ships, cut a patch release within 2
  weeks with the mutter-surface compatibility fix (wayland-helper.md policy), manual
  matrix green. This is the standing recurring maintenance cost of the project.

## Release checklist (run per tag, mostly automated)

- [ ] `ci.yml` green on the tag commit
- [ ] manual matrix: GNOME Wayland (current stable, x64) — install helper, all 11
      positions, 2 monitors, dialog
- [ ] manual matrix: GNOME X11 (or Xorg session) — native path, no helper
- [ ] `tiler doctor` clean on: fresh profile, upgraded profile, flatpak, rpm
- [ ] OBS builds green, flatpak bundle verified on a clean VM
- [ ] release published, remote refreshed, changelog posted
