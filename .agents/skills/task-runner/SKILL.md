---
name: task-runner
description: Implement a Tiler board task (TL-xxx, the "Tiler" GitHub project) with an architect → developer → reviewer subagent loop and a PR finalization stage; the architect interviews the user, produces plan.md, the developer implements it, the reviewer verifies it, then the orchestrator drives PR, CI, merge and cleanup. Use when the user names a task ID (e.g. "run TL-205", "implement task TL-202") or invokes /skill:task-runner with a task ID.
disable-model-invocation: false
---

# Task Runner

Orchestrate board tasks (the "Tiler" GitHub project) through **architect → developer →
reviewer**, then through **PR → CI → merge → cleanup**.
You (the main agent) never implement code yourself — you dispatch subagents via the
`subagent` tool and judge their structured results.

| Role | Agent (project-owned, `.agents/agents/`) | Model | Mutates |
|------|-------|-------|---------|
| Plan + interview | `architect` | `opencode-go/glm-5.3` | nothing |
| Implementation | `worker` (alias "developer") | `opencode-go/deepseek-v4.1-flash` | worktree only |
| Verification | `reviewer` | `opencode-go/deepseek-v4.1-flash` | nothing |

The three role definitions live in `.agents/agents/` and are discovered **project-scoped**:
`cwd` must be inside this repo (the worktree qualifies). The `.agents/skills/` subtree is
skipped by agent discovery, so the skill cannot be mistaken for an agent. Their system prompts
own the stable contracts (REPORT, VERDICT/ISSUES, read-only rules, Tiler constraints); the
dispatch prompts below only add what varies per task.

## 0. Invariants — never break these

- `main` is protected: **every change reaches it through a PR**. No direct commit or push to
  `main`, no `--admin`, no force.
- Merge only on an explicit instruction from the user in this session.
- Force-push only `task/<ID>`, always `--force-with-lease`. Never `main`, never another branch.
- Delete a worktree or branch only when the PR shows `mergedAt` non-null, or the user says
  scrap. Never `git worktree remove --force` on uncommitted work.
- Children never touch `main`, never merge, never create branches or worktrees.
- Never delete, skip or weaken tests to get green.
- Allowed untracked scratch in the worktree: `plan.md` and `progress.md`, nothing else.
- The board comment trail is the task's history — keep it current (§7).

## 1. Resolve the task and probe state

Input: one task ID (`TL-1xx` … `TL-6xx`).

The **board is the "Tiler" GitHub project** (number 1, owner `langebo`, repo `langebo/Tiler`)
— tasks are issues, state lives in the `Task status` field
(`Todo | In progress | ReviewPassed | Done | Dropped`). Access with `gh`:
- `gh issue view <num> --repo langebo/Tiler --json title,body,url` — the body IS the task block
- `gh project item-list 1 --owner langebo --format json` — item fields (`Epic`, `Task status`)
- TL-ID → issue number: issue titles carry the TL-ID (`gh issue list --repo langebo/Tiler --label task --state all`)

