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

- ⏳ **PP27** (deps: PP672 ✅, PP673 ✅, PP674 ✅, PP675 ✅, PP676 ✅, PP677 ✅, PP678 ✅, PP679 ✅, PP680 ✅, PP702 ✅, PP783 ✅, PP784, PP785 ✅, PP786 ✅) (requires: console) **takion.c is 2007 lines of C over raw sockets and timers, and the whole stream rides on it** — Ten tasks wrote it; four more take the C out. → §PP27
- 📋 **PP30** (deps: PP23 ✅, PP27 ⏳) **forward error correction is two vendored C libraries doing Galois field arithmetic per lost packet** — chiaki_fec_decode has three callers - frameprocessor.c, the C suite and this port's shim - and gf-complete has a fourth site none of them reach: chiaki_lib_init. → §PP30
- 📋 **PP784** (deps: PP788 ✅, PP789 ✅, PP790 ✅, PP791 ✅, PP792 ✅, PP805 ✅, PP809 ⏳) (requires: console) **senkusha runs before the stream phase and calls four of takion's exports, so the transport cannot leave with it** — Porting was the call; its lines shipped and takion loses its last caller when this does. → §PP784
- 📋 **PP807** (deps: —) **the port never sends the data ack the C sends after a drained message, so nothing tells the console it arrived** — WriteAck has no caller outside the tests, and PP805 found it while wiring the arm that reads the console's acks - the same message going the other way. → §PP807
- 📋 **PP808** (deps: —) **nothing raises a takion disconnect, so a wait on a transport that died runs its whole timeout** — Senkusha's disconnect arm is reached by tests alone after PP805, and the stream's connect wait has the same silence PP366 named in the C. → §PP808
- ⏳ **PP809** (deps: PP792 ✅, PP805 ✅, PP813) (requires: console) **no path installs a senkusha phase, so every session still measures the link with the C's run** — A live run measured through it and the phase stalled at the bang, so the numbers read back are the C's fallback; PP813 carries the census. → §PP809
- 📋 **PP810** (deps: PP797 ✅) **the render pump plays as much of a unit as the engine had room for and drops the rest of it** — The dequeue is unconditional and the copy is clamped, so a full engine costs whatever did not fit; PP797 made it reachable by keeping the sink deliberately full. → §PP810
- 📋 **PP811** (deps: PP800 ✅) **the ported send path has no caller, so a live takion stamps its MAC and sends with no lock across the two** — PP675 wrote the order the C holds one mutex over, and ManagedTakion's four sends do it inline instead; PP800's census is what found the seam had only a test on it. → §PP811
- 📋 **PP812** (deps: PP801 ✅) (requires: console) **the datagram capture taps the C's takion and the port opens its own, so a run without a relay records nothing** — The tap watches a socket no datagram reaches now; PP785 keeps the C's answers as a recording, and this flag is how one is made. → §PP812
- 📋 **PP813** (deps: —) **the port's senkusha stalls awaiting the bang on a live link, so every session streams on fallback mtu and rtt** — The C measured 7.030ms and MTU 1453 on that link minutes earlier; the phase saw 54 arrivals and handled 4, so the wire is up and an arm is silent. → §PP813

## Block G — Test discipline

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

## Done when — PP809

- **A live session measures through the port and the numbers are read back**
  --show-stream against PS5-385 prints a [senkusha] line: the rung the run reached, how
  many arrivals an arm acted on, and what the session then carries. A rung short of
  MtuOutMeasured, or 1454/1454/1000, is PP28's fallback and says the port measured
  nothing. The console has to reach ready first, which it does not today.

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
