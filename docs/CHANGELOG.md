# Shipped Ledger

## Block A — Core

- ✅ **PP1** **no .NET host exists, so a XAML window has nowhere to live** — app/ builds one self-contained 62MB ChiakiNg.exe on net10.0-windows with Fluent, opens an empty 1280x720 window, and fails its own build when app.manifest and Version drift.
- ✅ **PP2** **settings, registered consoles and the PSN token live in QSettings, which the .NET host cannot read** — the remaining 148 preferences are declared with the store, kind and default Settings reads them with, extracted from source and read back through the grammar PP80 measured.
- ✅ **PP79** **the .NET store reads the default hive, so a user with an active profile sees none of their consoles** — the reader resolves the active profile first and derives every path from it, with five assertions on the join and the third store named.
- ✅ **PP80** **the store reader knows the byte-array encoding and none of the other five, so an @ nickname reads back wrong** — bool, double, uint, rect and the @ escape are read the way a probe store proved Qt writes them, under 21 more assertions.
- ✅ **PP3** **app data, session logs and key files are placed by QStandardPaths, which the .NET host does not share** — the log, shader, placebo and baseline paths are stated as the Qt paths a probe measured, with the roaming/local split and the missing Downloads folder both asserted.

## Block B — Native interop

- ✅ **PP4** **libchiaki is C with function-pointer callbacks and no managed binding, so no .NET code can start a session** — the seam is settled and proved: opaque handles, an UnmanagedCallersOnly thunk per callback and a C-side builder per struct, with the sample sinks filed as PP87.
- ✅ **PP83** **the 22 callbacks libchiaki takes are uncrossed in the direction it calls them, so nothing above the seam can be built** — a managed handler now receives libchiaki's log callback through an UnmanagedCallersOnly thunk and a GCHandle, under ten assertions including one across a forced collection.
- ✅ **PP84** **nothing managed has called chiaki_lib_init or built a ChiakiSession, so the lifecycle has no first end** — chiaki_lib_init and chiaki_session_init are reachable from .NET over an opaque connect-info builder, under twelve assertions and no console.
- ✅ **PP85** **the session thread and its event callback are unreached, so nothing managed can hear a session end** — start, the event callback and join are reachable from .NET, and a quit off the session thread arrives at a managed handler under seven assertions.
- ✅ **PP86** **the controller state is 21 scalars and a touch array, and nothing managed can put one into a session** — a controller state built in C crosses by handle and comes back equal by chiaki_controller_state_equals, under fifteen assertions.
- ✅ **PP5 (the connect info)** **streamsession.cpp drives the session through Qt signals and QThread, so a session cannot run without Qt** — settings become session parameters with no Qt: the local-address rule, the four profile groups and both overrides, under twenty-one assertions.

## Block C — Video and input path

- ✅ **PP57** **videoreceiver reads slice.slice_type uninitialised when the bitstream parser declined the frame, which is UB** — The slice is zeroed at declaration and the log names a type only when the parse produced one, held by 2 munit assertions over a declined frame that still reaches the callback.
- ✅ **PP68** **chiaki_bitstream_header never returns on a truncated SPS, and the header it is handed comes off the network** — vl_rbsp_ue now stops at the end of the NAL and at 32 leading zeroes, and every parse that overran is refused, held by 2 munit assertions that used to hang the suite.
- ✅ **PP70** **vl_vlc_valid_bits returns 32 minus a count that can exceed 32, unsigned, so an exhausted reader reports billions of bits** — vl_vlc_valid_bits clamps at zero, so the loops that trusted it terminate, held by a munit assertion at the alignment that hung and 132 tests still green.
- ✅ **PP69** **slice_set_reference_frame_h265 edits the frame from a parse that ran out of input, and reports that as success** — The write is refused when the parse overran or would reach behind the caller's buffer, held by a munit assertion at the one length of 128 measured where that changes the answer.
- ✅ **PP65** **d3d11va's decode submission stalls: a 103us median send against a 26990us p99, which is 1.6 frame intervals** — It is the harness, not the driver: fed at 60fps instead of as fast as it will take them, the p99 is 548us against 25124us, while the surface pool and the held frames move nothing.

