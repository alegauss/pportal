# PP46 — cold start, build size, idle working set

```
measure-startup --exe <path-to-exe> [--tree <dir>] [--runs 3] [--out report.json]
measure-startup --self-test
```

Exit `0` measured with QtWebEngine present · `2` measured but **WebEngine absent, so this is not the
"before"** · `1` could not measure.

These are the numbers most likely to be quoted in a release note, which is why they are the ones
most likely to be quoted without being measured. One command produces all three, so the before and
the after are taken the same way.

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

| | |
|---|---|
| tree | **259.8 MB** in 1490 files |
| chromium in tree | **none** |
| cold, to responsive window | **1218 ms** |
| warm, to responsive window | 1105 ms (median of 2) |
| idle working set | **434.4 MB** (median of 3) |
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

## Assertions

`--self-test` checks the failure modes that would otherwise produce a quotable wrong number:

- a process that stays alive and shows no window is reported as showing none, **never as 0 ms**
- a missing exe is refused rather than timed
- Chromium detection is exercised against a tree built with it and one built without, including that
  `qtwebengine_resources.pak` counts (matching only the DLL would undercount by an order of
  magnitude) and that an ordinary `Qt6Quick.dll` does not

Injected fault: the no-window branch made to return `(WindowAppeared: true, 0 ms)`. It first went
**undetected**, because the self-test used `cmd.exe`, which exits the moment its stdin reads EOF and
so exercised the "exited early" path instead. Switching that check to `ping -n 6` made the fault
produce 2 red checks. The check that the no-window case is not reached via early exit exists because
of that miss.
