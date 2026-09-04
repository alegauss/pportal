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

- ⏳ **PP27** (deps: PP672 ✅, PP673, PP674, PP675, PP676, PP677, PP678, PP679, PP680) (requires: console) **takion.c is 2007 lines of C over raw sockets and timers, and the whole stream rides on it** — The nine tasks it waits on are the managed transport; after them, the three files leave the build. → §PP27
- 📋 **PP30** (deps: PP23 ✅, PP27 ⏳) **forward error correction is two vendored C libraries doing Galois field arithmetic per lost packet** — chiaki_fec_decode has three callers - frameprocessor.c, the C suite and this port's shim - and gf-complete has a fourth site none of them reach: chiaki_lib_init. → §PP30
- ⏳ **PP33** (deps: PP24 ✅, PP293 ✅, PP340 ✅, PP481 ✅, PP533 ✅) (requires: console) **HTTP and JSON in the core are curl and json-c, two vendored dependencies for what the runtime already does** — the file itself: one file calls it, the shim, and PP481's oracle is what that seam is for. → §PP33
- 🛠 **PP295** (deps: PP297 ✅, PP696, PP697) **streamconnection.c is 1531 lines and calls the video receiver, so every deletion below waits on it** — Three criteria are met; the fourth is the four files leaving, which waits on the one commit that edits the C and on the shim, whose wrappers outlive it. → §PP295
- 📋 **PP671** (deps: —) **Fec.Recovers with no decoder named runs the C, so after the flip a default becomes a loader failure** — The managed decoder is the one that stays; the default should follow it on the flip, so the sixty-four recorded cases judge the port alone. → §PP671
- 📋 **PP673** (deps: —) **no managed code parses a takion control message, so a control datagram stops at the dispatch branch** — takion_parse_message and the DATA and DATA_ACK switch join TakionReceivePath's Control branch to the drain and ack models, and both exist only as source checks. → §PP673
- 📋 **PP674** (deps: —) **takion's data queue is 32-bit and the managed reorder queue is 16-bit only, so no managed push can hold a data packet** — chiaki_reorder_queue_init_32 has no counterpart and no shim export, and takion_handle_packet_message_data, the push that reads seq and channel, has none either. → §PP674
- 📋 **PP675** (deps: —) **no managed code sends a takion datagram, so every takion send is a model that emits no bytes** — chiaki_takion_send_raw, chiaki_takion_send and the three data builders have no managed bytes, and TakionMessageHeader knows no DATA or DATA_ACK chunk type. → §PP675
- 📋 **PP676** (deps: —) **the feedback and mic sends have no managed code, and each places its MAC where packet_mac's table does not look** — takion_send_feedback_packet encrypts at the position plus a block and MACs at eight, the mic packet at ten, and both hold the recursive cipher lock throughout. → §PP676
- 📋 **PP677** (deps: —) **the key state has no managed transcription, so every key position the port expands is the shim's** — PP111 reached the expansion through the shim and PP519 fed it a console's positions; a managed parse of an AV header or a control message needs the ledger in managed code. → §PP677
- 📋 **PP678** (deps: PP672 ✅, PP673, PP674, PP675, PP677) **the receive loop runs only against test doubles, and nothing owns takion's state** — TakionReceiveLoop.Run traces steps through an ITakionLoopHost implemented only in tests; the tag, counter, ledger, cipher and queues have no owner. → §PP678
- 📋 **PP679** (deps: —) **the v7 AV parse and header formatter are unported, and the formatter's callers are senkusha's** — chiaki_takion_v7_av_packet_parse differs from v9 in three places, and chiaki_takion_v7_av_packet_format_header is called only by senkusha.c, so who owns them is a decision. → §PP679
- 📋 **PP680** (deps: PP668 ✅) **takion_handle_packet_av is only a branch in managed code, so no video packet reaches the flush** — The disable gates, the queue seeded at packet_index minus unit_index, the entry with its stamp and the flush into StreamAvDispatch have no composition; the parse is PP668's. → §PP680
- 📋 **PP685** (deps: —) **PP395's chokepoint comment calls the two data-type-2 sends the keyboard pair, which they are not** — They are the corrupt frame and the IDR request, and the word keyboard appears nowhere else in the file; a reader taking the comment for the table gets the video path wrong. → §PP685
- 📋 **PP694** (deps: —) **the microphone's units reach nothing, and libopus's second consumer is why the dependency cannot leave** — PP652 answered the input question opusencoder.c waited on, so the encoder is portable now and PP651 already measured managed Opus at a quarter of a percent of a frame. → §PP694
- 📋 **PP696** (deps: PP671) **the frame path's deletion has no commit that edits the C, so four files stay while their ports exist** — PP623's middle step is the only one touching lib, and nobody has written this path's: session.c's asks, the shim's wrappers and the suite's four files all still name them. → §PP696
- 📋 **PP697** (deps: PP696) **after the frame-path flip the models describe a C that has gone, in the present tense** — PP634 found this on the holepunch side: the predicates stay because they notice the calls coming back, and what goes stale is the prose around them. → §PP697

