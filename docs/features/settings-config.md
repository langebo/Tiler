# Feature: Settings & Configuration (GSettings)

Storage: a native GSettings schema, installed with the app:

- System path: `/usr/share/glib-2.0/schemas/dev.blange.tiler.gschema.xml`
- Flatpak: schema compiled into the Flatpak bundle (standard for GSettings apps).
- User data: `~/.config/dconf` (standard dconf backend), i.e. normal GSettings behavior.

## Schema `dev.blange.tiler`

| Key | Type | Default | Meaning |
|---|---|---|---|
| `enabled-positions` | `as` | all 11 | positions whose shortcuts are registered/enabled |
| `keybinding-<position>` | `s` (11 keys, one per position) | table from keybindings.md | Accelerator string, e.g. `'<Super>Left'` |
| `helper-consent-given` | `b` | `false` | Wayland helper auto-install accepted (wayland-helper.md) |
| `helper-uuid` | `s` | built-in uuid | installed helper version stamp (upgrade detection) |
| `log-level` | `s` | `'warn'` | `fatal|error|warn|info|debug` — stderr logging verbosity |

Deliberately **not** in v1 (post-v1, same schema): gaps/margins per edge, per-monitor
overrides, custom zone definitions, per-app rules. The schema is additive-stable: keys
are only added, never retyped.

## Access layer

```csharp
public interface IConfig {
    // reads
    IReadOnlySet<Position> EnabledPositions { get; }
    string KeybindingFor(Position p);
    bool HelperConsentGiven { get; }
    // writes (dialog / install-helper use these)
    void SetKeybinding(Position p, string accelerator);
    void SetEnabled(Position p, bool enabled);
    void SetHelperConsentGiven(bool v);
}
```

- Implementation uses `Gir.GLib.Settings` (community GLib bindings — same package family
  as the GTK4 bindings, so one dependency serves both config and GUI). Fallback if the
  GLib binding proves AOT-unfriendly in the M0 spike: direct D-Bus calls to
  `org.gnome.desktop.SettingsWritable`/dconf (small, still AOT-safe). Decision is forced
  by spike result, documented in [docs/README.md](../README.md) (decisions table).
- `wm.keybindings` (the GNOME desktop schema) is accessed the same way but treated as a
  separate, external schema — only via `KeybindingManager`.

## Semantics

- No config file on disk to hand-edit (GSettings is the storage); the dialog is the
  editing surface. `tiler doctor` prints the effective values for support.
- Missing schema (broken install) → clean error "settings schema not installed, run
  `tiler doctor`", never a crash; the CLI tiling path degrades to built-in defaults so
  **shortcuts keep working even with a broken config install**.
- XDG overrides: honored for Flatpak (dconf lives in `$XDG_CONFIG_HOME`), nothing special
  needed.

## Testing

GSettings facade is injected → unit tests: defaults, persistence round-trip,
corrupt/missing schema degradation, schema-additive-migration (old data + new key).
Live dconf round-trip covered in the Xvfb integration suite.
