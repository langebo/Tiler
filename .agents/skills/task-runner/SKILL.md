---
name: task-runner
description: Implement a Tiler board task (TL-xxx, the "Tiler" GitHub project) via a developer/reviewer subagent loop; dispatch a worker to implement, a reviewer to verify against the task's acceptance criteria, and re-dispatch the developer with the found issues until the reviewer passes. Use when the user names a task ID (e.g. "run TL-205", "implement task TL-202") or invokes /skill:task-runner with a task ID.
disable-model-invocation: false
---

# Task Runner

Orchestrate board tasks (the "Tiler" GitHub project) through a **developer → reviewer loop**.
You (the main agent) never implement the task yourself — you dispatch subagents via
the `subagent` tool (pi-subagents package; its builtin `worker` and `reviewer`
agents) and judge their structured results.

## 1. Resolve the task

Input: one task ID (`TL-1xx` … `TL-6xx`).

The **board is the "Tiler" GitHub project** (number 1, owner `langebo`, repo
`langebo/Tiler`) — tasks are issues, state lives in the `Task status` field
(`Todo | In progress | ReviewPassed | Done | Dropped`). Access it with `gh`:
- `gh issue view <num> --repo langebo/Tiler --json title,body` — the issue body IS the task block (Deps, description, guidelines, acceptance criteria)
- `gh project item-list 1 --owner langebo --format json` — item field values (`Epic`, `Task status`)
- TL-ID → issue number: issue titles carry the TL-ID (`gh issue list --repo langebo/Tiler --label task --state all`) — the planning files were migrated to GitHub Projects and are no longer kept in-repo

1. Resolve the TL-ID to an issue: `gh issue list --repo langebo/Tiler --label task
   --state all` (issue titles carry the TL-ID).
2. View the issue. If its `Task status` is `Done` or `Dropped`: stop and ask the
   user what they want before proceeding (re-run anyway? resume the worktree?).
3. **Dependency gate:** parse the task's `**Deps:**` line (ignore `—` and milestones
   like `M2 exit`; a `⛔` prefix marks a spike decision). Resolve every TL-ID via the
   map and check each one's `Task status`. Any not `Done` → stop, list the missing
   deps, ask the user whether to proceed anyway. A `⛔`-marked dep with no recorded
   spike decision (a comment on the spike issue) is a hard stop — do not offer to
   proceed.
4. Collect spec context the developer needs: the spec files referenced by the task
   (e.g. `docs/features/cli.md`, `docs/architecture/backends.md`) and
   the root `README.md` (constraints + testing strategy).
5. Confirm with the user in one line: task title + what it does. Then start the loop (§4).

## 2. Prepare branch & worktree, then dispatch the developer

Before the first developer dispatch, the orchestrator (you) prepares an isolated
work area — this happens once, before the loop, and is reused by every iteration.

First take a **baseline**: from the repo root, run the standard build + test commands
on `main` once. Red baseline → stop and tell the user — both subagents would chase
pre-existing failures. Then create the work area:

```bash
# from the repo root; only if the branch does not already exist (resume case)
git fetch origin
git worktree add -b task/<ID> .worktrees/<ID> main
```

- Worktree lives at `<repo root>/.worktrees/<ID>` (untracked, local; ignore it in git
  if you commit anything else — do NOT add a `.gitignore` entry for it).
- If the branch `task/<ID>` already exists (re-run/resume of an earlier session),
  reuse it: `git worktree add .worktrees/<ID> task/<ID>` (or remove the stale worktree
  first if the path exists: `git worktree remove --force .worktrees/<ID>`).
- Tell the user which worktree the loop will run in, then continue.

`subagent({ agent: "worker", task: "<self-contained prompt>", async: true,
cwd: "<repo root>/.worktrees/<ID>" })` with a **self-contained** task (the
subagent sees nothing else). Include, in this order:

- **Goal:** "Implement task `<ID> — <title>` in the Tiler worktree at
  `<repo root>/.worktrees/<ID>` (branch `task/<ID>`, already checked out)."
- **Branch rule:** "Work ONLY inside the worktree above. Commit your work to
  `task/<ID>` (small commits). Do NOT touch `main`, do NOT merge, do not create other
  branches or worktrees."
