# PP46 — cold start, build size, idle working set

```
measure-startup --exe <path-to-exe> [--tree <dir>] [--runs 3] [--out report.json]
                [--cache-state cold-boot|dropped|warm]
measure-startup --self-test
```

Exit `0` measured with QtWebEngine present · `2` measured but **WebEngine absent, so this is not the
"before"** · `1` **no cold-start number** — either nothing was measured, or run 1 failed and the
headline figure is missing. `1` outranks `2`: a row without a cold start is not a measurement of a
startup, whichever build it came from.

These are the numbers most likely to be quoted in a release note, which is why they are the ones
most likely to be quoted without being measured. One command produces all three, so the before and
the after are taken the same way.

## `--cache-state`, and why the cold number needs it (PP61)

Run 1 is called the cold start and it is one only on a machine that has not launched this
executable before. The OS file cache outlives the process: after one launch the loader, the Qt
plugins and the QML cache are resident, so run 1 of the *next* invocation is a warm start wearing
the cold label. The same build measured 3771 ms and then 1218 ms, and nothing in the report said
which was which.

The harness cannot observe this for itself, so the caller states it:

- `cold-boot` — the machine was rebooted and this is the first launch since.
- `dropped` — the standby list was dropped before run 1. Needs elevation; a decision, not a default.
- `warm` — this executable has run before on this boot. Run 1 is not cold.

Left unset it is written as `unknown`, the report says `"cold_is_comparable": false`, and the
command prints a warning. **Unknown compares with nothing — not even with another unknown**, since
two reports that both declined to say are not thereby in the same state.

## The state of this task

**The "before" has not been taken.** `mingw-w64-x86_64-qt6-webengine` is not installed on the
machine this was written on, so `CHIAKI_HAVE_WEBENGINE` is off, no Chromium is in the tree, and
`QtWebEngineQuick::initialize()` ([`gui/src/main.cpp`](../../gui/src/main.cpp), guarded) never runs.
Measuring here and calling it the before would be publishing a number for a build that had already
dropped the thing being measured — exactly the folklore §PP46 exists to prevent.

So the harness ships now and the before is taken on a machine that has the package. The tool refuses
to let a row be misread: it scans the tree for Chromium and stamps `webengine_present` and
`is_before_baseline` into the JSON, and returns exit 2 when they are false.

### What was measured here (Qt build, WebEngine ABSENT — not the before)

Row in [`qt-build-webengine-absent.json`](qt-build-webengine-absent.json).

| | |
|---|---|
| tree | **259.8 MB** in 1490 files |
| chromium in tree | **none** |
| cold, to responsive window | **1214 ms** |
| warm, to responsive window | 1102 ms (median of 2) |
| idle working set | **433.0 MB** (median of 3) |
| window title seen | `chiaki-ng` |

Useful as a floor and as proof the harness works, and nothing more. When the before is taken, the
delta against these figures is the cost of everything *except* WebEngine, which is not a number
anybody asked for.

## Three things the numbers are not

1. **"Cold" means first run of this invocation, not a cold machine.** The very first run after a
   build measured **3771 ms**; every later invocation reports ~1200 ms because the loader, the Qt
   plugins and the QML cache stay in the OS file cache. A genuinely cold figure needs a fresh boot or
   a dropped cache. The tool reports run 1 apart from the rest rather than taking a median over all
   of them, because a median over three runs is two warm starts with one cold one dragged into the
   middle.
2. **"To the console list" is not what is timed.** What is timed is the first visible top-level
   window with a real client area, then that window answering `WM_NULL`. Reaching the console list is
   an application-level event and this harness is outside the application; claiming it would claim
   more than was observed. The window title is recorded so a reader can tell the app from a modal
   error box — a dialog is a visible top-level window too, and timing one would report a cold start
   for a build that never started.
3. **Installer size is not measured; tree size is.** An Inno Setup script exists
   ([`scripts/chiaki-ng.iss`](../../scripts/chiaki-ng.iss)) but nothing in the tree builds an
   installer since the CI workflows were deleted (PP22). The deploy tree is the honest proxy, and it
   is labelled as the tree.

## A zero that got past the probe

The probe never invented a time, and the report did it anyway. The runs are summarised **after** the
probing, and that step took run 1 as the cold figure by position without checking that run 1 had
produced one — so a failed run 1 was serialised as `"cold_to_responsive_ms":0.0`, in a row that
otherwise looked complete and exited `2` like any other.

It is not a corner. Run 1 is the *slowest* run by construction, so any timeout tight enough to catch
a hang lands between the cold time and the warm ones and fails exactly run 1. Observed on this
build at `--timeout-ms 1235`:

```
run 1 (cold): FAILED - no visible top-level window within 1235ms
run 2 (warm): window 1153 ms   responsive 1169 ms
run 3 (warm): window 1084 ms   responsive 1117 ms
...
"runs":2,"cold_to_window_ms":0.0,"cold_to_responsive_ms":0.0,"warm_to_responsive_ms_median":1117.4
```

Two wrong numbers in one row. The cold start is `0.0` rather than absent, and the warm median counts
**one** later run out of two — the summary skipped the first *successful* run rather than run 1, so
run 2 fell out of both figures.

The same command now reports `"cold_measured":false`, `null` in both cold fields, a `cold_failure`
saying why, `warm_runs:2`, and exits `1`. A run that produced no time contributes no time; run 2 is
never promoted into the gap, because run 2 is a warm start whatever happened before it.

[`Report.cs`](Report.cs) exists so that step can be asserted without launching anything.

## Assertions

`--self-test` checks the failure modes that would otherwise produce a quotable wrong number:

- a process that stays alive and shows no window is reported as showing none, **never as 0 ms**
- a missing exe is refused rather than timed
- Chromium detection is exercised against a tree built with it and one built without, including that
  `qtwebengine_resources.pak` counts (matching only the DLL would undercount by an order of
  magnitude) and that an ordinary `Qt6Quick.dll` does not
- the summary, driven with synthetic runs: a failed run 1 serialises `null` and never `0`, carries its
  reason, exits `1` even on a WebEngine build, does not let run 2 stand in as the cold figure, and
  still counts both later runs as warm — plus the all-succeeded case, so those cannot pass by
  breaking the ordinary path

Injected faults, and what each produced:

| fault | red checks |
|---|---|
| the no-window branch returns `(WindowAppeared: true, 0 ms)` | **0 at first** — the check used `cmd.exe`, which exits the moment its stdin reads EOF, so it exercised the "exited early" path instead. Switched to `ping -n 6`: **2**. The check that the no-window case is not reached via early exit exists because of that miss. |
| `cold` taken from `results[0]` without checking it succeeded | **6**, and the JSON in the failure output is byte-for-byte the `"cold_to_responsive_ms":0.0` row above |
| warm runs taken as `measured.Skip(1)` instead of `results.Skip(1).Where(ok)` | **1** — `both later successful runs must be warm, got 1` |