## Block D — Screens

## Block E — Windows-only build

- ✅ **PP62** **the tree's only build cannot include QtWebEngine, so the login screen the port replaces is compiled out** — The reference build is an MSVC configure built once for the purpose, filed as PP63: MSYS2 has no qt6-webengine and the published Windows releases carry no Chromium, measured.
- 🗑 **PP20** **171 platform conditionals remain in gui, 33 of them macOS and 17 Linux, after those trees were deleted** — abandoned: 5f09bef3 deleted them before this line was filed: gui carries no Q_OS_MAC, Q_OS_LINUX or __APPLE__ today, and no CMakeLists carries a platform branch either.

## Block F — Managed core

## Block G — Test discipline

- ✅ **PP54** **the vendored munit does not compile on gcc 16, so the only test target in the tree cannot be built at all** — munit is a pinned submodule, so this repo's CMake now builds that one file as the C11 it was written for, and chiaki-unit compiles on gcc 16 with 113 of 113 passing.
- ✅ **PP56** **compile.cmd never builds chiaki-unit, so ctest reports green against whatever binary was last linked by hand** — A default build now links chiaki-unit with the client, so ctest answers about the tree that is there; notests keeps the fast path and warns that it did not.
- ✅ **PP67** **nothing launches the suite, so running ctest means knowing it is a MinGW64 shell away and not on PATH** — test.cmd runs the suite through the MinGW shell, bounds it with a timeout so a hang is not silence, cuts one test out of a full run, and warns when the binary predates lib.
- ✅ **PP73** **nothing rechecks a task's counted premise against the tree, and three of four spot-checked lines did not match it** — Only where a regex is the premise: PP16, PP30 and PP33 now declare a roadkeep-remaining query answering 149, 14 and 420 from the tree; the rest count lines, which no query expresses.
- ✅ **PP74** **compile.cmd builds the Qt client and knows nothing about app/, so the tree's only gate now covers half of it** — A default compile.cmd builds app\ after the Qt client and goes red when it breaks; noapp skips it and says so, and a machine with no .NET SDK gets a note rather than a refusal.
- ✅ **PP75** **test.cmd runs ctest and nothing runs the .NET host's selftest, so 11 assertions sit in the tree ungated** — test.cmd runs the C suite and then the .NET host's selftest; a broken decoder that the gate could not see now exits 1, and noapp skips it out loud.

## Block H — Performance and telemetry

