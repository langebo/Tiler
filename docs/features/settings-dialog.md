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

## M0 spike result — **Pass**

Spike: `tests/Tiler.AotGui.Spike` (throwaway, outside `Tiler.sln`, so no shipped code or test
depends on GTK), bindings `GirCore.Gtk-4.0` 0.8.1. Window = label + check button, one
main-loop round-trip, exit 0; `--cycles 50` repeats open/close. Both architectures published
with `PublishAot=true`, net11.0.

| Measurement | linux-x64 | linux-arm64 |
|---|---|---|
| AOT publish | ok, 0 trim/AOT-analyzer warnings | ok, 0 trim/AOT-analyzer warnings |
| First frame after process start (median of 5, native) | **125 ms** (cold 1232; then 127, 125, 124, 120) | **144 ms** (cold 1502; then 144, 142, 141, 146) |
| First frame, same run on a dev workstation | 261 ms median (255–347) | not applicable |
| Process start → exit, one window (dev workstation) | 388 ms median (359–401) | not measured |
| 50× open/close | **50/50 windows, exit 0** | **50/50 windows, exit 0** |
| Spike binary (CI publish) | 2,910,152 B | 3,077,888 B |
| Shipping `tiler`, same publish config | 5,923,976 B | 6,161,328 B |

Provenance — every number above is measured, none estimated:
- **Native, both arches, CI**: `gui-spike (linux-x64)` on `ubuntu-24.04` and
  `gui-spike (linux-arm64)` on `ubuntu-24.04-arm`, i.e. real arm64 hardware. The first-frame
  stopwatch starts at managed entry, so loader time (~tens of ms) is excluded — the metric is
  slightly optimistic, and still an order of magnitude inside the target.
- Dev-workstation x64 cross-check: openSUSE Tumbleweed, GTK 4.24.2, Mesa, same AOT binary.
- An earlier arm64 run under qemu-user (2,485 ms) was ~10× inflated by emulation and is
  superseded by the native runner number above.

Bindings: `Gir.Core` is alive — `GirCore.Gtk-4.0` 0.8.1 released 2026-07-12,
0.9.0-preview.2 on 2026-11-26, commits on 2026-11-26, repo not archived
(github.com/gircore/gir.core). The package id in the task was wrong: it is `GirCore.Gtk-4.0`,
not `Gir.Core.Gtk4`. No AOT workaround was needed (0 analyzer warnings), so alternative (a)
hand-rolled P/Invoke was not used. 0.8.1 carries net10 assets that net11 consumes fine;
0.9.0-preview.2 adds a net11 asset. Pin 0.8.1.

**What the binary-size number really means.** Native AOT compiles only our managed code;
Gir.Core P/Invokes `libgtk-4.so.1`, so GTK stays a shared system library (13 MB plus ~118
transitive libraries on openSUSE) and does not inflate our binary. The spike binary is
therefore *smaller* than `tiler` (2.9 MB vs 5.9 MB), not the +30–60 MB predicted above.
Consequence: the settings window needs GTK4 present on the machine. Without it, touching a
`Gtk` type aborts the process — `DllNotFoundException: Unable to load shared library
'libgtk-4.so.1'`, exit 134 (measured) — so `tiler gui` must probe for `libgtk-4.so.1`
*before* loading the GUI module and print the "GUI not available on this system" notice
instead of crashing. That probe is M1 work.

### Fallback consequences (gate table, resolved)

- **Pass (this decision)** → the dialog ships for both architectures, exactly as specced below.
- Fail on one arch → dialog ships for the passing arch only; the failing arch gets
  `tiler doctor` plus the config-file-free notice "GUI not available on this architecture".
  Not needed: both arches passed natively.
- Fail entirely → v1 ships CLI-only, GUI descends to post-v1; everything else in this plan is
  unaffected. Not needed.

**Non-engineer summary — we will ship a settings window in v1**, because a small GTK4 window
works inside our own self-contained, ahead-of-time-compiled program on both processor
architectures we ship, opens and closes fifty times in a row without a failure, and appears on
screen about a seventh of a second after launch — well inside our half-second target. The one
new requirement is that the window needs the machine's GTK4 desktop library, which GNOME
desktops provide (minimal/server installs may lack it); the command line keeps working without
it, and `tiler gui` will say so plainly instead of failing.
