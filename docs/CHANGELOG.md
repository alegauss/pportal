# Shipped Ledger

## Block A — Core

## Block B — Native interop

## Block C — Video and input path

- ✅ **PP57** **videoreceiver reads slice.slice_type uninitialised when the bitstream parser declined the frame, which is UB** — The slice is zeroed at declaration and the log names a type only when the parse produced one, held by 2 munit assertions over a declined frame that still reaches the callback.
- ✅ **PP68** **chiaki_bitstream_header never returns on a truncated SPS, and the header it is handed comes off the network** — vl_rbsp_ue now stops at the end of the NAL and at 32 leading zeroes, and every parse that overran is refused, held by 2 munit assertions that used to hang the suite.
- ✅ **PP70** **vl_vlc_valid_bits returns 32 minus a count that can exceed 32, unsigned, so an exhausted reader reports billions of bits** — vl_vlc_valid_bits clamps at zero, so the loops that trusted it terminate, held by a munit assertion at the alignment that hung and 132 tests still green.
- ✅ **PP69** **slice_set_reference_frame_h265 edits the frame from a parse that ran out of input, and reports that as success** — The write is refused when the parse overran or would reach behind the caller's buffer, held by a munit assertion at the one length of 128 measured where that changes the answer.
- ✅ **PP65** **d3d11va's decode submission stalls: a 103us median send against a 26990us p99, which is 1.6 frame intervals** — It is the harness, not the driver: fed at 60fps instead of as fast as it will take them, the p99 is 548us against 25124us, while the surface pool and the held frames move nothing.

## Block D — Screens

## Block E — Windows-only build

- ✅ **PP62** **the tree's only build cannot include QtWebEngine, so the login screen the port replaces is compiled out** — The reference build is an MSVC configure built once for the purpose, filed as PP63: MSYS2 has no qt6-webengine and the published Windows releases carry no Chromium, measured.

## Block F — Managed core

## Block G — Test discipline

- ✅ **PP54** **the vendored munit does not compile on gcc 16, so the only test target in the tree cannot be built at all** — munit is a pinned submodule, so this repo's CMake now builds that one file as the C11 it was written for, and chiaki-unit compiles on gcc 16 with 113 of 113 passing.
- ✅ **PP56** **compile.cmd never builds chiaki-unit, so ctest reports green against whatever binary was last linked by hand** — A default build now links chiaki-unit with the client, so ctest answers about the tree that is there; notests keeps the fast path and warns that it did not.

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

## Block I — NVIDIA path

- ✅ **PP47 (the feature choice and the floor)** **DLSS needs motion vectors and a depth buffer, and a decoded video stream carries neither** — DLSS cannot apply and VSR is the candidate; the plain video-processor upscale 1080p to 4K costs 262.9us on an RTX 4060, and VSR itself did not engage.
- ✅ **PP48 (the cost half)** **the client already prefers the cuda decoder on an NVIDIA card, and nothing measures whether that helps** — All three hardware paths decode within 13% of each other; what separates them is the per-frame copy the client skips only for vulkan - 793us cuda, 2253us d3d11va.
- ✅ **PP71** **paced at 60fps the cuda decoder's send p99 reaches 13480us against d3d11va's 548us, and varies wildly run to run** — Contamination is out - alone in a fresh process it is the same or worse - and the clocks fall with the idleness pacing creates; vulkan and d3d11va both beat cuda at 60fps.
