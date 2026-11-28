# Feature: CLI

The CLI is the primary interface — keybindings invoke it, scripts can too. It stays a
fast, stateless, single-invocation tool.

## Usage

```
tiler <position> [options]
tiler gui [options]
tiler install-helper [--yes]
tiler doctor
```

`<position>`: one of the 11 names in features/window-positions.md (case-insensitive).

## Options (v1)

| Option | Applies to | Meaning |
|---|---|---|
| `--verbose` | all | stderr diagnostics (backend chosen, monitor rect, applied rect) |
| `--dry-run` | position | resolve and log the target rect, apply nothing |
| `--gui` | — | alias for `tiler gui` |

Post-v1: `--monitor <n|name>` (cross-monitor targeting), `--window <title-regex>`
(act on a non-focused window).

## Exit codes

| Code | Meaning |
|---|---|
| 0 | success (or idempotent no-op) |
| 1 | bad usage / unknown position (parser error) |
| 2 | no focused window (e.g. overview open, lock screen) |
| 3 | backend unavailable (no `$DISPLAY` under X11; helper extension not active on Wayland) — message always includes the remediation command |
| 4 | window not tileable or geometry rejected (override-redirect, min-size guard, `ApplyFailed` when the backend rejects the rectangle) |
| 5 | stale/inconsistent monitor state |
| 6 | session type unknown or unset (`XDG_SESSION_TYPE` is neither `x11` nor `wayland`) — the message names the value and the remediation; never a cross-protocol fallback |

## Behavior contract

- **Startup path:** read `$XDG_SESSION_TYPE` → pick backend → (Wayland) verify helper
  bus name → get monitors + focused window → compute rect → apply → exit. Budget:
  p95 < 150 ms on an AOT binary (keybinding feel). Measure in CI (time `tiler doctor`).
- **Output policy:** success is silent (no stdout noise for keybinding users); all
  diagnostics on stderr. `--verbose` prints the resolved rect — that is the only stdout.
- **Environment dependence:** the *keybinding command string* written by the app differs
  per install channel (system: `tiler left-half`; Flatpak:
  `flatpak run dev.blange.tiler left-half`). The CLI itself is channel-agnostic; only the
  registered command differs (see packaging/flatpak.md).
- **Concurrent invocations:** none needed — each process re-reads state; a race between
  two rapid commands resolves to last-write-wins in the compositor. No locks in v1.

## `tiler doctor`

Prints a human-readable health report: detected session type, backend name + version,
helper extension status (Wayland), monitor list with work areas, GSettings schema
installed?, keybindings registered (count + which), install channel. Non-zero exit if
anything is broken. This is the supported troubleshooting entry point.
