# Tiler — agent instructions

A personal desktop tool: one .NET 11 Native AOT binary that tiles the focused window on GNOME
(Wayland and X11). Background lives in `README.md`; intended behavior lives in `docs/`.
This file is the contract for coding agents working in this repo — rules that are not obvious
from the code. Keep it short; if a rule is already in `docs/`, link instead of repeating.

## Hard constraints

- **Native AOT, self-contained, single binary.** Target framework, RID, version and AOT flags
  live only in the root `Directory.Build.props`; test projects opt out through
  `tests/Directory.Build.props`. Never set them per project to work around a problem.
- **AOT-safe code.** No reflection-based serialization (use a `System.Text.Json` source-gen
  context, like `src/Tiler/Dbus/DbusJsonContext.cs`), no dynamic codegen, no trim-unsafe APIs.
  A new `DynamicDependency` or `RequiresUnreferencedCode` needs a one-line justification in the
  change description. AOT analyzer warnings are not to be suppressed.
- **No new NuGet dependency without asking first.** If it is justified, it must be AOT- and
  trim-clean, and the reason goes in the PR body.
- **Headless CLI never needs GTK.** Nothing in the shipping binary may reference GTK; the
  dialog is an opt-in subcommand (`docs/features/settings-dialog.md`).
- **No silent cross-protocol fallback.** A Wayland session with no helper is an error with a
  clear message and exit code 3 — never tile through `$DISPLAY` behind the user's back
  (`docs/architecture/overview.md`).
- **State lives in standard locations.** Config and keybindings only through GSettings / XDG
  paths, Flatpak-compatible. No files under the repo, no `$HOME` dotfiles of our own.

## Architecture map (current tree)

| Path | Role |
|---|---|
| `src/Tiler/Program.cs` | entry point, `CommandLineParser` wiring, `DynamicDependency` on the options type |
| `src/Tiler/Repositioner.cs` | today: geometry + the D-Bus call in one place; `CalculatePosition` is the pure core |
| `src/Tiler/Position.cs`, `MoveOptions.cs` | CLI surface |
| `src/Tiler/Dbus/` | Tmds.DBus proxy for the GNOME "Windows" extension; `Windows.xml` is an `AdditionalFile` for the source generator |
| `tests/Tiler.Tests/` | unit tests, no display required |
| `tests/Tiler.X11.Tests/` | Xvfb integration; recompiles the spike's X11 interop sources |
| `tests/Tiler.X11.Spike/`, `tests/Tiler.AotGui.Spike/` | throwaway spikes, deliberately outside `Tiler.sln` |

The target layout and its invariants (pure tile math, backends only report monitors and apply a
rectangle) are in `docs/architecture/overview.md`. Follow that document; do not pre-create
folders for future epics.

## Conventions

- Match the file you are in. There is no `.editorconfig`: file-scoped namespaces, 4-space
  indent, `Nullable` enabled globally, constants in `SCREAMING_SNAKE_CASE`.
- Comments explain *why* the code cannot show it (see `tests/Tiler.X11.Tests/X11FactAttribute.cs`);
  no narration of what the next line does.
- Smallest correct diff. No drive-by renames, reformatting, or refactors of untouched code. If
  you notice unrelated dead code, mention it; do not delete it.
- Tests are xunit and named after the behavior they pin. Display-dependent tests use
  `X11Fact` / `XvfbFact` so a plain `dotnet test` on a desktop still passes. Never delete, skip
  or weaken an existing test to get green — a test that looks wrong is reported, not edited.
- Spikes stay out of `Tiler.sln`, and nothing in the shipping build may depend on them. Only
  delete a spike when the board issue's decision is recorded.

## Verification — required before claiming a change is done

```bash
dotnet build
xvfb-run -a dotnet test                     # full suite; plain `dotnet test` skips X11 tests
```

Add an AOT publish when the change touches AOT-sensitive code (reflection, serialization,
P/Invoke, trimming, `*.csproj`, `Directory.Build.props`):

```bash
dotnet publish src/Tiler/Tiler.csproj -c Release -r linux-x64 -o out
xvfb-run -a ./out/tiler --version
```

- Report the exact commands and their output. "Tests pass" without the command is not evidence.
  A skipped test is not a passing test — say which ones skipped and why.
- Required checks on `main` are `build`, `test`, `aot (linux-x64)`, `aot (linux-arm64)`.
  Local green does not override a red CI run.
- Anything that needs a real GNOME session (Wayland backend, helper install, settings dialog)
  cannot be verified in CI: say what you could not check manually, and list the exact steps a
  human should follow.

## Git and board

- Work happens on a branch, never on `main` (protected). Board task branches are `task/TL-xxx`;
  other work uses a descriptive branch (`chore/…`, `docs/…`).
- Commit subjects are `<type>(<scope>): <imperative summary>`, with the task ID as the scope for
  board tasks — `feat(TL-203): add X11 backend`, `docs(TL-105): record spike findings`,
  `ci(TL-104): …`. Types in use: `feat`, `fix`, `ci`, `docs`, `spike`, `chore`.
- One task = one branch = one PR, merged with a merge commit (no squash) and a
  `Closes #<issue>` line in the body. Never `--admin` a merge, never force-push anything except
  your own task branch (`--force-with-lease`).
- Never commit `bin/`, `obj/`, `.worktrees/`, or scratch files (`plan.md`, `progress.md`).

## Specs and docs

- `docs/` describes intended behavior and is authoritative for review. If the code and a spec
  disagree, report the divergence — do not quietly edit the spec to match the implementation.
- Board tasks: the `task-runner` skill (`.agents/skills/task-runner/SKILL.md`) keeps the
  implementer read-only on `docs/` and applies spec corrections as a separate `docs(<ID>)` commit
  in the task branch. Interactive changes may update `docs/` in the same PR when the behavior
  change is the point of the change.

## Repo-wide rules live here only

`.agents/agents/` defines the `architect`, `worker` and `reviewer` roles, and they inherit this
file. Repo facts and constraints belong in `AGENTS.md` — do not repeat them in role prompts or
in per-task dispatch text, or they drift.
