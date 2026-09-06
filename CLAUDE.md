# pportal — always-on rules

The roadmap workflow (add / ship / retire, the gate, the one-task-one-commit rule, what the
public docs owe a task) lives in the `pportal-roadmap-docs` skill. **Invoke it before working
any PP task.**

This file holds only what must be known *before the first tool call* — traps where the obvious
action is wrong and the failure looks like something else. Every rule here was paid for.

## Build and test: PowerShell only

`compile.cmd`, `test.cmd` and `package.cmd` run through the **PowerShell tool, never Bash.**
The Bash tool's PATH puts Visual Studio's `clang.exe` ahead of MSYS2's gcc; cmake reconfigures
with it, rewrites `build/CMakeCache.txt`, and fails with `No CMAKE_RC_COMPILER could be found`.
The cache stays poisoned, so the next PowerShell run fails identically.

**Recovery is not `clean`.** From the repo root:

```
Remove-Item build\CMakeCache.txt -Force
Remove-Item build\CMakeFiles -Recurse -Force
```

then `compile.cmd` — seconds, `ninja: no work to do`. A `clean` additionally deletes
`build/chiaki-ng-package` and the installer, which nothing rebuilds, turning
`DriftCorpusTests` red for an unrelated-looking reason; run `package.cmd` after any `clean`.

**Something outside this session poisons the cache on its own** (an IDE cmake kit). Check
`build\CMakeCache.txt` for a `CMAKE_C_COMPILER:STRING=` entry — `STRING=` rather than
`FILEPATH=` — **before every build**, not just at session start. Its other symptom does not
look like a build problem at all: one red `CtrlAssertBoundsTests.TheConfiguredBuildCompilesAssertsOut`
means "read CMakeCache.txt", not "somebody changed ctrl.c".

**Never chain two native commands in one PowerShell call.** `compile.cmd; test.cmd`, or either
piped into `Select-String`, has twice aborted mid-run with "Falha no processo do host de teste".
Re-run alone before believing either result.

**`compile.cmd` does not build `gui/`.** `-DCHIAKI_ENABLE_GUI=OFF` is passed on every
configure, so a `gui/` edit commits unbuilt and the gate is green on a file that does not
compile. To build it: `$env:CHIAKI_ENABLE_GUI='ON'; .\compile.cmd nodeploy`, then a plain
`compile.cmd` to put the tree back.

## Writing a file: the most expensive mistake in this repo

**`Write` on an existing path reports "updated successfully" and that word is the only
warning.** It has destroyed ~700 lines across four occasions; one of them shipped. A
`PreToolUse` hook now refuses `Write` over an existing non-empty file — if it fires, that is
the trap, not a false alarm. Use `Edit`.

Before creating any `app/Protocol/*.cs` or `app/Session/*.cs`:

1. **`roadkeep delivered <block> --near "<the sentence you would file>"`** — the five nearest
   deliveries in that block, ranked. This is the read that exists for exactly this question,
   and nothing in this repo's workflow was calling it: for the sentence PP736 was about to
   propose, PP524 — the port it duplicated — comes back **rank 1**. An order, not a verdict;
   you still read it.
2. **Then search the CHANGELOG by the BEHAVIOUR, never by the class name you intend to use.**
   The port's classes are named for behaviour (`PortGuessing`, `OfferAck`), never for the C
   function they came from, so grepping `app/` for a C name always answers "unported".
   Grep the concept, a distinctive literal, a `#define`'s value, an offset.
3. **List the directory filtered by the name's first word.** ~35 `Session*.cs` classes exist;
   an obvious name is usually taken.
4. **Read the `near` list `roadkeep add` prints.** It runs the same search — but only after
   the id is minted, which is why PP736 was caught after its commit rather than before it.

**Grep is the wrong instrument for a TYPE question.** A line-oriented pattern cannot see a
primary-constructor class whose base list sits on a continuation line — it got 3 of 7 answers
wrong once. Use reflection (`app.GetTypes()` + `IsAssignableFrom`), as `SeamReach` does.

**A falling test total is a destroyed file until proven otherwise.** `git diff --stat` names it.

## Counting lines

Counted claims are LINE ITEMS: `wc -l` **plus one per file with no trailing newline**.

- `ChiakiNg.exe --recount` is the answer, and it prints the `roadkeep` call that fixes each
  claim. Run it **before writing any number**, not after.
- Run it from `app\bin\Debug\...`, **never `...\Release\...`** — the Release copy is whatever a
  publish last left and has been 44 commits stale while answering all session.
- `Measure-Object -Line` skips empty lines (reported `ctrl.c` as 1552; it is 1713).
  `wc -l` undercounts files with no trailing newline.
- `lib/src` has a `remote/` subdirectory — a `lib/src/*.c` glob silently misses 4 files and
  7103 lines. 46 `.c` files recursively, not 42.
