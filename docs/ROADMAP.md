# Roadmap (active backlog)

## Priority

- Block H
- Block I

## Block A — Core

## Block B — Native interop

## Block C — Video and input path

## Block D — Screens

## Block E — Windows-only build

- 📋 **PP63** (deps: PP62 ✅) (requires: msvc-qt-webengine) **nothing in the tree can configure a Qt build carrying WebEngine, so PP46's before cannot be produced at all** — MSYS2 has no qt6-webengine and no Windows release carries Chromium, so the reference is built once with MSVC. → §PP63
- 📋 **PP301** (deps: —) (requires: runner) **no MSVC toolchain has ever compiled this tree, so the CI workflow's first push is the first time it is tried** — PP22 configures a runner the only way a runner can be, and everything built here so far came through MSYS2 MinGW64. → §PP301
- 📋 **PP302** (deps: —) (requires: signing-certificate) **nothing signs the host or the installer, so SmartScreen warns on the first run of every release** — PP22 shipped the builds and the packages of its own sentence and not the signs, because that one starts with buying a certificate. → §PP302

## Block F — Managed core

- ⏳ **PP27** (deps: PP672 ✅, PP673 ✅, PP674 ✅, PP675 ✅, PP676 ✅, PP677 ✅, PP678 ✅, PP679 ✅, PP680 ✅, PP702 ✅, PP783, PP784, PP785, PP786) (requires: console) **takion.c is 2007 lines of C over raw sockets and timers, and the whole stream rides on it** — Ten tasks wrote it; four more take the C out. → §PP27
- 📋 **PP30** (deps: PP23 ✅, PP27 ⏳) **forward error correction is two vendored C libraries doing Galois field arithmetic per lost packet** — chiaki_fec_decode has three callers - frameprocessor.c, the C suite and this port's shim - and gf-complete has a fourth site none of them reach: chiaki_lib_init. → §PP30
- 📋 **PP783** (deps: PP787 ✅, PP795 ✅) (requires: console) **session.c runs the stream itself and nothing in app installs the port's phase, so PP295's four files cannot leave** — It runs to the idle loop against a console and draws nothing, which is PP763's failure exactly. → §PP783
- 📋 **PP784** (deps: PP788 ✅, PP789 ✅, PP790 ✅, PP791 ⏳, PP792) **senkusha runs before the stream phase and calls four of takion's exports, so the transport cannot leave with it** — Porting is the call; five lines carry it, and the file stops calling takion when the last lands. → §PP784
- 📋 **PP785** (deps: PP780 ✅) **the shim's eighteen takion symbols are the oracle, so the deletion takes away what proves the port right** — PP33 met this and answered it: a define like the frame path's, and a recording of what the C answers, so the comparison outlives the C. → §PP785
- 📋 **PP786** (deps: PP780 ✅) **three of the C suite's files exercise takion directly and the case floor counts them, so the deletion drops it silently** — PP696 put the suite's four behind a define for the frame path and takion's three have none, so the floor of 149 falls with no line saying it should. → §PP786
- ⏳ **PP791** (deps: PP790 ✅) **nothing implements senkusha's run host or turns its arrivals into the flags its waits end on** — The host is owed: senkusha's six senders have no managed builders, and ManagedTakion exposes no raw send for the pings. → §PP791
- 📋 **PP792** (deps: PP791 ⏳) (requires: console) **session.c runs senkusha itself and there is no callback to hand it to, so a managed run is unreachable** — PP753 built this seam for the stream phase and senkusha needs its own; PP28's placement already says what each outcome decides. → §PP792
- 📋 **PP797** (deps: —) **the console's audio reaches a receiver whose output goes nowhere, and nothing drains the ring into a speaker** — Every piece is written and two joins are missing: the root hands the arms a no-op sink, and the ring the opus decoder fills has no reader. → §PP797
- 📋 **PP798** (deps: —) **one access unit per session is refused by the decoder and the log says which error, never which frame** — A benign first unit before the codec has its header and a real corruption print the same line, so neither can be told from the other. → §PP798
- 📋 **PP799** (deps: —) **the port streams and never says it connected, so a window waiting on the event holds a spinner over a live picture** — The composition root builds an event sink with nothing listening, so every session event the run raises is counted as unheard and dropped. → §PP799

## Block G — Test discipline

- 📋 **PP796** (deps: —) **a stale suite binary is a warning and the floor then fails naming ffmpeg, so it reads as a shrunken suite** — The two are printed by the same run and the second explains the first wrongly; a reader who acts on the message checks a build option instead of rebuilding. → §PP796

## Block H — Performance and telemetry

- ⏳ **PP46** (deps: PP42 ✅, PP63) **the claim that dropping the bundled browser makes startup and the installer smaller is untested** — A Chromium leaving the build should be visible in cold start and in megabytes, and stating it without measuring is how a port collects folklore. → §PP46
- 💭 **PP303** (deps: PP46 ⏳) **PP46's before costs two multi-gigabyte installs for a number about an application this port is not a version of** — PP277 settled that this is a new application and not upstream's next, so a delta against a Qt build compares two products. → §PP303

## Block I — NVIDIA path

- ⏳ **PP49** (deps: PP11 ✅, PP47 ✅, PP700 ✅) (requires: console, a-person-looking) **the console sends SDR on most titles and an HDR display shows it flat, with nothing in the client trying** — the quality half and the integration: a decoded console frame to judge on, and a setting that turns it off. → §PP49
- ⏳ **PP53** (deps: PP11 ✅, PP41 ✅) (requires: variable-refresh-display) **frames arrive with network jitter and are presented against a fixed refresh, so each waits for a vblank it missed** — the reading itself: a display that varies its refresh, and a trace saying the frame arrived unpaced. → §PP53

