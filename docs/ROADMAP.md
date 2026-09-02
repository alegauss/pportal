# Roadmap (active backlog)

## Priority

- Block H
- Block I

## Block A — Core

## Block B — Native interop

## Block C — Video and input path

- ⏳ **PP11** (deps: PP9 ✅, PP163 ✅, PP322 ⏳) **fullscreen, HDR handoff and refresh-rate switching are handled by the Qt window** — the HDR half alone: PP319 chose the overlay above the video in the compositor's tree, and PP322 is the reading that has to confirm it. → §PP11
- ⏳ **PP322** (deps: —) (requires: a-person-looking) **the two-layer tree commits and nobody has looked at it, which is the mistake PP163 made one layer down** — the reading itself: a person looking once at what --dcomp-demo --layers draws, which no assertion in this tree can reach. → §PP322

## Block D — Screens

## Block E — Windows-only build

- 📋 **PP63** (deps: PP62 ✅) (requires: msvc-qt-webengine) **nothing in the tree can configure a Qt build carrying WebEngine, so PP46's before cannot be produced at all** — MSYS2 has no qt6-webengine and no Windows release carries Chromium, so the reference is built once with MSVC. → §PP63
- 📋 **PP301** (deps: —) (requires: runner) **no MSVC toolchain has ever compiled this tree, so the CI workflow's first push is the first time it is tried** — PP22 configures a runner the only way a runner can be, and everything built here so far came through MSYS2 MinGW64. → §PP301
- 📋 **PP302** (deps: —) (requires: signing-certificate) **nothing signs the host or the installer, so SmartScreen warns on the first run of every release** — PP22 shipped the builds and the packages of its own sentence and not the signs, because that one starts with buying a certificate. → §PP302

## Block F — Managed core

- ⏳ **PP27** (deps: PP23 ✅, PP25 ✅, PP44 ✅) (requires: console) **takion.c is 2007 lines of C over raw sockets and timers, and the whole stream rides on it** — PP531 timed the MAC gate against the C; what is left is the loop around it, which no shim entry point exposes. → §PP27
- 📋 **PP28** (deps: PP293 ✅, PP294 ✅, PP295, PP23 ✅) **session.c 1267, ctrl.c 1767 and streamconnection.c 1531, three state machines with no oracle** — the three together, once PP293, PP294 and PP295 have each landed: what is left here is the ordering between them. → §PP28
- 📋 **PP30** (deps: PP23 ✅, PP27 ⏳) **forward error correction is two vendored C libraries doing Galois field arithmetic per lost packet** — 13 sites and none of them arithmetic: chiaki_fec_decode has three callers - frameprocessor.c, the C suite, and this port's shim. → §PP30
- 📋 **PP31** (deps: PP28) **the video decoder is where 100% managed stops being achievable, and no task above says so** — There is no managed H.264 or HEVC decoder that holds 1080p60 at remote play latency, so this boundary is chosen deliberately or discovered late. → §PP31
- 📋 **PP32** (deps: PP28) **audio decode is Opus in lib and the microphone's noise and echo stages are speexdsp in the Qt client** — Managed Opus exists and speexdsp has none; the conversion between them is SDL_AudioCVT rather than speex, so the audio path is three dependencies and not two. → §PP32
- ⏳ **PP33** (deps: PP24 ✅, PP293 ✅, PP340 ✅, PP481 ✅, PP533 ✅) **HTTP and JSON in the core are curl and json-c, two vendored dependencies for what the runtime already does** — the deletion: holepunch.c is the only unit needing either library, and two files call it - session.c, the shim. → §PP33
- 📋 **PP295** (deps: PP27 ⏳, PP297 ✅) **streamconnection.c is 1531 lines and calls the video receiver, so every deletion below waits on it** — PP286 to PP291 removed no C, and the shim wraps five of the receiver's exports: lib has one caller and this port's own seam is the other. → §PP295
- 💭 **PP622** (deps: PP573 ✅) **PP33's line cannot honestly say one caller, because the check demands the phrase 'one files call it'** — PP573 builds the required sentence from a count word and a fixed plural, so the number PP33 is heading for is the one spelling the line is refused for. → §PP622
- 📋 **PP624** (deps: PP600 ✅) **ConsoleList keys registration and hiding on a MAC, and discovery answers with a host-id in another spelling** — PP600 joined by nickname to get a Connect button that works and left the hidden set empty, so ConsoleActions' Hide outcome cannot be reached from any screen. → §PP624
- 📋 **PP625** (deps: PP600 ✅) **the front door starts a session and releases it in the same call, so a connect that succeeds ends at once** — PP600 had nowhere to hand a running session, so the starter creates, starts and disposes, and libchiaki's own quit reasons reach nobody. → §PP625

