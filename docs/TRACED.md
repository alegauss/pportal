# Traced — investigated, not a defect

Each entry below looked like a defect, was traced to the end, and turned out to be unreachable
or already correct. **Read this before filing a defect in `lib/`.** If a sweep surfaces one of
these again, report it as already traced rather than re-tracing it — the tracing is the
expensive part, and this loop re-reads these files every few ticks.

This file is not governed by `roadkeep`: nothing here is a task, and an entry is not work
anybody owes. It exists because the port's discipline is that **an unreachable path is not a
defect** — filing one overstates it and spends a commit on no behaviour change — and because
that conclusion was, until now, recorded nowhere a second machine or a second person could
read.

**An entry reopens when its premise changes, not when the code is read again.** Each one below
names the premise that makes it harmless. A seventh `ChiakiTarget`, a new caller setting
`target` without the unknown guard, a shim that stops wrapping a leaf — that is when the
finding becomes real and is worth filing. Add the premise when you add an entry; an entry that
only says "checked, fine" cannot be invalidated and will be re-traced.

## Unreachable or correct as written

*Traced between 2026-08-21 and 2026-08-27.*

- **`session.c:970`** — the `rp_version_str == NULL` path returns without closing
  `session_sock`, while its neighbour nine lines up (`format_hex` failure, 954-963) closes it
  explicitly. A real inconsistency, and **unreachable**: `ChiakiTarget` has six values,
  `chiaki_target_is_unknown` excludes two, and the remaining four are exactly the four
  `chiaki_rp_version_string` returns a string for. `session->target` is only ever set at line
  178 (PS5_1 or PS4_10) or at 501/510 behind `!chiaki_target_is_unknown(server_target)`.
  **Premise:** those six values and those two assignment sites.
- **`session.c:1108`** — `rp_version_str ? rp_version_str : ""` is dead defensiveness; line 966
  already returned if it was null. Harmless redundancy.
- **`takion.c:1201` and `takion.c:629`** — guarded elsewhere.
- **`rpcrypt.c` `bright_ambassador`** — `session->target` is never `PS4_UNKNOWN` there.
- **`session.c:613`** — a dead store.
- **The unpaired HOLEPUNCH event.**
- **SDL3 in the package** — the sweep showed `SDL2.dll -> SDL3.dll` and SDL2 contains the
  literal string; two `ldd` runs disagreed and could not be reconciled. Nothing was built on
  it. Related: the `lcms`/`vulkan` vcpkg entries are declared and never asked for, which is
  legitimate for a transitive pin and should not be filed as dead weight.

## Cleared by measurement, or by reading the reason rather than the code

*Traced 2026-08-30.*

- **`ctrl.c` and `session.c` falling back to different ports** — `SESSION_CTRL_PORT` and
  `SESSION_PORT` are two names for **9295**. No behavioural trap.
- **`pserr` discarded in the punch** — it is acted on: `err = pserr` unless SUCCESS or TIMEOUT,
  and the forgiven timeout is PP498's own finding.
- **`--selftest` missing PP530's staleness guard** — deliberate and documented: it is run *by*
  the gate, from the host the gate just built.
- **`compile.cmd gui` printing "Failed to find required Qt component WebEngineQuick" while
  reporting OK** — `gui/CMakeLists.txt:11` asks for it *without* `REQUIRED` and line 71 guards
  on `_FOUND`. CMake's phrasing, not a broken build.
- **`holepunch-test.c` compiled into the shipped library** — it is a separate `add_executable`,
  not library dead weight.
- **The C suite having orphan or unregistered tests** — 16 suites declared, 16 externed in
  `test/main.c`, all in the suites array; no `test/*.c` sits outside `test/CMakeLists.txt`.
- **The shim ABI join being unasserted** — `SelfTest` compares `AbiVersion()` against
  `ExpectedAbi` and `ChiakiNative` throws at load; the gate runs the selftest.
- **The site being stale** — `npm test` 25/25, `npm run build` exit 0, no drift in the
  generated `product.generated.ts`.
- **The two failing interaction tests** — they say why themselves: *"no pad SDL can map"*.
  Hardware absent, failing loudly by design, and outside the gate (PP227).

## Open lines whose claims were swept

*Swept 2026-08-30. PP573 and PP574 found two lines whose caller counts shipped work had
falsified — PP33 said one caller and has four, PP30 said one and has three; the shim was the
missed one both times. The rest came back clean.*

- **PP27** — "the loop around it, which no shim entry point exposes" **holds**. The shim wraps
  seven takion entry points and all are leaves (`packet_mac`, `format_congestion`, four
  `send_buffer_*`, `v9_av_packet_parse`). `chiaki_takion_connect` is exported and not wrapped.
- **PP295** — "this is what still calls the native receiver" **holds**; `streamconnection.c`
  calls four `chiaki_video_receiver_*` functions.
- **PP32** — "the conversion between them is `SDL_AudioCVT` rather than speex" **holds**;
  `SDL_AudioCVT`/`SDL_ConvertAudio` are live in `gui/streamsession.{h,cpp}`. This one looked
  likely to be stale given the unreconciled SDL2/SDL3 note above — it is not.
- **PP322** — `--dcomp-demo --layers` resolves to `Views.DcompDemo.RunLayers()`, so the line is
  runnable the moment somebody looks.
- **`GuiFreshness` being a model nothing runs** — it is run: `GuiFreshnessTests` calls `Check()`
  against the real checkout and `Assert.Fail`s, inside the gate's xUnit pass.

## Comment-stripping in drift checks — not a defect

*Swept 2026-08-30.*

153 of 185 source-reading classes in `app/` do not go through `CCall.Code()`. That number looks
alarming and is fine: their anchors are code-shaped strings no comment would contain. Sweeping
every code-shaped literal in `app/` against every `lib/src/*.c` found **five** anchors present
only inside comments, and all five are correct:

- Three are false positives of the sweep — the literal itself *ends with* a `//` comment
  (`CtrlRudpSubtypes`, `TakionHandshake`, `StunLookup`), so any stripper removes it. Two of
  those deliberately assert upstream's own commented text.
- The two real ones, `srand((unsigned int)time(NULL))` and
  `assert(mutex_err == CHIAKI_ERR_SUCCESS)`, sit in **past-tense** C comments describing
  behaviour that was later changed — the exact trap. Both checks strip comments first, citing
  PP400 and PP403 by name.

**Do not file "153 classes should strip comments".** PP399, PP400 and PP401 each hit this once,
PP403 made stripping the habit, and the habit holds where the risk is real.