- **The task block verbatim** (the GitHub issue body: description + technical
  guidelines + acceptance criteria), including the issue number so commits can
  reference it.
- **Spec excerpts:** the referenced spec sections (paste them, do not just give paths —
  the worker can read them, but inline the parts that constrain the design).
- **Ground rules:** net11 + Native AOT, no new dependencies without explicit
  justification in the report, keep the repo's existing structure/conventions.
  Note: `docs/` (architecture/features/packaging specs) is tracked and IS checked
  out in the worktree — you may READ it there, but must NOT edit it; work from the
  specs pasted below. If you believe a
  spec is wrong, record it under REPORT item (d); the orchestrator updates `docs/`
  in the main checkout at completion.
- **Deliverable (exact contract):** end the final message with a `## REPORT` section
  containing: (a) what was changed (files + one line each), (b) how each acceptance
  criterion was satisfied or why it needs a manual check, (c) test/build commands run
  and their results, (d) open questions / known gaps (incl. any suspected spec
  divergence), (e) worktree left clean: `git status --porcelain` empty, everything
  committed.
- **Test integrity:** never delete, skip, or weaken existing tests/assertions to make
  the suite green; if a test looks wrong, leave it as-is and flag it in (d).
- If this is a **re-dispatch** (loop iteration > 1): the worktree is unchanged from
  the previous run; additionally include the reviewer's issue list verbatim under
  `## ISSUES TO FIX (from review)` and instruct: "Fix every issue on the same branch
  `task/<ID>` in the same worktree, re-run your tests, and produce an updated REPORT
  noting the delta since the last iteration."

Record the returned `runId`. Async completion wakes your session natively (do not
poll); read the child's final message (its `## REPORT`) from the wake, or check
`subagent({ action: "status", id: "<runId>", options: { view: "transcript" } })`.
If the run failed, inspect the transcript tail / saved output artifact the same way.

## 3. Dispatch the reviewer

`subagent({ agent: "reviewer", task: "<self-contained prompt>", async: true,
cwd: "<repo root>/.worktrees/<ID>" })`. The builtin reviewer is read-only but has
bash — it may build and run tests. Include:

- **Goal:** "Verify that branch `task/<ID>` fully satisfies the task below. The code
  lives in the worktree `<repo root>/.worktrees/<ID>` — inspect the actual diff
  (`git -C <worktree> diff main...task/<ID>`), read files there, and run the test
  suite / acceptance checks you can run headlessly from the worktree."
- **The task block verbatim** (the GitHub issue body: title, guidelines, acceptance criteria).
- **The developer's REPORT verbatim** — and the instruction: "The developer claims X;
  verify claims against the code, do not take them on trust."
- **Verdict contract (exact):** the final message must end with either
  `VERDICT: PASS` or `VERDICT: FAIL`, followed by `## ISSUES` — a numbered list, each
  issue as: what is wrong, where (file:line), why it matters, and what "fixed" looks
  like. If PASS, `## ISSUES` must be empty. No PASS with open issues.
- **Adversarial stance:** assume the implementation is subtly wrong; try to falsify
  each REPORT claim; exercise the failure modes listed in the spec.
- **Scope:** flag only (a) acceptance-criterion failures, (b) bugs the diff
  introduces, (c) red/broken tests. No style or out-of-scope improvement nits.
- **Test audit:** check the diff for deleted/skipped/weakened tests; require the full
  suite green (any failure must be proven pre-existing on `main`).
- **Iter > 1 only:** include the previous round's `## ISSUES` and instruct: "First
  verify each previous issue is actually fixed (code/test evidence); re-raise only
  with new evidence; keep the previous issue numbers for issues still open."
- **Read-only intent:** the reviewer may run builds/tests but must NOT modify files in
  the worktree (scratch goes to /tmp only).

The completion wake delivers the verdict; check it via
`subagent({ action: "status", id: "<runId>", options: { view: "transcript" } })` if
needed. Before dispatch, snapshot `git -C <worktree>
status --porcelain`; compare after — if the worktree changed during review, discard
that round and re-dispatch the reviewer once; if it recurs, tell the user.

## 4. The loop