## Block G — Test discipline

- 📋 **PP683** (deps: —) **the oracle guard census reads test files only, so the selftest's guarded comparisons are invisible to it** — PP665 prints what an absent oracle costs from eleven test files; the host's own 460 checks guard comparisons too, and PP681's defect lived in one the count never saw. → §PP683
- 💭 **PP691** (deps: —) **checks that match a roadmap sentence literally go red when the sentence gets more precise** — PP666 hit two in one task and both were red about text that had improved; nobody has counted how many more of these literal readers the tree carries. → §PP691

## Block H — Performance and telemetry

- ⏳ **PP46** (deps: PP42 ✅, PP63) **the claim that dropping the bundled browser makes startup and the installer smaller is untested** — A Chromium leaving the build should be visible in cold start and in megabytes, and stating it without measuring is how a port collects folklore. → §PP46
- 💭 **PP303** (deps: PP46 ⏳) **PP46's before costs two multi-gigabyte installs for a number about an application this port is not a version of** — PP277 settled that this is a new application and not upstream's next, so a delta against a Qt build compares two products. → §PP303

## Block I — NVIDIA path

- ⏳ **PP49** (deps: PP11 ✅, PP47 ✅) (requires: console, a-person-looking) **the console sends SDR on most titles and an HDR display shows it flat, with nothing in the client trying** — the quality half and the integration: a decoded console frame to judge the picture on, and a setting that turns it off. → §PP49
- 🛠 **PP52** (deps: PP32 ✅, PP652 ✅) **nothing runs echo cancellation, and the vendor answer is absent on a machine with the card** — Nothing cleans a sample yet: a stage between the capture and the encoder, read back rather than assumed to have run, is still to build. → §PP52
- ⏳ **PP53** (deps: PP11 ✅, PP41 ✅) (requires: variable-refresh-display) **frames arrive with network jitter and are presented against a fixed refresh, so each waits for a vblank it missed** — the reading itself: a display that varies its refresh, and a trace saying the frame arrived unpaced. → §PP53
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
- **libchiaki builds with neither curl nor json-c** Met by PP663 for the ordinary build:
  both libraries and holepunch.c sit behind CHIAKI_ENABLE_HOLEPUNCH, off by default and
  passed explicitly, with the suite green either way. What that flag still carries is
  PP481's oracle - nine wrappers plus fifteen over json-c - so the FILE stays until
  those have an answer.
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

## Done when — PP76

- **One played session per decoder against a real console** PP48 settled what a
  generated stream can settle and PP71 reversed its ranking under pacing. Neither is the
  number a user feels: a generator carries resolution and bitrate, not a congested link.
- **The number read is the difference between the two counters** PP528 separated frames
  lost from frames dropped, and PP76's own remaining half is that difference per decoder
  under jitter - which is what makes this a reading rather than a second instrument.

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

## Done when — PP673

- **Every control head in the corpus parses, under the session's one tag** PP608's heads
  carry the sixteen-byte header whole. The parse runs over every control datagram in
  tests/corpus with tag_local read off the first and PP515's recorded length as the
  datagram size; a refusal, or a head with a byte moved that is accepted, fails by
  datagram index.
- **DATA keeps the buffer, DATA_ACK releases it, anything else is dropped** The switch
  is asserted against a sink the way TakionReceivePath is: a DATA message reaches the
  drain's entry shape with the buffer retained, a DATA_ACK reaches TakionDataAck.Read
  and is borrowed, and an unknown chunk type is counted and dropped.

## Done when — PP674

- **The wide queue agrees with a create_32 export after every operation** The oracle
  shape PP108 and PP150 used for sixteen bits: fixed-seed random scripts of push, pull,
  peek and drop run through both queues, with count, begin and the drop list compared
  after every step, across the thirty-two-bit wrap.
