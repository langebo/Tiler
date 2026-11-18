# Feature: App-Managed Keybindings

Mechanism (GNOME): the app owns entries in
`org.gnome.desktop.wm.keybindings.custom-keybindings` (a:sv of entry names) plus one
`org.gnome.desktop.wm.keybindings:<name>` group per entry (`binding`, `command`,
`find-words` — we always set `find-words = false`). GNOME evaluates these shell-side and
runs `command` in the user session. This is the Rectangle-on-macOS pattern; the
shortcuts are technically shell custom-keybindings but fully managed by the app —
that is the v1 decision, and no third-party extension is involved.

## Entries

One entry per position: `tiler-<position-lowercase>` (e.g. `tiler-lefthalf`).
`command` = channel-specific binary invocation (see cli.md) + position name.

## Default bindings (v1)

| Position | Default | Position | Default |
|---|---|---|---|
| Full | `<Super>Return` | RightThird | — |
| LeftHalf | `<Super>Left` | LeftTwoThirds | `<Super><Ctrl>Left` |
| RightHalf | `<Super>Right` | RightTwoThirds | `<Super><Ctrl>Right` |
| LeftThird | `<Super>1` | LeftQuarter | `<Super><Alt>Left` |
| RightThird | `<Super>3` | RightQuarter | `<Super><Alt>Right` |

Defaults are **suggestions, not claims**: on first registration the app checks whether
the binding is already used by another entry (built-in or other custom); conflicts are
skipped and surfaced in the dialog/doctor, never overwritten.

## Lifecycle (KeybindingManager)

- **Install/first run:** `EnsureAll()` — for each position not yet in
  `custom-keybindings`, append the name and create its group with default binding +
  command. Existing user-modified entries are **never rewritten** (repair-only: if an
  entry we own exists but its `command` no longer matches our expected template — e.g.
  after a path or channel change — update just `command`, keep the user's `binding`).
- **Per-position disable:** the dialog writes an empty `<Super>`-free binding
  (`''`) rather than removing the entry (preserves state, trivially re-enableable).
- **Uninstall:** `RemoveAll()` deletes exactly the entries whose names match
  `tiler-*` and whose `command` matches our template; anything else is untouched.
  Offered as `tiler doctor --remove-keybindings` (and post-v1: an uninstall script).
- **Idempotent:** running `EnsureAll()` any number of times converges to the same state.

## Semantics & limits (honest)

- Keybindings only fire when a *normal* window has focus; on overview/lock screen they
  do not (GNOME behavior) — documented in the dialog.
- Custom keybindings on **Wayland** work the same as X11 (shell feature, session-agnostic)
  — no extra permission needed. The helper extension is only required for the *move*
  itself.
- These are GNOME-specific. KDE/XFCE/Cinnamon support (post-v1) replaces this component
  with a DE-specific implementation behind the same `IKeybindingManager` interface.

## Testing

`KeybindingManager` takes an injected GSettings facade → unit tests cover: first-run
create, no-clobber of user edits, command repair on channel change, conflict skip,
disable/enable, removal-only-ours. No live GNOME needed.
