---
name: pportal-roadmap-docs
description: How to work a task in this port's roadmap — the three docs/ files (ROADMAP.md, CHANGELOG.md, IMPROVEMENTS.md) owned by the roadkeep CLI and never hand-edited, and above all the one-task-one-commit rule: every finished roadmap task ends with `run-commit.cmd -m "<title>"` from the repo root, code plus doc sync in that single commit. Use whenever adding a task, picking the next PP-number, marking a task shipped, retiring, linting, editing any of those files, executing a block or a list of PP ids, or finishing any task that touches this repo.
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

The same rule applies to any finished unit of work in this repo, roadmap task or not:
when the work is done and validated, commit it with `run-commit.cmd -m "…"` rather than
leaving it in the tree.

## The gate before the commit

Validated means the build was run, not that the edit looked right —
[`compile.cmd`](../../../compile.cmd) is the only build this tree still has:

| Command | When |
|---|---|
| `compile.cmd configure` | fast check after a deletion — does every path the build graph names still resolve |
| `compile.cmd` | full build + portable tree — **before committing** |
| `ctest` in `build/` (target `chiaki-unit`, test `unit`) | anything touching `lib/` or `test/` |

A configure that passes is not a target that links. Run the full build before the
commit, not before the decision.

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