```
iteration = 0
loop:
  iteration += 1
  dev  = delegate worker   (task spec [+ previous issues if iteration > 1])
  wait for dev
  rev  = delegate reviewer  (task spec + dev REPORT)
  wait for rev
  if rev verdict == PASS:  push + CI green → §5
  else if iteration == 5:  stop → report to user: branch name, remaining issues,
                           reviewer's last verdict. Ask how to proceed.
  else: continue loop      (developer gets rev's issue list)
```

- **Cap at 5 developer iterations.** Two consecutive rounds with the same open issue
  numbers means the loop is stuck — stop early, report, ask the user.
- **Verdict parsing:** take the LAST `VERDICT: PASS|FAIL` line in the reviewer's
  output. Missing/unparseable verdict, or `PASS` with a non-empty `## ISSUES` →
  treat as `FAIL` (the raw output becomes the issue note).
- Never dispatch both agents in parallel; the reviewer always reviews the latest
  developer output.
- If a developer run **fails** (crashed/timed out): inspect its status/transcript
  (`action: "status"`, `view: "transcript"`) and saved output artifact, then
  re-dispatch the developer with a note "your previous run died mid-task; the worktree
  `<repo root>/.worktrees/<ID>` still exists on branch `task/<ID>` — inspect its git
  log/status, keep the good parts, finish the work" instead of starting from scratch.
- Between iterations, one short status line to the user:
  "Iteration N: reviewer found K issues (summary of the worst one)".
- **CI gate (on PASS, before §5):** `git -C <worktree> push origin task/<ID>`, then
  wait for the Actions run to finish (timeout ~20 min). Red → treat as a FAIL with
  CI log excerpts as the issue list and loop back. The board's Definition of Done
  includes CI green; local tests alone are not enough.

## 5. Completion (review passed → manual confirmation → done)

When the reviewer returns `VERDICT: PASS`, the loop is over but the task is **not**
done yet — the user confirms manually.

1. Update the board: `gh project item-edit 1 --owner langebo --url <issue-url>
   --field "Task status" --value ReviewPassed` (it was set to `In progress` after the
   first developer dispatch, §2).
2. **Write the completion summary for the user.** Two parts, in this order:
   - **What changed:** from the developer's REPORT + the diff, grouped by area, each
     item one line: non-technical "what this means for you" first, then the technical
     shape (files, new commands, config, deps).
   - **How to test it manually:** a numbered checklist the user can follow in a real
     session, built from: the task's acceptance criteria marked *manual*, the
     developer's "needs a manual check" items, plus the basic path (build/publish the
     worktree, run the app, exercise the feature, run `tiler doctor`). Be concrete —
     real commands and expected visible outcomes, never "verify it works".
   - **Spec updates (if any):** if the REPORT flagged spec divergence, apply those
     edits to `docs/` in the MAIN checkout now (in its own commit; the worktree's
     copy is read-only for the developer) and list
     them here. Post the whole summary as an issue comment — the comment is the
     task's history on the board:
     `gh issue comment <num> --repo langebo/Tiler --body-file /tmp/<id>-summary.md`
3. **Ask the user to confirm** (use the structured-question tool): present the summary,
   then ask "Task TL-xxx: does your manual testing confirm it works?" with options:
   - *Confirmed — mark done* (description: marks the task Done, suggests merge)
   - *Problems found* (the user types what is wrong)
   - *Defer* (leave it ReviewPassed; stop here, no further work)
   A free-form answer like "scrap it" → set `Task status` to `Dropped` and, on the user's
   go-ahead, `git worktree remove --force` + `git branch -D task/<ID>`.
   **Wait for the answer. Do not mark the task Done before it.**
4. Branch on the answer:
   - **Confirmed:** set `Task status` to `Done`. Report final state; offer the merge
     (check out `main`, `git merge --ff-only task/<ID>`, then
     `git worktree remove .worktrees/<ID>`) as the suggested next step — run it only on
     the user's go-ahead. Commit messages / the PR should reference the issue
     (`Closes #<num>`) so the merge links them on GitHub.
   - **Problems found:** the user's description becomes the issue list for a new loop
     round: re-dispatch the developer (same worktree + issues, same REPORT contract),
     then the reviewer; the iteration cap (§4) keeps applying cumulatively. On a
     later PASS, return to step 2 with an updated summary (delta since last review).
   - **Defer:** stop. The task stays `ReviewPassed`; tell the user the worktree and
     branch are preserved for whenever they come back to it.