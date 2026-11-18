# Feature: Settings Dialog (v1) + M0 Spike

## M0 spike (blocking, do before any GUI work)

Goal: prove or kill **GTK4-in-.NET-AOT (net11)** on the two target architectures.

Spike program (throwaway, but kept as `tests/Tiler.AotGui.Spike`):
1. Bind GTK4 via community bindings (`Gir.Core.Gtk4` / GtkSharp4 lineage) with
   `<PublishAot>true</PublishAot>`, linux-x64 **and** linux-arm64.
2. Open a window containing a label, a check button, one event loop round-trip, exit.
3. Record: build success, startup time, binary size, runtime stability under repeated
   open/close (50 cycles).

**Decision gate:**
- Pass (both arches, stable) → dialog proceeds as specced below.
- Fail on one arch → dialog ships for the passing arch only; other arch gets
  `tiler doctor` + config-file-free notice "GUI not available on this architecture".
- Fail entirely → v1 ships CLI-only (GUI descends to post-v1); **everything else in this
  plan is unaffected** — that is the point of isolating the risk here.

If the C# GTK4 bindings are dead/unmaintained at spike time, the allowed alternatives
(in order): (a) hand-rolled P/Invoke against `libgtk-4.so` for the ~12 widgets we need
(deliberately finite), (b) drop GUI per gate. No other frameworks.

## Dialog scope (small on purpose)

One window, ~400×520, four sections. Launched via `tiler gui` (also from a `.desktop`
file: "Tiler Settings").

1. **Status** — session type, backend active?, helper installed/enabled/running
   (Wayland), install channel. One-line verdict per row, red/amber/green dot.
2. **Shortcuts** — one row per position: enable toggle + accelerator editor
   (GNOME `GtkAccelEditor`-style: user presses the combo, we write the GSettings
   accelerator string). Buttons: *Reset to defaults*, *Remove all Tiler shortcuts*.
3. **Helper (Wayland only)** — status, *Install/Reinstall helper*, *Restart shell
   prompt* (explanatory), *Uninstall helper*.
4. **About** — version, links (repo, blange.dev), `doctor` output copy button.

Behavior:
- Edits apply immediately (GSettings write + KeybindingManager sync); no Apply button.
- While the dialog is open it listens for keybinding/monitor changes made elsewhere and
  refreshes (no write conflicts expected; last-writer-wins is fine).
- The dialog is the **only** process allowed to write GSettings; CLI is read-only
  (except `install-helper`).
- Startup: AOT, no JIT; target < 500 ms to first frame (measured, non-blocking metric).

## AOT notes

- GTK4 pulls a lot of native symbols; expect the AOT binary to grow ~30–60 MB. Accepted
  (compare: Rectangle-class apps ship similarly). Track size in CI.
- All C#→GTK calls go through one `Gui/` module so the bindings surface stays auditable;
  no reflection in that module (AOT analyzer must stay clean).

## Testing

GUI: no automated UI tests in v1 (Cypress-level tooling for GTK4 is not a dependency
we take on). Manual matrix per M4: X11 + Wayland × x64 + arm64 × fresh profile.
The accelerator→GSettings-write path **is** unit-tested (it's just the KeybindingManager).
