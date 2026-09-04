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

- ⏳ **PP27** (deps: PP672 ✅, PP673 ✅, PP674 ✅, PP675 ✅, PP676 ✅, PP677 ✅, PP678 ✅, PP679 ✅, PP680 ✅, PP702) (requires: console) **takion.c is 2007 lines of C over raw sockets and timers, and the whole stream rides on it** — Its ten tasks are the managed transport; after them, the three files leave the build. → §PP27
- 📋 **PP30** (deps: PP23 ✅, PP27 ⏳) **forward error correction is two vendored C libraries doing Galois field arithmetic per lost packet** — chiaki_fec_decode has three callers - frameprocessor.c, the C suite and this port's shim - and gf-complete has a fourth site none of them reach: chiaki_lib_init. → §PP30
- 🛠 **PP295** (deps: PP297 ✅, PP696, PP697) **streamconnection.c is 1540 lines and calls the video receiver, so every deletion below waits on it** — Three criteria are met; the fourth is the four files leaving, which waits on the one commit that edits the C and on the shim, whose wrappers outlive it. → §PP295
- ⏳ **PP671** (deps: PP696) **Fec.Recovers with no decoder named runs the C, so after the flip a default becomes a loader failure** — The managed decoder is the one that stays; the default should follow it on the flip, so the sixty-four recorded cases judge the port alone. → §PP671
- 📋 **PP696** (deps: PP707) **the frame path's deletion has no commit that edits the C, so four files stay while their ports exist** — PP623's middle step is the only one touching lib, and nobody has written this path's: session.c's asks, the shim's wrappers and the suite's four files all still name them. → §PP696
- 📋 **PP697** (deps: PP696) **after the frame-path flip the models describe a C that has gone, in the present tense** — PP634 found this on the holepunch side: the predicates stay because they notice the calls coming back, and what goes stale is the prose around them. → §PP697
- 📋 **PP702** (deps: —) **senkusha.c calls five takion symbols, so PP27's fourth criterion cannot be met while the file stands** — PP638 counted the frame path's callers, not senkusha's; the v7 formatter is one of five, and nothing in the backlog ports the file or answers its calls. → §PP702
- 📋 **PP703** (deps: PP680 ✅) **ManagedTakion's video queue is only ever set to null, so one step of its recorded teardown is unreachable** — PP678 recorded the order and PP680 built the arm that opens the queue; nothing joins them, so a step the C always takes is asserted by nobody. → §PP703
- 📋 **PP706** (deps: PP694 ✅) **the microphone has a capture, a unit splitter, an encoder and a head, and nothing runs them as one path** — PP652, PP676 and PP694 each built a piece and each is driven by tests alone; audiosender.c is still the only thing that composes them, and nothing managed does. → §PP706
- 📋 **PP707** (deps: —) **nothing managed drives a live session, so the flip that stops session.c asking removes the only path that streams** — StreamRun starts the C session and ManagedStreamRun is constructed by tests alone; PP696 would still link, and the application would have no way to show a picture. → §PP707
- 📋 **PP708** (deps: —) **nothing in the port renders audio, so a session shows a picture and plays no sound at all** — PP698 had to generate a tone to prove a loopback reference works, because no code here plays one; AudioRing is a model whose only consumer is the selftest. → §PP708

## Block G — Test discipline

- 📋 **PP704** (deps: PP683 ✅) **FeedbackPayloadTests guards eight comparisons on an oracle the census does not name, and it is not the only file** — PP676's oracle arrived after PP665 wrote the list and four shape files decline too, so the printed floor is short by more than the host's one row. → §PP704
- 📋 **PP705** (deps: PP691 ✅) **four sweeps over app/ hand-write their own exclusion, so a new census has to be added to the others by hand** — PP691 needed two edits in files it does not own; nothing says which files record a phrase in order to judge it, so the next census needs the same two. → §PP705

## Block H — Performance and telemetry

- ⏳ **PP46** (deps: PP42 ✅, PP63) **the claim that dropping the bundled browser makes startup and the installer smaller is untested** — A Chromium leaving the build should be visible in cold start and in megabytes, and stating it without measuring is how a port collects folklore. → §PP46
- 💭 **PP303** (deps: PP46 ⏳) **PP46's before costs two multi-gigabyte installs for a number about an application this port is not a version of** — PP277 settled that this is a new application and not upstream's next, so a delta against a Qt build compares two products. → §PP303

