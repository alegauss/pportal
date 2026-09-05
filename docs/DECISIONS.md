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
- ✅ **PP708** **nothing in the port renders audio, so a session shows a picture and plays no sound at all** — The WASAPI surface is declared once for both directions, and a render's silence is a flag on a released buffer rather than a pass the pump skips.
- ✅ **PP712** **three rows of the run-host census name a type with no member that answers, and the check cannot see it** — A counterpart names the member that does the work; where the runtime removes the need - a free, a lock - the row says so rather than naming a plausible type.
- ✅ **PP714** **nothing managed reports congestion, so a managed run would tell the console nothing about what it lost** — The sequence span is an int subtraction widened to 64 bits, not a 16-bit wrap: a ceiling below its floor reports about 1.8e19 lost, and the port reproduces it.
- ✅ **PP717** **nothing decides which history events a controller change becomes, so PP676's serialisers have no caller** — A slot whose finger changed emits the old one's release and not the new one's press - the C's branch is an else, and the arrival waits for the next change.
- ✅ **PP718** **PP585's wait census still calls congestioncontrol.c unported, and nothing in the gate can tell it is wrong** — A census asserted by group counts survives a row moving between groups, so each group also has to check the claim its rows make rather than only its size.
- ✅ **PP719** **nothing managed raises a session event, so the frame path's nine reach nobody and the run's CONNECTED is owed** — An event raised with nobody listening is dropped and counted rather than refused, because every raiser in the frame path is written as though a send cannot fail.
- ✅ **PP723** **nothing composes PP676's serialisers into a sender, so a controller change is recorded and never reaches a wire** — The input delay is sampled only where a handover reached the socket, so the keepalive's own sends and the changes the console does not care about are not counted as input.
- ✅ **PP726** **nothing managed formats the launch spec, so the JSON that tells the console the stream's shape has no port** — The launch spec is the C's template byte for byte: a field this port thought better, or a key order it found tidier, would be a message no console has ever been sent.
- ✅ **PP727** **nothing turns the launch spec into the BIG's payload, and the obfuscation there is not the encryption it looks like** — Collapsing the zero-buffer encrypt and the XOR into one call changes the cipher mode: it agrees for one block, differs after it, and the symptom is a console that never answers.
- ✅ **PP721** **nothing calls the managed event seam, so the five a pad info decides and the FEC failure still reach nobody** — The pad state belongs to the dispatch layer and not to the parse: a refused length leaves it alone, so the message after a bad one is judged against what the console actually said.
- ✅ **PP729** **the third dispatch layer routes a protobuf to three handlers and the one that keys the session has no port** — An over-long ECDH field fails the decode and a missing one refuses the bang, so two shapes of a bad key leave the state differently: one still waiting, one refused.
- ✅ **PP730** **nanopb refuses a bang with no required fields and this port reads it as a refusal, so one message leaves two states** — The managed parser is the lenient half of PP25's pair, so a reader that decides on a message checks its required set first or it is answering about bytes nanopb refused.
- ✅ **PP732** **two more managed readers decide on a message nanopb would refuse, and one of them is the streaminfo** — An absent required field and an empty one are different messages: nanopb refuses the first and keeps the second, so a reader treating them alike answers about bytes no console sent.
- ✅ **PP713** **eleven rows of the frame path's census name a type with no member, and nothing says which are ctors** — A counterpart naming no member states which of three reasons that is, because one legitimate way to say nothing is how the other reasons get in unexamined.
- ✅ **PP716** **packetstats' sequence arm is pushed with no mutex while three neighbours take one, and two threads reach it** — A departure is reproduced where the C's flaw is visible to a user or a console, and corrected where it is not; this one is a report off by a packet, which is jitter.
- ✅ **PP715** **one wrap past 65535 makes the client report 1.8e19 packets lost, and nothing says what the console does then** — A clamp producing a pair too wide for the field it is sent in produces nothing: the narrowing is part of the arithmetic, not a formality after it.
- ✅ **PP725** **the sender's overflow rung copies a packet buffer onto itself, and nothing records that the port left it out** — What a departure needs is the arithmetic that makes it one: a text search finds the call, and only the modulo says both its arguments are the same slot.
- ✅ **PP731** **the step after the bang builds two gk crypts at fixed indices and hands them to takion, and none of it is ported** — The C's key buffer is a cache, so computing the stream on demand is the same bytes with no thread to release - which is what makes leaving it out cost nothing.
- ✅ **PP737** **the key stream makes an AES object and an array per block on every call, and nothing has held it to PP44's budget** — ECB carries nothing between blocks, so the counters can be built together and encrypted once - which is what turns a per-block cost into a per-call one.

## Block G — Test discipline

- ✅ **PP683** **the oracle guard census reads test files only, so the selftest's guarded comparisons are invisible to it** — The census is files that guard, wherever they live; what keeps a file out is defining a guard rather than asking one.
- ✅ **PP691** **checks that match a roadmap sentence literally go red when the sentence gets more precise** — A check may match an address roadkeep uses or words held on purpose; a whole sentence of governed prose is the fragile case, and a fifth one is now a decision on record.
- ✅ **PP704** **FeedbackPayloadTests guards eight comparisons on an oracle the census does not name, and it is not the only file** — A guard has three uses and only one costs: a comparison declines, a test of the guard is the check working, and code that reads it asserts nothing.
- ✅ **PP705** **four sweeps over app/ hand-write their own exclusion, so a new census has to be added to the others by hand** — A sweep's exclusion is asked of the swept file, not listed by the sweeper; the marker is asserted to be in exactly the census files, so it cannot become a way of opting out.
- ✅ **PP720** **the suite's staleness warning globs every .c under lib, so a file no target builds warns on every run** — A staleness guard asks the build graph and not the tree: this checkout keeps C that no target compiles, so a glob warns about files no rebuild can bring up to date.

## Block H — Performance and telemetry

## Block I — NVIDIA path

- ✅ **PP76** **the decoder preference is measured on synthetic frames, and drops under network jitter are what a stream is judged by** — PP48's copy ranking does not carry: this port downloads every vulkan frame to NV12 for D3D11, so vulkan's no-copy path is not the one a live session measures.
- ✅ **PP709** **nothing drives the in-box echo canceller, so PP52's second criterion has a reading of it and no stage** — The in-box canceller's rates stop at 22050, so no cleaning stage can sit in a 48000 chain until something in the port converts between them.
- ✅ **PP710** **the port cannot change an audio rate, so the cleaner's 22050 ceiling keeps it out of the announced 48000 chain** — A rate change in this port is Windows's own DMO, not a filter it owns; the way in stays free, because the capture engine's converter already does it.
- ✅ **PP52** **nothing runs echo cancellation, and the vendor answer is absent on a machine with the card** — The stage runs at the best rate a ten-millisecond unit divides evenly into, not the canceller's own best; and what it removed is read off the samples rather than off a return code.
- ✅ **PP711** **WasapiCapture can only be asked for the announced format, so the cleaning stage resamples both inputs itself** — The stage keeps two doors: one taking bytes, so its assertions run on a machine with no microphone, and one taking frames an endpoint already converted.

## Block J — Public documentation
