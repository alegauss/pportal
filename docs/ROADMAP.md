# Roadmap (active backlog)

## Priority

- Block H
- Block I

## Block A — Core

## Block B — Native interop

## Block C — Video and input path

- 📋 **PP641** (deps: PP11 ✅, PP322 ✅) **PP10's HUD is XAML, and the compositor tree PP319 chose covers a WPF window's own drawing entirely** — PP322 read that an eight-bit premultiplied surface composes above the ten-bit plane; what draws the HUD into it is the half nobody has built. → §PP641

## Block D — Screens

## Block E — Windows-only build

- 📋 **PP63** (deps: PP62 ✅) (requires: msvc-qt-webengine) **nothing in the tree can configure a Qt build carrying WebEngine, so PP46's before cannot be produced at all** — MSYS2 has no qt6-webengine and no Windows release carries Chromium, so the reference is built once with MSVC. → §PP63
- 📋 **PP301** (deps: —) (requires: runner) **no MSVC toolchain has ever compiled this tree, so the CI workflow's first push is the first time it is tried** — PP22 configures a runner the only way a runner can be, and everything built here so far came through MSYS2 MinGW64. → §PP301
- 📋 **PP302** (deps: —) (requires: signing-certificate) **nothing signs the host or the installer, so SmartScreen warns on the first run of every release** — PP22 shipped the builds and the packages of its own sentence and not the signs, because that one starts with buying a certificate. → §PP302

## Block F — Managed core

- ⏳ **PP27** (deps: PP23 ✅, PP25 ✅, PP44 ✅) (requires: console) **takion.c is 2007 lines of C over raw sockets and timers, and the whole stream rides on it** — PP610 timed the MAC gate and PP633 the loop's copy over real payloads; what is left is takion.c, takionsendbuffer.c and reorderqueue.c leaving. → §PP27
- 📋 **PP28** (deps: PP293 ✅, PP294 ✅, PP23 ✅) **session.c 1196, ctrl.c 1767 and streamconnection.c 1531, three state machines with no oracle** — the three together, once PP293, PP294 and PP295 have each landed: what is left here is the ordering between them. → §PP28
- 📋 **PP30** (deps: PP23 ✅, PP27 ⏳) **forward error correction is two vendored C libraries doing Galois field arithmetic per lost packet** — 13 sites and none of them arithmetic: chiaki_fec_decode has three callers - frameprocessor.c, the C suite, and this port's shim. → §PP30
- 📋 **PP31** (deps: PP28) **the video decoder is where 100% managed stops being achievable, and no task above says so** — There is no managed H.264 or HEVC decoder that holds 1080p60 at remote play latency, so this boundary is chosen deliberately or discovered late. → §PP31
- 📋 **PP32** (deps: PP28) **audio decode is Opus in lib and the microphone's noise and echo stages are speexdsp in the Qt client** — Managed Opus exists and speexdsp has none; the conversion between them is SDL_AudioCVT rather than speex, so the audio path is three dependencies and not two. → §PP32
- ⏳ **PP33** (deps: PP24 ✅, PP293 ✅, PP340 ✅, PP481 ✅, PP533 ✅) **HTTP and JSON in the core are curl and json-c, two vendored dependencies for what the runtime already does** — the deletion: holepunch.c is the only unit needing either library, and one file calls it - the shim, which wraps nine of its exports. → §PP33
- 📋 **PP295** (deps: PP297 ✅) **streamconnection.c is 1531 lines and calls the video receiver, so every deletion below waits on it** — PP286 to PP291 removed no C, and the shim wraps five of the receiver's exports: lib has one caller and this port's own seam is the other. → §PP295

## Block G — Test discipline

- 📋 **PP642** (deps: —) **a ship's `recorded in` clause names a file and nothing checks the file ever received the design** — PP11 is the first entry to carry one, and what holds its paragraph in DcompDemo.cs is a test written by hand for that one entry. → §PP642
- 💭 **PP643** (deps: —) **two `<summary>` elements on one member compile, and the wrong one wins silently** — PP322's attach docstring sat above the reading test describing a member two declarations down, and the ratchet joins tasks to tests by exactly that text. → §PP643

## Block H — Performance and telemetry

- ⏳ **PP46** (deps: PP42 ✅, PP63) **the claim that dropping the bundled browser makes startup and the installer smaller is untested** — A Chromium leaving the build should be visible in cold start and in megabytes, and stating it without measuring is how a port collects folklore. → §PP46
- 💭 **PP303** (deps: PP46 ⏳) **PP46's before costs two multi-gigabyte installs for a number about an application this port is not a version of** — PP277 settled that this is a new application and not upstream's next, so a delta against a Qt build compares two products. → §PP303

## Block I — NVIDIA path