## Block I — NVIDIA path

- ⏳ **PP49** (deps: PP11 ✅, PP47 ✅, PP700 ✅) (requires: console, a-person-looking) **the console sends SDR on most titles and an HDR display shows it flat, with nothing in the client trying** — the quality half and the integration: a decoded console frame to judge on, and a setting that turns it off. → §PP49
- ⏳ **PP52** (deps: PP32 ✅, PP652 ✅, PP698 ✅) **nothing runs echo cancellation, and the vendor answer is absent on a machine with the card** — Nothing cleans a sample: the in-box DSP takes two inputs in filter mode and the second, a reference of what is playing, has no capture yet. → §PP52
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
  a progress bar: porting into app removes no C, and takion.c cannot leave until PP295
  has landed, streamconnection.c being one of the six files PP638 counted as calling
  takion. The three files' sizes are stated in the section, where the recount reaches
  them.

## Done when — PP46

- **The three numbers are recorded on the Qt build** Cold start to the console list,
  installer size, and process working set at idle - alongside the rest of the baseline,
  in the same record the sink already writes. This is the before, and PP63 is what makes
  it buildable.
- **And again on the WPF build, in the same record** A delta needs both halves written
  the same way. These are the two numbers most likely to be quoted in a release note,
  and a quoted number nobody measured survives long past the day it stopped being true.

## Done when — PP295

- **The stream connection's event ordering is ported, not only its functions** Met.
  PP640 stated six orderings as checks on the C, ManagedStreamRun.Run reproduces all six
  in one trace, and PP689 added the pad info's own five - decided after its switch so
  both layouts share it. The failure this names is a port right about every function and
  wrong about the sequence.
- **The managed video receiver is driven by the ported stream connection** Met. PP667's
  dispatch drives it, PP684 gave its outbound seam its first non-test implementation so
  the corrupt frame and the IDR request reach a sink as bytes, and PP686 hands it the
  profiles a console announced rather than headers a test wrote.
- **Every consumer PP638's linker run named has a counterpart** Met by PP669:
  session.c's five, the shim's thirteen and the suite's four each resolve to a managed
  class by reflection, and a call with no row or a row with no call fails by name.
  Seventeen was the count before it was measured; the mapping is what the criterion
  asked for.
- **streamconnection.c, videoreceiver.c, frameprocessor.c and fec.c leave the build** An
  end state, not a progress bar, and the order is PP623's and PP655's: the counterparts
  first, which PP669 mapped; then the one edit that stops session.c asking, which PP638
  measured. That edit is PP696, so this cannot land until PP696 has. Porting into app
  removes no C.

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

## Done when — PP52

- **Both paths are read from the machine before either is integrated**
  spike/audio-effects reports whether the vendor SDK is reachable and whether the in-box
  Voice Capture DSP is registered, with the evidence for each so a no is refutable. A
  model reads its committed file rather than restating the numbers, and names what each
  path would ship.
- **Something actually cleans the captured samples** A stage sits between the capture
  and the encoder and is read back rather than assumed to have run, which is PP648's
  rule. If it is a vendor path its absence is quiet, which the hardware contract
  requires; if it is the in-box transform there is no absence to be quiet about.

## Done when — PP696

- **One commit edits lib and the build, and no test file** session.c stops asking, the
  shim's wrappers go behind PP663's option, the suite's list loses its frame-path files
  with the floor moving to match, and the four library files leave. The gate is green
  after it because every assertion it moves was already taught where it lands.
- **Every consumer the census names is answered before the file goes**
  FramePathConsumers reads session.c, the shim and the suite's list from the tree and
  resolves each symbol's counterpart by reflection. Nothing leaves the build while that
  reading names a call with no answer, so the flip's own precondition is a check rather
  than a reviewer's judgement.

## Done when — PP697

- **The predicates stay and the tense around them turns** PP634's correction, applied to
  the frame path: each predicate is a shape the C could return in, so none is deleted.
  What changes is prose asserting the tree still has what the flip removed, turned to
  say what it was rather than what it is, the way PP591 and PP652 turned theirs.

## Done when — PP671

- **The recorded cases judge the managed decoder on a bare build** Fec.Recovers defaults
  to the managed decoder, so the sixty-four recorded erasure cases assert on every build
  instead of declining without the C. The differential in FecCodecTests stays the one
  place the C is named, and OracleGuardCensus counts two fewer guarded theories.

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
