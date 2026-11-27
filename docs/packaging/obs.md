# Packaging: OBS (distro packages, v1)

Channel: `openSUSE Build Service` (build.opensuse.org) project, native distro packages —
the "real RPM/deb" install path for Fedora/openSUSE/Debian users.

## Project layout

- Project: `home:blange:tiler` (start in a home project; moving to `devel:apps` or a
  domain-named project is a later governance decision, not a build change).
- Packages:
  - `tiler` — the AOT binary, GSettings schema, `.desktop` files
    (`Tiler.desktop` launcher with "Settings" action, `Tiler-keybindings` not a package
    concept), man page. `Provides: tiler-cli`.
  - `tiler-helper` — the GNOME extension (files only; **not** auto-enabled by the
    package — enablement stays a user-consented action via `tiler install-helper`;
    packages that auto-load shell extensions are a hard no for distro policy).
    Recommends `tiler`.
- Targets (v1): **Fedora (current + previous)** and **openSUSE Tumbleweed + current
  stable**. Debian/Ubuntu: post-v1 (OBS makes this a config change, not a rewrite).

## Build model

**Source = release tarball from GitHub Actions** (git import would require OBS to
install the .NET AOT toolchain in build roots — possible but heavy). So:
- Actions produces a deterministic `tiler-X.Y.Z.tar.gz`: AOT binaries (linux-x64,
  linux-arm64 in separate subpackages or arch: noarch + per-arch binary blobs),
  schema XML, extension tarball, spec file template.
- spec: `%install` places prebuilt AOT binaries; `Requires: libX11, libXrandr`
  (runtime P/Invoke deps), `Recommends: tiler-helper, dconf, gnome-settings-daemon, gtk4`
  — gtk4 only for the settings dialog, which needs the system library; the CLI never loads it.
  `%post`: `glib-compile-schemas`. `%files` minimal. No build-time .NET in OBS at all.
- **Constraint to document in the spec header:** binaries are prebuilt upstream (OBS
  verifies/builds packaging only). If you later want full OBS-native AOT builds, that
  is a separate milestone (build-root toolchain provisioning).

## CI integration

`ci-release.md` handles the upload: tag push → Actions builds → `osc`/OBS API creates
the source and triggers builds → job status watched; release notes link build status.
Failed target build blocks the release (green policy).

## Acceptance

- `zypper in tiler` (TW) and `dnf install tiler` (Fedora current): `tiler doctor`
  green on both Wayland and X11 sessions; `tiler left-half` works immediately;
  helper available via `tiler-helper` + consent flow.
- Uninstall removes schema entries from `custom-keybindings` template match only
  (postunscript runs the removal path of KeybindingManager — verify it degrades safely
  when dconf is absent).