- ✅ **PP39** **the numbers the stream already computes are drawn on screen and never recorded, so the Qt build leaves no baseline** — The counters the stream already computes are now written as one JSON line per session to chiaki_baseline.jsonl in the log dir, timestamped and held by 8 munit assertions.
- ✅ **PP40** **glass to glass latency is not among the numbers measured, and it is the one users judge on** — The record now carries a latency floor: input queueing plus the console's reported round trip, read as milliseconds against ICMP, plus the decode-to-present handoff, held by 4 munit assertions.
- ✅ **PP41** **only one stage of the frame path is timed, so a slower build says which build and never which stage** — Receive, reorder dwell, reassemble, FEC correct and the decoder's send-to-pull are each timed now, as min/max/mean plus a p99 from a fixed log histogram, held by 6 munit assertions.
- ✅ **PP42** **telemetry has no sink: the counters live in the window and end with it** — The row now names the configuration that produced it - decoder, requested bitrate, both loss knobs - and its field set is closed against an address ever entering it, held by 11 munit assertions.
- ✅ **PP43** **the present path is the largest risk in the port and would be chosen by argument rather than by measurement** — Measured on an RTX 4060: the shared surface holds 60fps at a 47us median present with a 5ms p99, while the airspace child window settles at 32fps - numbers and caveats in spike/present-path.
- ✅ **PP44** **a managed transport allocating per packet would show up as stutter, and nothing would fail when it does** — The budget is 0 bytes per packet, measured rather than agreed: the C reassemble path allocates nothing per packet in steady state, and a munit counter plus a managed gate both fail when it rises.
- ✅ **PP45** **no harness runs the old build and the new one against the same input and prints the difference** — compare-baselines reads two records and prints p50, p99 and max per stage with the delta, refusing a verdict and flagging mismatched conditions; the median arrived with it as schema 4.
- ✅ **PP46 (the harness)** **the claim that dropping the bundled browser makes startup and the installer smaller is untested** — measure-startup takes cold start, tree size and idle working set in one command and stamps webengine_present, so a row taken without Chromium cannot be read as the before.
- ✅ **PP58** **the airspace present path runs at half the shared surface's frame rate and nothing says why** — PresentEx waits a vblank even at PresentInterval.Immediate, and stacking that on WPF's vblank-synced render tick costs one frame in two; off that tick the same child window holds 60fps.
- ✅ **PP59** **takion mallocs a queue entry and a packet buffer per video packet, so the receive step has no budget** — The receive step costs 3 allocator calls and 1754 bytes per video packet, measured through a seam the counter can reach, and the gate fails when either rises.
- ✅ **PP60** **one baseline file accumulates records of four schemas and the comparison tool refuses all but the newest** — compare-baselines now reads every shape by the fields present rather than the schema number, compares the intersection and prints what it had to drop.
- ✅ **PP64** **a field can be added to the baseline record without bumping the schema, and 34b10cbf already did it** — The emitted key set is pinned per schema number, so a field added without a bump and a bump with no recorded row both turn the suite red.
- ✅ **PP66** **spike/decode-path writes a result.json naming no adapter, so two runs cannot be told apart by the file** — Every run now names the card, read out of the d3d11va device by the DXGI route video-upscale already takes, and the two committed results carry it flagged as annotated.

## Block I — NVIDIA path

- ✅ **PP47 (the feature choice and the floor)** **DLSS needs motion vectors and a depth buffer, and a decoded video stream carries neither** — DLSS cannot apply and VSR is the candidate; the plain video-processor upscale 1080p to 4K costs 262.9us on an RTX 4060, and VSR itself did not engage.
- ✅ **PP48** **the client already prefers the cuda decoder on an NVIDIA card, and nothing measures whether that helps** — Decode does not separate the paths, the per-frame copy does, and PP71's p99 contradicts the ordering (design §PP48 superseded: the auto ordering PP71 reverses and the stall PP65 answered).
- ✅ **PP71** **paced at 60fps the cuda decoder's send p99 reaches 13480us against d3d11va's 548us, and varies wildly run to run** — Contamination is out - alone in a fresh process it is the same or worse - and the clocks fall with the idleness pacing creates; vulkan and d3d11va both beat cuda at 60fps.
- ✅ **PP72 (the record half)** **the auto decoder order prefers cuda over d3d11va on an OpenGL renderer, and the paced numbers now say the opposite** — The row now names the renderer beside the decoder, so the two OpenGL-fallback paths are comparable and the preference can be settled from real sessions.
- ✅ **PP51** **NVIDIA first has no stated contract for what happens on an AMD or Intel machine** — The floor is written down in docs/HARDWARE-CONTRACT.md - d3d11va decode, a neutral renderer, an SDR present with no NGX - and a non-goal now binds a proposal to it.
- ✅ **PP77** **the decoder choice that holds the non-NVIDIA floor is 70 lines inside a Qt method, so nothing can assert it** — the decode branch that holds the non-NVIDIA floor is now chiaki_decoder_choice in lib/, a pure function nine assertions cover, and dropping its d3d11va arm turns the suite red.
