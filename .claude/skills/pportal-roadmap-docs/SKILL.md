---
name: pportal-roadmap-docs
description: How to work a task in this port's roadmap — the three docs/ files (ROADMAP.md, CHANGELOG.md, IMPROVEMENTS.md) owned by the roadkeep CLI and never hand-edited, the check that the public documentation area under site/docs still holds after the work, and above all the one-task-one-commit rule: every finished roadmap task ends with `run-commit.cmd -m "<title>"` from the repo root, code plus doc sync plus any page it made stale in that single commit. Use whenever adding a task, picking the next PP-number, marking a task shipped, retiring, linting, editing any of those files, executing a block or a list of PP ids, or finishing any task that touches this repo.
---

# Roadmap tasks & committing

## ⛔ READ FIRST — one task, one `run-commit.cmd` (non-negotiable)

**A task is not finished until the commit landed.** The commit tool is
`run-commit.cmd` (on the OS PATH, in `D:\Dev\bin`) — never `git commit` by hand:

```
cd d:\Git\alegauss\pportal
run-commit.cmd -m "<conventional-commits title, ASCII>"
```

- **Always pass `-m`.** It stages everything and generates the body from the staged
  diff; without a title it infers one, and for a docs/ROADMAP commit that means prose
  about already-shipped work gets misread as `feat: implement <feature>`.
- **`cd` to the repo root first** — `run-commit.cmd` stages relative to CWD.
- **The doc sync rides in the same commit as the code**, so `ROADMAP.md` /
  `CHANGELOG.md` / `IMPROVEMENTS.md` never describe a state that did not ship.
- **You may NOT do more than one task before committing.** A multi-task request
  (a whole block, or a list of `PP<n>`s) is *not* permission to batch: it is a request
  to run them one at a time, committing after each. One giant diff spanning many tasks
  is the failure this rule exists to prevent.
- **A batch of ≥2 tasks runs under the `/loop` skill** (self-paced): exactly one task
  per iteration, `run-commit.cmd` at the end of that iteration, then advance. Do not
  hand-roll a loop that defers commits to the end.
- **Self-check before starting task N+1:** `git status` / `git log -1`. If the previous
  task's work is still in the working tree, stop and commit it first.
- **Declare your paths, then read them back at the moment of committing.** A claim is dated
  by a marker write and released when the marker moves (`[claims] held = 60` minutes).
  `roadkeep claim PP<n> --path <p>` says what the task will touch; **`roadkeep claim PP<n>`
  with no `--path` answers what you declared plus what the tree holds that another live claim
  says is its own — the analysis `git add -A` cannot make.** `roadkeep claims` lists held,
  expired and stale. This rule assumes one writer and two sessions run here regularly: on
  2026-09-06 two were live, `claims` reported **0 held**, and `5755f959` swallowed four files
  of unrelated work under its own title.

The same rule applies to any finished unit of work in this repo, roadmap task or not:
when the work is done and validated, commit it with `run-commit.cmd -m "…"` rather than
leaving it in the tree.

## The gate before the commit

Validated means the build was run, not that the edit looked right —
[`compile.cmd`](../../../compile.cmd) is the only build this tree still has:

| Command | When |
|---|---|
| `compile.cmd configure` | fast check after a deletion — does every path the build graph names still resolve |
| `compile.cmd` | full build + portable tree — **before committing**. Builds the Qt client, `chiaki-unit` and the .NET host in `app\` |
| `test.cmd` | the unit suite **and** the .NET host's selftest, after anything touching `lib/`, `test/` or `app/` |
| `test.cmd <name>` | one test's output, cut out of a full run (C only) |
| `test.cmd noapp` | the C suite alone |

`configure` is cmake only and says nothing about `app\`. `noapp` skips the .NET host and prints
that it did; a machine with no .NET SDK gets a note rather than a refusal, because the Qt client
does not need one (PP74).

`test.cmd` (PP67) is the launcher for the suite, the way `compile.cmd` is for the build:
`ctest` is not on a plain Windows PATH and the binary needs the MinGW runtime beside it.
It also bounds every run — nothing configures a per-test timeout, so a hanging test
otherwise stalls with no output at all — and warns when `lib/` or `test/` is newer than
the binary, which is the stale green PP56 fixed showing up a second way.

A configure that passes is not a target that links. Run the full build before the
commit, not before the decision.

**Name EVERY task id an assertion holds, not just the one you are working.** A test
written under one id often ends up holding the task that finished the work — PP300's
assertions were all in a file whose summary said PP29 — and naming both is four
characters, while the alternative is a second test file written to move a number.
`ChiakiNg.exe --ratchet` (PP305) lists what is owed with each ledger sentence, which is
how to tell the two cases apart.

`AssertionRatchetTests` (PP38) counts
shipped tasks that no assertion mentions, against the ceiling in
[`tests/assertion-ratchet.txt`](../../../tests/assertion-ratchet.txt), and the count may
fall but may not rise — so a task shipped with no test naming its id turns the suite red
in the commit that ships it. The join is the id in the test's own summary, which is how
this tree has always been written. If the count FALLS, lower the ceiling in the same
commit: the test says so, and a ratchet left loose has given the gain away.

`ChiakiNg.exe --recount` (PP304) is the one worth running BEFORE the gate. Every
comment added to a `.c` file changes a line count that `docs/ROADMAP.md` or
`docs/IMPROVEMENTS.md` states, and `test.cmd` reports those only as a red
`CountedClaimTests` after the work is done. `--recount` answers the same question and
prints the `roadkeep` call that corrects each — with the section anchor resolved, which
is the part that is easy to get wrong: a claim about `session.c` lives in `§PP28` and
not in the `§PP293` its pointer suggests. It writes nothing; run what it prints.

## ⛔ READ SECOND — the three files are owned by `roadkeep`

[`roadkeep.toml`](../../../roadkeep.toml) declares this project's format (prefix `PP`,
`ref_scheme = "id"`, the markers and the limits) and the roadkeep plugin — declared in
[`.claude/settings.json`](../../settings.json), so a clone gets it — carries the rest: a
hook that **denies a hand-edit** to any of the three files and names the command, the
`mcp__*roadkeep__*` tools whose input schema *is* the format, and its own skill with the
write path. Start a task with `brief`, not by reading the files; `lint` is the gate.

Each file has one job — never duplicate content between them:

| File | Single responsibility |
|---|---|
| [`docs/ROADMAP.md`](../../../docs/ROADMAP.md) | **Task status** — active backlog only (📋 designed · 💭 idea · ⏳ partial · 🛠 in-progress), one line per task, plus block headings and non-goals. Nothing else. |
| [`docs/CHANGELOG.md`](../../../docs/CHANGELOG.md) | What has **shipped** — the ledger, indexed by block. Authoritative for the highest block letter. |
| [`docs/IMPROVEMENTS.md`](../../../docs/IMPROVEMENTS.md) | **Design rationale** for *unshipped* work only. No status tables, no shipped implementation reports. |

- **Shipping is `ship <id>`** — one transaction (ledger entry, roadmap line deleted,
  `§PP<n>` dropped, dependents re-annotated) or none of it. Then commit (rule above).
- **The read BEFORE an add is `delivered <block> --near "<the sentence you would file>"`.**
  It ranks that block's nearest deliveries against the sentence you are about to propose,
  which is the duplicate question asked *before* an id is spent. This tree was not calling it:
  PP736 restated PP524's `AvHeadFields` and was committed before anyone noticed, and PP524
  comes back **rank 1** for PP736's own sentence. `add` prints the same ranking as `near`, but
  only after the line exists — that is the difference between catching a duplicate and
  recording one. `non-goal list` is the other read, and non-goals are binding.
- **Adding is `add --block <x> --symptom "…" --why "…"`.** `ref_scheme = "id"` means the
  `§PP<n>` pointer is derived, not hand-numbered; the section prose is `section add`.
  **Reuse an existing block** — `stats` lists the ones still holding open lines,
  `grep -nE '^## Block' docs/CHANGELOG.md` lists every block ever opened. A new block
  needs a job no existing heading can honestly hold, and is titled for the *capability*,
  not for the task in hand.
- **The next id is `roadkeep next-id`** — it scans all three files and never fills a gap.
  Retired ids are never reused.
- **Status lives in exactly one file.** If a marker in `IMPROVEMENTS.md` disagrees with
  the roadmap files, the roadmap files win.
- **Keep a task line terse** — one sentence: symptom + why + `→ §PP<n>` pointer. The
  reasoning belongs in `IMPROVEMENTS.md`, which is what the pointer addresses.
- **Non-goals are binding.** "Windows-only" and "no redesign while porting" are refused
  at input like every other line — check them before proposing new work.
- **Order is `priority` in `roadkeep.toml`**, not opinion: `PP39` first (the baseline
  whose window closes), then Block H, then Block I.

## ⛔ READ THIRD — then ask what the public documentation owes the task

The port has a documentation area at [`site/docs/`](../../../site/docs), published at
<https://alegauss.github.io/pportal/docs> (PP446, Block J). It is written for a reader
outside this tree, and the three roadkeep files are not: `CHANGELOG.md` records that a
task shipped and says nothing that somebody integrating against the port could act on.

**So every finished task ends with one more question, asked before `run-commit.cmd`: does
a page under `site/docs/src/content/docs/` now say something false, or is something now
true that no page says?** Answer it in the turn — "nothing there covers this" is a
complete answer, and a page written because the rule exists is worse than no page.

| The task | The pages |
|---|---|
| changed a flag, an interface, a wire format or a behaviour a page describes | **update it in this commit** — a page describing the old behaviour is what this question exists to catch |
| moved a boundary: C that is now managed, a dependency gone, a path replaced | update the page that names the boundary, where one does; otherwise nothing |
| internal only — a test, a refactor, a build script, a rationale, a marker | nothing |

Two rules the pages carry, so an edit does not break them:

- **No figure is typed.** Versions, flag lists and counts are rendered from
  `site/src/lib/product.generated.ts`, which `site/scripts/product.mjs` writes out of the
  application's own source on every build. If a task makes a page want a number, derive
  it; a number typed into a sentence is true the day it is typed.
- **A page is not the rationale file.** `IMPROVEMENTS.md` argues for *unshipped* work and
  a ship deletes it. A page explains what the port does *now*, to somebody who is not
  doing the work — so moving a deleted `§PP<n>` section across verbatim is not the same
  document and reads as one.

The gate for a docs edit is the site's, not `compile.cmd`: `npm run build && npm test` in
`site/`, which builds the area last and asserts the three joins between the two builds.
`SiteDocsAreaTests` holds the same joins from the .NET side, so `test.cmd` catches a
wiring change on a machine with no node.
