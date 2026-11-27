# Tiler.Wayland.Spike

Throwaway spike for issue #6: can our own tiny GNOME Shell extension (`tiler@blange.dev`) take
window operations over session D-Bus and visibly tile a window, so the third-party Window Calls
extension can be dropped?

Deliberately **not** in `Tiler.sln`: TL-301 authors the shipping `src/Tiler.Helper/` extension
and the `WaylandBackend` from what this proves. Nothing in the shipping build depends on it.

- `extension/` — `metadata.json` + ESM `extension.js` exporting `dev.blange.Tiler` on
  `/dev/blange/Tiler` with `GetFocused`, `GetWorkarea`, `MoveResize`.
- `driver/` — C# console app using the same `Tmds.DBus.SourceGenerator` proxy pattern as
  `src/Tiler/Dbus/Windows.xml`; tiles the focused window to the left half.

`GetWorkarea` is a **spike-only** diagnostic standing in for the shipping `GetMonitors`
(`s` JSON of the focused window's monitor work area) so bar exclusion is observable without
building the real monitor list.

## Run (needs a real GNOME session, `XDG_SESSION_TYPE=wayland`)

Checks, in order. Each prints an observable; stop at the first one that does not.

```sh
# 0. No name collision before installing — must print nothing.
gdbus call --session --dest org.freedesktop.DBus --object-path /org/freedesktop/DBus \
  --method org.freedesktop.DBus.ListNames | grep dev.blange.Tiler

# 1. Install the extension into the user profile.
mkdir -p ~/.local/share/gnome-shell/extensions/tiler@blange.dev
cp extension/extension.js extension/metadata.json \
  ~/.local/share/gnome-shell/extensions/tiler@blange.dev/

# 2. Enable it. The running shell only knows the extensions it scanned at startup, so right
#    after the copy `gnome-extensions enable` fails with "does not exist" (verified on 51.1)
#    and `org.gnome.Shell.Extensions.EnableExtension` (interface on /org/gnome/Shell, not on
#    /org/gnome/Shell/Extensions) returns false. Add the UUID to the enable list instead:
gsettings set org.gnome.shell enabled-extensions \
  "$(gsettings get org.gnome.shell enabled-extensions | sed "s/]$/, 'tiler@blange.dev']/")"

# 3. Log out and back in — a Wayland session has no in-place shell reload, and
#    org.gnome.Shell.Extensions.ReloadExtension is deprecated/no-op on 51. Then check:
gnome-extensions info tiler@blange.dev        # expect: Enabled: Yes / State: ACTIVE

# 4. Wait up to 10 s for the bus name.
for i in $(seq 10); do
  gdbus call --session --dest org.freedesktop.DBus --object-path /org/freedesktop/DBus \
    --method org.freedesktop.DBus.ListNames | grep -q dev.blange.Tiler && { echo "bus name owned"; break; }
  sleep 1
done
```

If the name never appears after the log-in, the extension failed to load: the shell's own error
list is `gdbus call --session --dest org.gnome.Shell --object-path /org/gnome/Shell --method
org.gnome.Shell.Extensions.GetExtensionErrors tiler@blange.dev`. If a stale name is already
owned before step 1, the spike used `dev.blange.Tiler.Spike` instead — recorded in
`docs/architecture/backends.md`.

Note on the bus name: `Gio.DBusExportedObject.export()` only publishes the object on the
connection; the well-known name is claimed with a separate `Gio.bus_own_name()` in `enable()`
(window-calls needs no such call only because the shell already owns `org.gnome.Shell`).

## Drive it from C#

```sh
dotnet build driver/Tiler.Wayland.Spike.csproj
# Focus an ordinary window (Files, a terminal), then:
dotnet run --project driver
```

Expect three lines and exit code 0:

```
FOCUSED ok id=<n> title=<t>
WORKAREA ok x=<n> y=<n> w=<n> h=<n>
TILED ok id=<n> x=<n> y=<n> w=<n> h=<n>
```

and the focused window visibly snapping to the left half of its monitor's work area. Any
failure prints one `FAILED ...` line and exits non-zero. The `y > 0` assertion is the
bar-exclusion check: it fails on a session without a top bar rather than passing trivially.

## Uninstall (undo the side effects)

```sh
gsettings set org.gnome.shell enabled-extensions \
  "$(gsettings get org.gnome.shell enabled-extensions | sed "s/, 'tiler@blange.dev'//")"
rm -rf ~/.local/share/gnome-shell/extensions/tiler@blange.dev
```

`window-calls@domandoman.xyz` is untouched by this spike; both register distinct bus names and
coexist.