## Block G — Test discipline

## Block H — Performance and telemetry

- ⏳ **PP46** (deps: PP42 ✅, PP63) **the claim that dropping the bundled browser makes startup and the installer smaller is untested** — A Chromium leaving the build should be visible in cold start and in megabytes, and stating it without measuring is how a port collects folklore. → §PP46
- 💭 **PP303** (deps: PP46 ⏳) **PP46's before costs two multi-gigabyte installs for a number about an application this port is not a version of** — PP277 settled that this is a new application and not upstream's next, so a delta against a Qt build compares two products. → §PP303

## Block I — NVIDIA path

- 📋 **PP49** (deps: PP11 ⏳, PP47 ✅) **the console sends SDR on most titles and an HDR display shows it flat, with nothing in the client trying** — RTX Video HDR does this conversion on the presented frame, and it is the one vendor feature whose benefit is visible in a still image, not argued from a graph. → §PP49
- 📋 **PP52** (deps: PP32) **the Qt client runs speex echo cancellation on the CPU, and speexdsp has no managed replacement** — NVIDIA ships GPU noise and echo removal for exactly this, so one task can both improve the voice sent to the console and delete a dependency the port has no answer for. → §PP52
- 📋 **PP53** (deps: PP11 ⏳, PP41 ✅) **frames arrive with network jitter and are presented against a fixed refresh, so each waits for a vblank it missed** — A variable refresh display can show a frame when it arrives rather than when the panel next allows it, which is latency removed and not an image improved. → §PP53
- ⏳ **PP76** (deps: PP528 ✅) (requires: console, a-person-looking) **the decoder preference is measured on synthetic frames, and drops under network jitter are what a stream is judged by** — one session per decoder against a real console, now that the difference between the two counters is the number to read. → §PP76

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
- **The managed transport is timed against the C over captured traffic** Not opinion:
  the C is right there and PP531 showed the shape - 0.13us managed against 0.06us per
  head, inside a 1178us mean arrival gap. The same comparison for the loop, not just the
  gate.
- **The transport meets PP44's allocation budget** Thousands of small packets a second,
  each an allocation if written carelessly. Span, ArrayPool and SocketAsyncEventArgs are
  the answer, chosen deliberately - PP44 set the budget before this line writes what has
  to meet it.
- **takion.c, takionsendbuffer.c and reorderqueue.c leave the build** Porting into app
  removes no C, so this is the end state and not a progress bar - the same shape PP33's
  own last criterion has. The three files' sizes are stated in the section, where the
  recount reaches them.

## Done when — PP11

- **A container visual carries the ten-bit swapchain below and the overlay above** PP319
  chose between the three paths D3DImage's ten-bit refusal left, and this is the one
  that costs neither PP10's overlay nor the picture. Fullscreen and the refresh rate
  already shipped; HDR is the half that is left.
- **PP322's reading confirms the overlay lands above the video** A compositor accepting
  a tree says nothing about what reaches the glass. This half is not blocked on a
  decision any more - it waits on the pixel nobody has looked at.

## Done when — PP322

- **A person looks at what --dcomp-demo --layers draws** The apparatus is built and no
  assertion in this tree can reach the answer. PP284 read a pixel none of PP281 to PP283
  had predicted, which is why the reading is the task rather than the tree.
- **The reading answers both questions, not only the visible one** Whether the overlay
  draws over the video plane is visible. Whether an eight-bit premultiplied surface
  composes over a ten-bit plane without the alpha taken twice has no error path
  anywhere: it looks like a slightly wrong colour.

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
  argues it. Not PP33's deletion or PP30's port: one removes what they agree with, the
  other leaves the vendored source alone.