- ⏳ **PP49** (deps: PP11 ✅, PP47 ✅) (requires: console, a-person-looking) **the console sends SDR on most titles and an HDR display shows it flat, with nothing in the client trying** — the quality half and the integration: a decoded console frame to judge the picture on, and a setting that turns it off. → §PP49
- 📋 **PP52** (deps: PP32) **the Qt client runs speex echo cancellation on the CPU, and speexdsp has no managed replacement** — NVIDIA ships GPU noise and echo removal for exactly this, so one task can both improve the voice sent to the console and delete a dependency the port has no answer for. → §PP52
- 📋 **PP53** (deps: PP11 ✅, PP41 ✅) **frames arrive with network jitter and are presented against a fixed refresh, so each waits for a vblank it missed** — A variable refresh display can show a frame when it arrives rather than when the panel next allows it, which is latency removed and not an image improved. → §PP53
- ⏳ **PP76** (deps: PP528 ✅) (requires: console, a-person-looking) **the decoder preference is measured on synthetic frames, and drops under network jitter are what a stream is judged by** — one session per decoder against a real console, now that the difference between the two counters is the number to read. → §PP76
- 📋 **PP644** (deps: —) **spike/video-upscale calls the extension read-back a hint, and PP49 measured that it is not one** — The same zeros came back on a run where 2.07 million pixels moved, so the echo says nothing in either direction and a reader is still told to weigh it. → §PP644
- 📋 **PP645** (deps: —) **both NVIDIA spikes report a mean, and four of PP49's six runs carried 200-300us outliers on it** — PP49's delta is stable at the p50 and moves by 40% at the mean, so which run gets committed is chosen by eye rather than by a rule the spike applies. → §PP645

## Block J — Public documentation

## Done when — PP33

- **Every curl and json-c call site in holepunch.c has a named counterpart** The
  `remaining` query below finds them; each area answered by a class in app/Protocol
  whose source assertions read the same file. What is not yet answered is named in the
  criteria under this one, rather than left for the count to imply.
- **The websocket thread's auto-ACK of offers is stated** The rule is a state mask -
  auto-ACK while a control offer is received and not yet established, or once a data
  offer is - and the `continue` on a parse failure skips the enqueue, losing the
  notification. Both stated, both asserted against the file.
- **The session HTTP calls run through HttpClient rather than curl** The four session
  calls and the wakeup reach PSN through HttpClient, with the response codes and the
  failure paths the curl setup encodes - CURLOPT_FAILONERROR among them, which turns an
  HTTP error into a transfer error and is not the default anywhere else.
- **libchiaki builds with neither curl nor json-c** Two steps, measured apart by PP565.
  Compiling is done: with holepunch.c out of the sources and both libraries unlinked,
  every other source in lib compiles and the archive is built. Linking waits on the four
  callers PP563 and PP564 named - every reference the exes fail on is a holepunch
  symbol, not curl or json-c.
- **The remaining query reads zero** And not before. The query counts C that porting
  does not remove, so it reads its full count until the criterion above is met and then
  reads zero. It is an end state, not a progress bar, and reading it as one is what made
  four shipped tasks look like none.

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
- **takion.c, takionsendbuffer.c and reorderqueue.c leave the build** Porting into app
  removes no C, so this is the end state and not a progress bar - the same shape PP33's
  own last criterion has. The three files' sizes are stated in the section, where the
  recount reaches them.

## Done when — PP46

- **The three numbers are recorded on the Qt build** Cold start to the console list,
  installer size, and process working set at idle - alongside the rest of the baseline,
  in the same record the sink already writes. This is the before, and PP63 is what makes
  it buildable.
- **And again on the WPF build, in the same record** A delta needs both halves written
  the same way. These are the two numbers most likely to be quoted in a release note,
  and a quoted number nobody measured survives long past the day it stopped being true.

## Done when — PP76

- **One played session per decoder against a real console** PP48 settled what a
  generated stream can settle and PP71 reversed its ranking under pacing. Neither is the
  number a user feels: a generator carries resolution and bitrate, not a congested link.
- **The number read is the difference between the two counters** PP528 separated frames
  lost from frames dropped, and PP76's own remaining half is that difference per decoder
  under jitter - which is what makes this a reading rather than a second instrument.

## Done when — PP295

- **The stream connection's event ordering is ported, not only its functions** The file
  where the ordering IS the behaviour: a port that reproduced every function and not
  their sequence would pass a message-level comparison and fail a session, which is the
  failure no oracle built from messages can catch.
- **The managed video receiver is driven by the ported stream connection**
  ManagedVideoReceiver takes a four-method outbound seam precisely so its driver need
  not be a session pointer, and corrupt-frame and IDR requests are two of the four -
  both messages this file sends.
- **Every consumer PP638's linker run named has a counterpart** Seventeen symbols over
  three kinds: session.c's six, the shim's twelve including jerasure's create_matrix,
  and the four files in the C suite. A port that answered the library's callers alone
  leaves the gate red at link time.
- **streamconnection.c, videoreceiver.c, frameprocessor.c and fec.c leave the build** It
  is an end state, not a progress bar: PP638 measured that session.c drives the stream
  connection, so this cannot land until PP28 stops it - and PP28 is what waits on the
  three criteria above. Porting into app removes no C.

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