- `--recount` reads only the governed docs. A count typed into a C# docstring is unchecked, so
  keep source comments qualitative ("a timer and a clamp", not "66 lines").

**Never round-trip a source file through `Get-Content -Raw` + `Set-Content`.** PS 5.1 reads a
BOM-less UTF-8 file as ANSI; `§` comes back as `Â§`. Use `Edit`. `Select-String` displays the
same way, so check `git diff` before believing a file is damaged.

## roadkeep

- **Never write a `PP<n>` before `roadkeep add` returns it.** Any id in the project's prefix
  reads as spent, and `next-id` derives past it. This cost PP471 outright — it exists nowhere.
  File first, then write the id into code and section.
- **After every `ship`, run `git status --porcelain -- roadkeep.toml`.** Engine 0.1.1104
  silently deleted the whole `[requirements]` table as a side effect of `ship`; not retested on
  0.2.148, and the check is one command. If modified, `git checkout -- roadkeep.toml` and
  re-run `lint`. Losing that table breaks every line naming `console`, `runner`,
  `signing-certificate` — `pick` starts offering unstartable work again.
- **Start a task with `brief`, not by reading the governed files.** `lint` is the gate.
- **`pick` under-reports until you declare what you have.** PS5-385 is real and registered, so
  `roadkeep pick --have console` is the honest call — a bare `pick` hides ready lines under
  `absent`, and on 2026-09-06 that made the *live critical path* (PP783) look blocked. Check
  `[requirements] declared` in `roadkeep.toml` first: `console` is pruned whenever nothing
  waits on it, and **in that gap `--have console` is a silent no-op** — neither an error nor a
  changed answer, which is worse than either.
- **`stats` "waiting N" is not "N blocked".** It counts lines that *declare* a requirement.
  `pick` is the one that answers what is ready; read its `backlog` row, not `stats`.
- **A console requirement is not satisfied by a console that is off.** Any `--capture-*` flag
  needs PS5-385 in REST MODE with remote play enabled; there is no wake-on-LAN ahead of
  discovery. Ask the user to put it in rest mode rather than retrying, and never read a failed
  capture as the requirement being false.
- **A deferral needs a re-entry trigger and a review date.** "Set aside" without the condition
  that revives it goes stale silently — one deferral's stated reason had been false for months.
- **`roadkeep claim <id> --path <p>` declares what a task will touch; `roadkeep claim <id>`
  with no `--path` reads it back and names what the tree holds that ANOTHER live claim owns.**
  That is the analysis `git add -A` cannot make, and it is the check to run immediately before
  committing. A claim is dated by a marker write and released when the marker moves
  (`[claims] held = 60` minutes); `roadkeep claims` lists held, expired and stale. On
  2026-09-06 two sessions were live here, `claims` reported **0 held**, and `5755f959`
  committed four files of unrelated work under its own title.
- **The five governed files merge structurally** — `roadkeep merge` is registered, so two
  sessions appending under one heading is two additions and not a conflict. `roadkeep merge
  --check` reads the wiring back. The `.gitattributes` half is committed; the `git config`
  half is per-clone, so **a fresh clone must run `roadkeep merge --register` again** or a
  conflict silently falls back to git's text markers.

## Filing a defect in `lib/`

**A suspected defect that resembles an already-fixed one is not evidence.** Of four filed from
resemblance, three needed correction, and only reading the code found them.

**File the question, not the fix.** One line that asks and carries the census a later answer
needs; the fix is a second line, written after the reading. The assertion written for the
question then inverts when it lands.

**Search before filing** — `docs/CHANGELOG.md` for the behaviour, then `app/` for the concept.
Two greps, both cheap; skipping them shipped a duplicate reader and a duplicate drift check.
Two drift checks over one file is worse than duplicated ordinary code: an upstream repair has
to turn both red, and they diverge the first time only one is updated.

**An unreachable path is not a defect.** Filing one overstates it and spends a commit on no
behaviour change. `docs/TRACED.md` lists what has already been traced to
unreachable-or-correct — **read it before filing**, and if a sweep surfaces one of those again,
report it as already traced rather than re-tracing it. If the *premise* that makes it
unreachable changes, it becomes real and is worth filing then.

## Committing

`run-commit.cmd` runs `git add -A`, and this repo often has **more than one session writing at
once**. The one-task-one-commit rule assumes one writer; nothing in the tool or the hooks
notices a second.

- **`git log -1` before starting a task, and again right before committing.** HEAD moved → an
  intervening commit probably carries part of your work. Say so; do not rebase or split while
  the other session is live.
- **`git status` showing files you did not touch** → `git stash push -u -- <paths>` *before*
  calling run-commit, then pop and commit them under their own message. Reading the status
  output is not enough; it has to change what you do next.
- **A failed `run-commit` leaves the tree STAGED, not committed** — an OpenAI error kills it
  after `git add -A`. That reads like a normal mid-task tree. Check `git log -1`, then retry
  with the same `-m` title. Never move to the next task on a failed commit.