1. **Probe before building anything** (§9): `git worktree list`, `git branch --list 'task/*'`,
   `gh pr list --repo langebo/Tiler --head task/<ID> --state all`, and the issue comments
   (an existing plan or a previous round's summary). Pick the entry point: plan, develop,
   review, or finalize.
2. Resolve the TL-ID to an issue, then view it. `Task status` `Done` or `Dropped` → stop and
   ask the user before touching anything.
3. **Dependency gate:** parse `**Deps:**` (ignore `—` and milestones like `M2 exit`; `⛔`
   marks a spike decision). Any dep not `Done` → stop, list the missing deps, ask whether to
   proceed. A `⛔` dep with no recorded spike decision is a hard stop — do not offer to proceed.
4. Read the required check names once and keep them in memory (G4):
   `gh api repos/langebo/Tiler/branches/main/protection --jq '.required_status_checks.contexts'`.
   On error/404, fall back to the names in `.github/workflows/ci.yml`.
5. Collect spec context: the spec files the task references (`docs/features/…`,
   `docs/architecture/…`, `docs/packaging/…`) and the root `README.md` constraints.
6. Confirm in one line (title + what it does), then continue.

## 2. Preconditions: baseline and work area

Take a **baseline**: from the repo root, run the standard build + test commands on `main`
once. Red baseline → stop and tell the user; every child would chase pre-existing failures.

Then create the work area (needed before the architect, which plans against the real tree):

```bash
git fetch origin
git worktree add -b task/<ID> .worktrees/<ID> main
```

- Resume case: branch exists → `git worktree add .worktrees/<ID> task/<ID>`; a stale path →
  `git worktree remove --force .worktrees/<ID>` first (only when nothing is uncommitted).
- Worktree: `<repo root>/.worktrees/<ID>`, untracked. Do not add it to `.gitignore`
  (`.git/info/exclude` is acceptable if the user asks).
- Tell the user which worktree the loop uses.

## 3. Architect: interview → plan → approval

Dispatch (cwd = the worktree, async; the completion wake delivers its final message):

`subagent({ agent: "architect", task: "<prompt>", async: true, cwd: "<repo>/.worktrees/<ID>" })`

The prompt is self-contained: **Goal** ("plan task `<ID>` — `<title>`; the repo at
`<worktree>` is a checkout of `main`, untouched"), **ROUND 1**, the **task block verbatim**
(with issue number), **spec file paths + the excerpts that constrain the design**, the
`README.md` constraints, and "your final message is machine-parsed — follow the output
contract in your system prompt exactly".

### 3.1 Interview (max 2 rounds, max 4 questions each)

Parse `## INTERVIEW` and `## MY CURRENT READING`. Ask the questions with the
structured-question tool (one call, ≤4 questions; the options are the architect's, the
recommended one first). A free-form answer is passed back verbatim.

Return the answers with
`subagent({ action: "resume", id: "<architectRunId>", message: "ANSWERS: Q1 …; Q2 …" })` —
resume revives the architect with its earlier reading intact. Round 2 questions → one more
ask/resume cycle. If round 2 still asks questions, force the plan: resume with
"STOP INTERVIEWING — write the plan now and put the remaining ambiguity under
`## 8 Assumptions`." Answers from the user override the architect's reading, never the
issue's acceptance criteria.

### 3.2 Plan gate (before the user sees it)

The final message must contain `# PLAN <ID> v<n>` and these sections: `## 1. Intent`,
`## 2. Approach`, `## 3. Steps`, `## 4. Acceptance map`, `## 5. Verification`,
`## 6. Risks`, `## 7. Non-goals`, `## 8. Assumptions`, `## FINAL DECISION`.
Reject and resume with the gaps named (counts as a revision) when:
- any acceptance criterion from the issue has no row in `## 4. Acceptance map`;
- a proof cell is empty or says "verify it works" without a test name or command;
- `## 5. Verification` lacks the repo's build + full test commands;
- `## FINAL DECISION` or `## 8. Assumptions` is missing;
- the plan invents scope beyond the issue (it must sit under `## 7. Non-goals`).

Do not fix the plan yourself. The architect owns it.

### 3.3 Approval

Write the message verbatim to `.worktrees/<ID>/plan.md`, post it to the board
(`gh issue comment <num> --repo langebo/Tiler --body-file .worktrees/<ID>/plan.md`), then show
the user the plan summary (`## FINAL DECISION` + the step list) and the file path, and ask:

- **Approve plan** → continue to §4; set `Task status` to `In progress`.
- **Revise** (the user's free text) → `resume` the architect with
  `REVISION: <feedback>`, overwrite `plan.md` with `v<n+1>` (keep a one-line change log at the
  top), post it as a **new** comment ("supersedes the previous plan"), ask again. Max 3
  revision rounds, then ask the user to approve or scrap.
- **Scrap** → `Task status` `Dropped`, remove the worktree and branch (explicit scrap
  qualifies), stop.

The approved plan is **frozen** (G3): no later re-planning. The developer may diverge only by
reporting it (REPORT item f); a change to an acceptance criterion means a new approval round.
`plan.md`-only edits by the developer are not a divergence — but they are also not accepted
unless the reviewer sees the same change in the REPORT.

## 4. Dispatch the developer

`subagent({ agent: "worker", task: "<prompt>", async: true, cwd: "<repo>/.worktrees/<ID>" })`

Self-contained prompt, in this order:
- **Goal:** implement task `<ID> — <title>` in `<worktree>` (branch `task/<ID>`, checked out).
  Work only there; commit to that branch; never touch `main`.
- **Approved plan:** read `.worktrees/<ID>/plan.md`, plus the plan pasted verbatim below.
  Follow it; a wrong or impossible step is reported, never silently redesigned.
- **The task block verbatim** (issue body + issue number, so commits can reference it).
- **Spec excerpts** that constrain the design (paste them and give the paths).
- **Task-specific ground rules** only — repo-wide rules (net11/AOT, deps, test integrity,
  `docs/` policy, verification commands) come from the project `AGENTS.md`, which every child
  inherits; do not paste them into dispatch prompts.
- **Deliverable:** "end with the `## REPORT` section as defined in your instructions".
- **Re-dispatch (iteration > 1):** add `## ISSUES TO FIX` verbatim (reviewer issues or CI log
  excerpts) and "fix every item on the same branch, re-run the suite, report the delta".

Record the `runId`; async completion wakes this session (do not poll). Failed run → read
`subagent({ action: "status", id: "<runId>", options: { view: "transcript" } })` and re-dispatch
with "your previous run died mid-task; the worktree still holds your work — inspect the git
log/status, keep the good parts, finish".

## 5. Dispatch the reviewer

`subagent({ agent: "reviewer", task: "<prompt>", async: true, cwd: "<repo>/.worktrees/<ID>" })`

Include:
- **Goal:** verify branch `task/<ID>` satisfies the task — real diff, real test run, real
  acceptance checks.
- **The task block verbatim** and the **approved plan verbatim**.
- **The developer's REPORT verbatim** — "the developer claims X; verify against the code, do
  not take claims on trust".
- **Iteration > 1:** include the previous `## ISSUES` — "verify each is actually fixed; keep
  the previous numbering for issues that remain".

The verdict format, plan-conformance rule, read-only rules, review scope and test audit are in
the role prompt; do not restate them, just supply the material.

Snapshot `git -C <worktree> status --porcelain` before dispatch and after; a worktree changed
during review discards that round (re-dispatch the reviewer once, then tell the user).

## 6. The loop, and the CI gate

```
dispatches = 0                      # cumulative cap 5 (§8)
loop:
  dev = worker (task spec + plan [+ ISSUES TO FIX when iteration > 1]);  dispatches += 1
  wait → REPORT
  rev = reviewer (task spec + plan + REPORT [+ previous issues])
  wait → verdict
  PASS  → push, CI gate → §7
  FAIL  → next iteration with rev's issues
```

- **Verdict parsing:** take the LAST `VERDICT: PASS|FAIL`. Missing/unparseable, or `PASS`
  with a non-empty `## ISSUES` → treat as FAIL, using the raw output as the issue note.
- Never run the developer and reviewer in parallel; the reviewer reviews the latest output.
- One status line between iterations: "Iteration N: reviewer found K issues (worst: …)".
- `dispatches == 5` (review fixes + CI fixes together) → stop: report branch, remaining
  issues, last verdict; ask how to proceed. Two consecutive rounds with the same open issue
  numbers means it is stuck — stop early and ask.
- **CI gate (on PASS):** `git -C <worktree> push -u origin task/<ID>`, then wait for the run
  (`gh run list --branch task/<ID> --limit 1 --json databaseId` →
  `gh run watch <id> --exit-status`, timeout ~20 min). Red → treat as FAIL, with a ≤40-line
  excerpt of `gh run view <id> --log-failed` as the issue list, loop back (and count the
  dispatch). Local green is not enough: the board's Definition of Done includes CI green.

## 7. Finalize

### 7.1 Board and summary

1. `gh project item-edit 1 --owner langebo --url <issue-url> --field "Task status" --value ReviewPassed`
2. Spec corrections: apply the REPORT's `docs/` fixes **in the worktree branch** as one
   `docs(<ID>): …` commit (G4). `main` is never written directly; the PR carries them.
3. Write the completion summary for the user, two parts:
   - **What changed** — from the REPORT + diff, grouped by area, one line each: the
     user-facing meaning first, then the technical shape (files, new commands, config, deps).
   - **How to test it manually** — a numbered checklist with real commands and expected
     visible outcomes (the task's *manual* criteria, the REPORT's "needs a manual check"
     items, build/publish the worktree, run the app, `tiler doctor`). Never "verify it works".
   - Post it: `gh issue comment <num> --repo langebo/Tiler --body-file /tmp/<ID>-summary.md`.

### 7.2 Choose the next step

Ask the user (structured question):

- **Create PR** → §7.3.
- **I'll test manually first** → they test; problems → their notes become `## ISSUES TO FIX`
  and the flow re-enters §6 (the cap keeps counting); pass → come back here with "Create PR".
- **Defer** → stop. Task stays `ReviewPassed`, branch + worktree + `plan.md` preserved; add a
  one-line resume state to the issue (branch, worktree path, PR number if any). Nothing is
  deleted.

### 7.3 Create the PR

Body at `/tmp/<ID>-pr.md` (not in the worktree): Summary (plan intent), Changes (REPORT a),
Verification (REPORT c + CI result), Manual test checklist (7.1), a link to the plan comment,
and `Closes #<num>`.

```bash
gh pr create --repo langebo/Tiler --base main --head task/<ID> \
  --title "<type>(<ID>): <issue title>" --body-file /tmp/<ID>-pr.md
```

`<type>` = `feat` / `fix` / `ci` / `docs` / `refactor`, matching the diff. Then comment the PR
link on the issue.

### 7.4 Watch the checks

`gh pr checks <n> --repo langebo/Tiler --required --watch --fail-fast`, then
`gh pr view <n> --json mergeable,mergeStateStatus,state,mergedAt`. Rules (§8):

- Red checks → offer "re-dispatch the developer with these failures" (the user decides); on
  yes, re-enter §6 with the failing job names and a ≤40-line log excerpt.
- `mergeable: CONFLICTING` → the developer rebases the worktree onto `origin/main`, resolves,
  re-runs the suite, pushes `--force-with-lease` (branch is `task/<ID>`; that branch is
  theirs, `main` is untouched).
- All required checks green and `mergeable: MERGEABLE` → §7.5.

### 7.5 Merge

Ask: **I merge it** / **Merge it yourself**. With the go-ahead:

```bash
gh pr merge <n> --repo langebo/Tiler --merge --delete-branch
gh pr view <n> --json state,mergedAt     # mergedAt must be non-null
```

The repo uses merge commits — do not squash. Protection not satisfied or the merge refused →
report the exact state and stop; never `--admin`, never a local `git merge` + push.

### 7.6 Cleanup (only after a verified merge)

```bash
git fetch --prune origin
git worktree remove .worktrees/<ID>     # takes plan.md/progress.md with it
git branch -D task/<ID>
git worktree prune
rmdir .worktrees 2>/dev/null || true
rm -f /tmp/<ID>-*.md
```

Verify and print: `git worktree list`, `git branch --list 'task/*'`, `git status --porcelain`,
`git log --oneline -3`. Then the final issue comment: merged PR link, iteration count, docs
commit if any, cleanup result. The issue closes itself via `Closes #`.

PR closed without merging → delete **nothing**; stop and ask.

## 8. Failure rules — one line each, no improvisation

| Situation | Rule |
|-----------|------|
| Merge conflict on the PR | Developer rebases onto `origin/main` in the worktree, `--force-with-lease` push to `task/<ID>`, counts as a dispatch |
| Suspected flake | `gh run rerun --failed` once; a second red = real failure |
| Red after green (new failure on re-run) | Treat exactly like red — new CI excerpt into the issue list |
| Merge blocked by protection / checks | Report state, stop; no `--admin`, no local merge |
| PR closed, not merged | Stop, ask, delete nothing |
| Worktree dirty at re-dispatch | `git -C <worktree> status`; commits pushed to `task/<ID>` or reported as uncommitted in REPORT (e) |
| Architect run died | Resume it (§9) or re-dispatch with the task block; the plan gate still applies |
| Reviewer died / unparseable verdict | Re-dispatch once with the same prompt; then FAIL with the raw output as the note |
| Cap reached (5 dispatches) | Stop, report branch + open issues, ask |
| Same issue numbers twice in a row | Stuck — stop early, report, ask |

## 9. Resume and recovery

Rebuild state from the repo, never from memory alone:

1. `git worktree list` + `git branch --list 'task/*'` → does the work area exist?
2. `gh pr list --repo langebo/Tiler --head task/<ID> --state all` → is there a PR, is it
   merged, closed, or open with failing checks?
3. Issue comments → approved plan (`plan.md` in the worktree is the local copy), last summary,
   last verdict, resume state line.
4. `subagent({ action: "status" })` + the retained transcript when a previous session's run is
   still relevant; `subagent({ action: "resume", id })` to revive a child that stopped.

Then enter at the right phase: no plan → §3; plan approved, no passing verdict → §4; PASS but
no PR decision → §7.2; PR open → §7.4; merged → §7.6 (cleanup only). Never restart a phase
whose artifacts already exist.
