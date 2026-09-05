# Decisions

## Block A — Core

## Block B — Native interop

## Block C — Video and input path

- ✅ **PP641** **PP10's HUD is XAML, and the compositor tree PP319 chose covers a WPF window's own drawing entirely** — The overlay surface is sized by what the HUD measures, never by the video plane: a composition visual carries its own size, and rendering the tree across a 4K plane costs more than a whole frame.

### §PP641 The overlay surface is the HUD's size

PP641 named three shapes for drawing PP10's XAML HUD into the overlay layer PP319 chose,
and priced the first as "a full-screen copy at HUD update rate". That sentence is what
made the option expensive, and it was not a property of the option.

A composition visual carries its own size and offset. Nothing requires the overlay
surface to match the video plane, and the HUD is a corner of text. `spike/overlay-draw`
measured the same visual tree at both sizes on this machine, sixty iterations after a
discarded warm-up.

| shape | pixels | render | copy | share of a 60fps frame |
| --- | --- | --- | --- | --- |
| the HUD's own bounds | 156x138 | 128 us | 2 us | 0.8% |
| a full 1080p plane | 1920x1080 | 4,757 us | 352 us | 30.7% |
| a full 4K plane | 3840x2160 | 18,390 us | 2,565 us | 125.7% |

The described option does not fit in a frame at all. The actual option costs under one
percent of one, at a rate far below frame rate, because stats update about once a
second.

The other two were priced without a machine. Accepting SDR has no time cost and PP319
already rejected it. Rebuilding against the compositor costs PP10 and PP12 again: their
four commits wrote 2,126 lines across 24 files, which is 4.3 times Block C's p90.

So the rule is the surface size, and `OverlayDraw.SurfaceSizeFor` is where it lives. Its
test measures a real visual tree rather than repeating the number, and the timings above
are read from the spike's committed file rather than typed.

## Block D — Screens

## Block E — Windows-only build

## Block F — Managed core

- ✅ **PP33** **HTTP and JSON in the core are curl and json-c, two vendored dependencies for what the runtime already does** — holepunch.c stays as unbuilt source, gui/'s answer after PP598: ~20 managed models are held against it, and deleting it turned 28 assertions red and would have silenced more quietly.
- ✅ **PP676** **the feedback and mic sends have no managed code, and each places its MAC where packet_mac's table does not look** — The quaternion picks its largest by MAGNITUDE and the ring formats newest-first; both are right-sized when wrong, so neither fails - one aims a pad differently, the other reads as lag.
- ✅ **PP677** **the key state has no managed transcription, so every key position the port expands is the shim's** — Both wrap branches need the RFC comparison AND the plain one to disagree, and neither is true of a repeat - an expansion written with one comparison adds 2^32 to twenty-six of four thousand.
- ✅ **PP678** **the receive loop runs only against test doubles, and nothing owns takion's state** — Receiving is the cheaper half: eight datagrams cost the trace and nothing else, while eight timeouts cost 712 bytes each inside the socket - so an idle session allocates and a busy one does not.
- ✅ **PP679** **the v7 AV parse and header formatter are unported, and the formatter's callers are senkusha's** — The formatter goes with the parse into managed code and the C's copy stands until senkusha.c is ported: moving it there patches the vendored C, deleting it strands two callers.
- ✅ **PP680** **takion_handle_packet_av is only a branch in managed code, so no video packet reaches the flush** — The queue's slot stays a long and the arm holds entries under that handle, which is what the C's void* is; the parse takes a ledger interface, so a session needs no native key state.
- ✅ **PP694** **the microphone's units reach nothing, and libopus's second consumer is why the dependency cannot leave** — Concentus is not bit-exact either way, so an audio port is held to the length and the TOC the protocol reads; and silence encodes to three bytes, which opusencoder.c drops.
- ✅ **PP698** **the echo canceller wants a reference of what is playing and nothing captures the render side** — The reference is a side of WasapiCapture and not a second class: one flag, the render default at the console role, and the same converter that puts both in the announced units.
- ✅ **PP702** **senkusha.c calls five takion symbols, so PP27's fourth criterion cannot be met while the file stands** — Senkusha's five calls have counterparts already, so PP27's fourth criterion needs the file's calls answered rather than a port of it - which of the two is still open.
- ✅ **PP703** **ManagedTakion's video queue is only ever set to null, so one step of its recorded teardown is unreachable** — The dispatch seam hands a MUTABLE datagram, because the C's handler owns the buffer and the AV branch decrypts in it; a read-only view would have cost a copy per packet.
- ✅ **PP706** **the microphone has a capture, a unit splitter, an encoder and a head, and nothing runs them as one path** — The redundancy is one frame deep and not two, because audiosender.c copies the arrival back over slot zero; a port that repaired it would send a packet the console has never been sent.

## Block G — Test discipline

- ✅ **PP683** **the oracle guard census reads test files only, so the selftest's guarded comparisons are invisible to it** — The census is files that guard, wherever they live; what keeps a file out is defining a guard rather than asking one.
- ✅ **PP691** **checks that match a roadmap sentence literally go red when the sentence gets more precise** — A check may match an address roadkeep uses or words held on purpose; a whole sentence of governed prose is the fragile case, and a fifth one is now a decision on record.
- ✅ **PP704** **FeedbackPayloadTests guards eight comparisons on an oracle the census does not name, and it is not the only file** — A guard has three uses and only one costs: a comparison declines, a test of the guard is the check working, and code that reads it asserts nothing.
- ✅ **PP705** **four sweeps over app/ hand-write their own exclusion, so a new census has to be added to the others by hand** — A sweep's exclusion is asked of the swept file, not listed by the sweeper; the marker is asserted to be in exactly the census files, so it cannot become a way of opting out.

## Block H — Performance and telemetry

## Block I — NVIDIA path

- ✅ **PP76** **the decoder preference is measured on synthetic frames, and drops under network jitter are what a stream is judged by** — PP48's copy ranking does not carry: this port downloads every vulkan frame to NV12 for D3D11, so vulkan's no-copy path is not the one a live session measures.
- ✅ **PP709** **nothing drives the in-box echo canceller, so PP52's second criterion has a reading of it and no stage** — The in-box canceller's rates stop at 22050, so no cleaning stage can sit in a 48000 chain until something in the port converts between them.
- ✅ **PP710** **the port cannot change an audio rate, so the cleaner's 22050 ceiling keeps it out of the announced 48000 chain** — A rate change in this port is Windows's own DMO, not a filter it owns; the way in stays free, because the capture engine's converter already does it.
- ✅ **PP52** **nothing runs echo cancellation, and the vendor answer is absent on a machine with the card** — The stage runs at the best rate a ten-millisecond unit divides evenly into, not the canceller's own best; and what it removed is read off the samples rather than off a return code.
- ✅ **PP711** **WasapiCapture can only be asked for the announced format, so the cleaning stage resamples both inputs itself** — The stage keeps two doors: one taking bytes, so its assertions run on a machine with no microphone, and one taking frames an endpoint already converted.

## Block J — Public documentation