- **A data message becomes an entry and a push, with PP491's two drops kept** A payload
  shorter than nine bytes and a failed entry are dropped, not queued; a good one is
  pushed with the sequence number from its first four bytes and the channel from the
  next two, and drains through TakionDataDrain in the C's order.

## Done when — PP675

- **The managed data message, continuation and ack match the C's bytes**
  chiaki_takion_send_message_data and its siblings are exported, so a shim call sends
  through PP607's connected takion and the peer this process holds receives the C's
  bytes; the managed builder, given the same tag, sequence and payload, produces the
  same array.
- **One send path: MAC under the cipher lock, then the socket, nothing allocated**
  chiaki_takion_send's order is reproduced - stamp, then send - and the send over a
  connected UDP socket is measured the way PP633 measured the receive: zero bytes
  allocated after warm-up per packet sent.

## Done when — PP676

- **Feedback state, history and mic bytes match the C's for one key and state** The
  three exports run through a loopback takion whose gkcrypt_local is built from known
  keys, so the encrypted bodies and the MACs at offsets eight and ten are the C's; the
  managed builders, given the same GkCrypt and position, produce the same bytes.

## Done when — PP678

- **A managed takion connects, runs its loop over a socket and tears down in order**
  Against PP606's responder: the connected event, then a loop that receives real
  datagrams into TakionReceiveBuffer and dispatches through TakionReceivePath, then
  close - send buffer, queues, postponed packets, the disconnected event, the socket -
  each step observed in the C's order.
- **Nothing is allocated per datagram once the loop is warm** PP633's measurement over
  the loop as it runs on a socket rather than in a replay: bytes allocated after warm-up
  are zero over the corpus fed through loopback, which is PP44's budget held on the
  transport itself and not on a harness.

## Done when — PP679

- **The v7 parse and formatter have an owner, and a test against the C for each** A
  decision recorded in the decisions file names where each goes; the parse is compared
  against chiaki_takion_v7_av_packet_parse through a shim export on real and synthetic
  headers, and the formatter's output is parsed back by the C's own v7 parse.

## Done when — PP680

- **The AV arm delivers the corpus's video in AvReorderTimeout's order** PP608's heads
  seed and order the video queue: the assembled arm - gates, lazy init, entry, flush -
  is fed the corpus, and the sequence it hands StreamAvDispatch and what it drops equal
  what AvReorderTimeout.Flush computes over the same heads.

## Done when — PP677

- **The managed key state agrees with the shim's over the corpus, either way**
  DatagramReplayReport.KeyPositions reads the low halves off PP608's heads; the same
  sequence goes through KeyState and the managed ledger, with commit and without, and
  every expanded position is equal, including PP521's twenty-six zeros before the cipher
  exists.

## Done when — PP683

- **The selftest is a row in the census, and the printed cost counts its guards**
  app/SelfTest.cs joins OracleGuardCensus.Files with the guard it calls; the census's
  own test holds that the file still carries it, and the gate's line about what a bare
  build skipped rises by what the selftest declines rather than stopping at the test
  project.

## Done when — PP685

- **The comment names the two sends that carry the type, and a check holds it** The
  sentence says corrupt frame and IDR request; StreamMessagesSource already reads the
  call sites, so the check is that the comment's named pair and the pair the calls pass
  with data type two are the same two, which a future rename breaks loudly.

## Done when — PP691

- **Every roadmap sentence a check holds is counted and judged** A list of the string
  constants in app/ that carry roadmap or ledger prose, each marked load-bearing or
  incidental, with the question answered for each: would a more precise sentence break
  it. Two are known already from PP666. A count that returns only those two is an
  answer, not a failure.

## Done when — PP694

- **A managed encoder turns a captured unit into an Opus frame** The 960-byte units
  WasapiCapture delivers go in and Opus frames come out, at the bitrate and application
  mode opusencoder.c sets. Held against the C through the shim on recorded input, the
  way every other port here is, rather than judged by whether the output decodes.
- **Whether libopus can leave is answered with both consumers counted** A census names
  every caller of the library across lib, shim and test, the way PP692 did for
  gf-complete rather than counting one module's export. It says what still holds libopus
  in the build and the package after the encoder is managed. PP651's decode reading is
  cited, never re-taken.

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