## Block J — Public documentation

## Done when — PP27

- **A shim entry point exposes takion's receive loop** The half PP531 could not reach.
  The MAC gate is timed because the shim reaches it; the loop around it is bound to
  sockets and threads a capture has neither of, so no oracle runs until an entry point
  exists.
- **The managed transport is timed against the C over captured traffic** PP635: the gate
  is comparable and the loop is not - takion's handlers are file-local, so the only C
  loop that runs is bound to a socket. PP610 timed the gate at 0.165us against 0.101us;
  PP633 replayed the loop over whole datagrams for the half a ratio cannot give.
- **The transport meets PP44's allocation budget** Thousands of small packets a second,
  each an allocation if written carelessly. Span, ArrayPool and SocketAsyncEventArgs are
  the answer, chosen deliberately - PP44 set the budget before this line writes what has
  to meet it.
- **takion.c, takionsendbuffer.c and reorderqueue.c leave the build** An end state, not
  a progress bar: porting into app removes no C, and this cannot land until the three
  criteria above it have. PP763 adds the other half - the frame path's deletion was
  green and left the client unable to stream, so a departure waits on something driving
  a live session, not on a passing gate.

## Done when — PP46

- **The three numbers are recorded on the Qt build** Cold start to the console list,
  installer size, and process working set at idle - alongside the rest of the baseline,
  in the same record the sink already writes. This is the before, and PP63 is what makes
  it buildable.
- **And again on the WPF build, in the same record** A delta needs both halves written
  the same way. These are the two numbers most likely to be quoted in a release note,
  and a quoted number nobody measured survives long past the day it stopped being true.

## Done when — PP49

- **The picture is judged on a decoded console frame, not a synthetic chart** The cost
  half is settled and does not need one: 29.0us follows the resolution. Whether an
  inferred HDR image is BETTER depends on where a real frame's highlights and shadows
  sit, and spike/video-hdr says so rather than implying an answer from a chart.
- **It is a setting that turns off, and a fidelity mode bypasses it** The caution the
  design filed, kept as a condition rather than a hope: an inferred HDR image is an
  opinion about colour the source did not express. Nothing in the present path asks for
  the extension yet, so this is the integration half and it waits on the window owning
  its own swapchain.
- **The setting reads back the effect, not the return code** PP648 measured that the
  toggles are per feature: VSR does not engage on the card where this one does, and
  every call succeeds either way. So a setting switched on in this port has to compare
  pixels the way both spikes do, or it claims to be on for users whose control panel
  says otherwise.

## Done when — PP53

- **A frame-time trace on a varying panel, not a flag DXGI accepted** The shipped half
  is an API answer and PP163 is this tree's record of what one is worth as a prediction
  about a pixel. A composed frame passes through DWM, so whether the panel actually
  follows it needs reading on a display that varies its refresh.
- **The present path asks for the flags, rather than a probe asking on its side**
  Nothing the client presents carries DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING today; only
  chiaki_render_tearing_probe does. Integration means the video plane's own swapchain
  carries it and presents at sync interval zero, which is the half that waits on there
  being a video plane at all.

## Done when — PP791

- **A host in app drives the run and nothing in tests implements it** The arrivals
  landed and the host did not, because senkusha's six senders have no managed builders
  and ManagedTakion exposes no raw send for the pings. Checked as PP745's was:
  SeamReach's row for ISenkushaRunHost leaves, which needs a class in app rather than a
  double in the test project.

## Done when — PP783

- **The flip matches the C path's own baseline, frame for frame** A run on the C path
  with d3d11va gave 1020 decoded, 972 shown and 426 pad states over twenty seconds,
  recorded in chiaki_baseline.jsonl. PP763 shipped on a green gate because nobody had a
  number; the flip is checked against that one, by compare-baselines rather than by
  looking.

## Non-goals

- **No Linux, macOS, Android, FreeBSD or Switch build** Those trees are already deleted
  and the target framework is Windows-only by construction, so a line proposing to keep
  one portable is proposing a second application.
- **No cross-platform UI toolkit as a hedge** Avalonia or MAUI would keep the port
  portable and give back none of the Win32, DXGI and WebView2 access the screens depend
  on, which is the whole reason WPF was chosen.
- **No redesign while porting** A screen that changes shape in the same commit that
  changes framework cannot be judged against the one it replaced, so behaviour is
  reproduced and improvements are filed apart.
- **No line ships without an assertion that fails without it** A test written after the
  fact asserts what the code does instead of what it should do, so it lands in the same
  commit as the line it holds or the line is not shipped.
- **No GPU vendor feature for the network path** Nothing NVIDIA ships touches a UDP
  socket, so the connection is improved by transport work and congestion control or not
  at all, whatever the card is.
- **No vendor path whose absence is visible to the user** First is not only: a machine
  with no NVIDIA card keeps d3d11va decode, a neutral renderer and an SDR present, and a
  feature that is not there is not in the menu rather than explained in a dialog. The
  floor and what actually covers it are in docs/HARDWARE-CONTRACT.md.
- **No local patch to the vendored C** Every drift check asserts the managed side
  matches lib/, so a patch leaves them agreeing with a libchiaki nobody runs. PP107
  argues it. Not PP33's deletion, PP30's port or PP295's: a deletion removes what they
  agree with, and a port leaves the vendored source alone.
- **No managed video decoder** Nothing in .NET decodes H.264 or HEVC at 1080p60 and
  remote play latency, and writing one would ignore the GPU already doing it for free.
  The reachable goal is a port that is 100% Windows and builds in Visual Studio, not one
  that is 100% managed - and this is where the difference is.
