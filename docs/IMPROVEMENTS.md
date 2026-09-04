# Improvements

## Block A — Core

## Block B — Native interop

### §PP93 Three answers to one question

This client carries three different touchpad extents. streamsession.cpp picks per
console - 1920x942 for a PS4, 1919x1079 for a PS5 - which are the real dimensions of a
DualShock 4 and a DualSense pad. controllermanager.h defines PS_TOUCHPAD_MAXX 1920 and
PS_TOUCHPAD_MAXY 1079, which is each axis's larger value and therefore neither pad, and
the dpad-touch path and the SDL touchpad path both use it whichever console is
connected.

On a PS4, holding dpad-down walks the finger to y=1079 on a pad that ends at 942, and
dpad-right reaches x=1920 on a PS5 pad that ends at 1919. The error is always outward,
so the gesture keeps working and stops near the edge rather than at it.

Whether that is worth changing is genuinely open, which is why this is an idea and not a
task. The console may well clamp what it is sent, in which case the only cost is that
the last increment of travel does nothing. Nobody has measured it, and measuring it
needs a PS4 as well as a PS5.

What is not open is that one client should not hold three answers to one question.
Whatever the right pair is, the dpad path and the mouse path should read it from the
same place - and the reason to write that down now is that the port has just copied all
three, so it is the moment when the duplication is visible.

## Block C — Video and input path

### §PP641 The overlay layer, and what draws into it

PP319 chose the arrangement and PP322 read it: a container visual carries a ten-bit
swapchain below and an eight-bit premultiplied surface above, and the two compose in the
order given. What that reading used as the overlay was a green block the shim draws.

PP10's screen is not a green block. It is XAML, drawn by WPF into the window's
redirection bitmap, and PP284 measured that the compositor tree covers that bitmap
whatever the topmost flag says. So the two halves of the choice are not symmetric: the
video plane has somewhere to go the moment a renderer presents into a composition
swapchain, while the overlay has a layer and nothing that draws into it.

Three shapes exist and none is chosen here, because choosing needs the cost of each.
Render the visual tree to a bitmap per frame and upload it, paying a full-screen copy at
HUD update rate. Keep the HUD in WPF and accept SDR while it is up, PP319's rejected
option narrowed to one screen. Or rebuild the HUD against the compositor, which costs
PP10 and PP12 a second time.

This line is the question and not the answer. It is filed now because shipping PP11
deletes the only sentence in the tree that says the overlay layer is empty.

## Block D — Screens

## Block E — Windows-only build

### §PP63 One configure that exists only to be measured

PP62 measured why. MSYS2 MinGW64 ships no qt6-webengine, and the published Windows
releases are MSYS2 builds carrying no Chromium either - v1.10.0's x64 portable is 261.5
MB with no QtWebEngineCore, no icudtl.dat and no .pak. So the before is not something to
download. It has to be built, and MinGW cannot: Chromium on Windows needs MSVC or
clang-cl.

What this is: a second configure, MSVC or clang-cl, building the Qt client with
CHIAKI_HAVE_WEBENGINE defined, once, so measure-startup has a binary to point at. What
it is not: a second build system. compile.cmd stays the tree's only build and the only
gate for a deletion.

Neither half of that toolchain is here, measured 2026-08-16: no cl.exe, no clang-cl, no
LLVM, no Qt under any usual root, and a Visual Studio Installer with no product behind
it. So this line starts with two multi-gigabyte installs - Build Tools with the C++
workload, and Qt for msvc2022_64 carrying QtWebEngine from an installer wanting an
account. That is a decision about the machine rather than a step of the port, and is why
this is open.

The risk a second toolchain brings is that somebody uses it for ordinary work, the build
splits in two, and the port keeps both green for ever. So the constraint is part of the
task: it stays outside compile.cmd's preflight and gates no commit.

The assertion is measure-startup's exit code - 0 rather than 2, which it returns only
where it found Chromium in the tree it measured.

### §PP301 The first green run

PP22 put the native build on a runner and configured it the only way a runner can be
configured - MSVC with the vcpkg toolchain - and that toolchain has never compiled a
line of this tree. Everything anyone here has ever built came through MSYS2 MinGW64, so
the workflow's first push is also the first test of it.

What is likely to be found there, named rather than discovered one red push at a time:
lib/ and third-party/ carry GCC's picky warning set and `-Werror-implicit-function-
declaration`, which cl.exe does not accept; the vendored curl configures itself off the
compiler it detects; nanopb's generator wants a Python 3 that vcpkg does not install;
and libplacebo is found through pkgconf, which vcpkg lays down as a tool rather than on
PATH.

The assertions PP22 shipped cover the file - every path it names, the framework it
installs, the toolchain it configures through - and cover nothing about whether the
build succeeds. That is the honest boundary: only a runner answers that, and pretending
otherwise would put a second build system in a test.

So this is the first green run, and what it costs is whatever the four above turn out to
be. It is filed apart from PP22 because the workflow is worth having while it is red -
the alternative was leaving CI unwritten until someone had a runner to iterate against,
which is how a port keeps building on exactly one machine.

### §PP302 The third verb in PP22's sentence

PP22's line named three things CI had stopped doing - builds, signs, packages - and
shipped two of them. The third is here because it is not the same kind of work: the
other two were files to write, and this one starts with buying something.

What an unsigned Windows application costs its user is concrete rather than theoretical.
SmartScreen shows "Windows protected your PC" on first run of an executable with no
reputation, and the button that runs it anyway is behind "More info". Browsers warn on
the download. The installer PP274 compiles carries the same absence, so the warning is
the first thing a new user sees and the last thing they see before deciding this is not
worth it.

The certificate is the decision. An OV certificate is issued to a name and can be used
from a runner with the key in a secret; an EV one carries reputation from the day it is
issued and lives on hardware, which a hosted runner cannot reach without a signing
service. Azure Trusted Signing sits between the two and is a subscription. All three are
a purchase and an identity check against a legal entity, and none of them is a step this
port can take on its own.

Filed so the gap is written down rather than remembered. The workflow signs nothing
today and says nothing about it, which is the state where a release goes out unsigned
because every part of it was green.

## Block F — Managed core

### §PP27 The transport, and the only place GC is a real question

takion.c is 2007 lines plus takionsendbuffer.c at 277 and reorderqueue.c at 200: the
sequencing, the retransmission, the send window and the reordering a video stream over
UDP needs.

This is the one task in the block where the runtime is a genuine risk rather than a
prejudice. A pause at the wrong moment is a dropped frame, and the traffic is thousands
of small packets a second, each an allocation if written carelessly. .NET has the answer
- Span, ArrayPool, SocketAsyncEventArgs - chosen deliberately.

THE MAC GATE IS ANSWERED. PP610 took PP531's measurement over 4025 heads a PS5 sent:
0.18us managed against 0.08us for the C, inside a 1159us mean gap. Under a fiftieth of a
percent, and the ratio is what a second machine keeps.

THE LOOP AROUND IT IS REACHABLE NOW, which this used to say it was not. Every receive
handler is file-local and removing a `static` is the patch a non-goal refuses, so PP601
named the door: chiaki_takion_connect takes the caller's socket. PP602 found the far end
must answer rather than replay, the tag being drawn fresh inside connect; PP606 built
that peer and PP607 runs a real takion against it, to the connected event.

WHAT IS LEFT IS WHAT TO FEED IT. PP510 keeps eighteen bytes a datagram on purpose -
enough for the dispatch and the MAC layout, and no frame of anybody's screen. Timing the
whole loop wants payloads, so it wants a second decision about what to record rather
than more code.

### §PP30 Reed-Solomon, by hand

third-party/jerasure and third-party/gf-complete implement erasure coding over GF(2^8),
and frameprocessor.c is what calls them: when packets of a video frame are missing, the
FEC blocks are what reconstruct them instead of asking for a retransmission that would
arrive too late to matter.

The surface to port is the call sites rather than the vendored source, so that is what
is declared here, where `remaining PP30` reads 13, across common.c, fec.c and
frameprocessor.c.

```roadkeep-remaining
lib/src/**/*.c :: jerasure|galois_
```

There is no NuGet package that is a drop-in for this, so it is the one dependency in the
block that has to be written rather than referenced. The arithmetic is well understood
and the code is small; what it is not is forgiving - a table built wrong produces frames
that decode into garbage only when packets are actually lost, which is to say only on a
network nobody is testing on.

Two mitigations, both cheap. The tables and the recovery have fixed inputs, so the
oracle covers them completely offline. And keeping the C for this one piece is a
legitimate outcome, because it is self-contained, has no OS surface, and is called with
buffers rather than with state.

### §PP33 Two dependencies that simply leave

holepunch.c is 5962 lines and is the only translation unit in this tree that still needs
either library: 234 curl_easy calls, 4 curl_ws, and the json_object and json_tokener
sites beside them.

```roadkeep-remaining
lib/src/**/*.c :: curl_easy_|json_object|json_tokener
```

Read that count carefully: it reports 420 sites in "45 files", and 45 is every .c under
lib/src - the glob's reach, not the hits. Every one of the 420 is in holepunch.c.

The behaviours are largely ported. PP231 stated the websocket auto-ACK, PP266 performs
the five session calls over a real HttpClient, and the shim exposes json-c's accessors
deliberately: an oracle the managed parser is held against (PP215).

PP663 TOOK THE DEPENDENCY OUT OF THE BUILD. holepunch.c, curl, json-c and the shim's two
oracles all follow CHIAKI_ENABLE_HOLEPUNCH, off by default, with the suite green either
way. So the fourth criterion is met and no build anybody runs links either library.

What is left is the FILE, and what holds it is not a feature. PP654 moved the one
wrapper the host itself reached; the nine that remain are PP481's oracle and the fifteen
beside them are the same for json-c. Deleting the file means deciding what those
comparisons are worth without the C to compare against - and THAT NEEDS A CONSOLE, which
is why this line declares one. PP312 built requirements for this shape; a line needing
hardware and declaring none is one `pick` offers as ready, which this was for seven
sessions. PP621 counts what else the deletion rewrites.

### §PP107 The two that were said to be uncalled

chiaki_reorder_queue_drop and chiaki_reorder_queue_peek are both broken. PP562: the
suite calls both and pins the drop, contrary to this.

drop announces the element to the drop callback and then does not remove it. It never
clears entry->set, so the element stays peekable and pullable - and its own
count-reduction loop, `while(!entry->set)`, cannot run for the same reason. peek writes
through its seq_num pointer unconditionally, and takion.c passes NULL for it. Read, not
run: running it is the crash.

Both are on one path: when crypt becomes available, takion re-checks the MACs of
everything already queued and drops what fails. There, peek cannot survive a set entry,
and a rejected packet is delivered anyway. The path needs a non-empty data queue when
crypt initialises, which is presumably why it has survived.

Decided: accepted. Not patched, because every drift check in this port asserts that the
managed side matches lib/, and a local patch would leave them asserting agreement with a
libchiaki nobody else runs. Reporting upstream stays open and is not this project's to
send.

What that cost was a reason held in prose, and prose does not go red. So
ReorderQueueSource holds five facts about the two and their caller: drop clears no set
flag, its count loop is guarded by the return above it, peek writes both out-pointers
where pull guards its own, and takion still passes NULL and still drops on a bad MAC.
Repair any upstream and the port's copy becomes the divergence, on the next run.

### §PP295 The file every deletion is waiting on

The third of PP28's three, and the one that decides when C starts leaving this build.

PP286 through PP291 ported the frame path from the bottom up: the Galois field, the
Cauchy matrix, the Reed-Solomon codec, the frame processor, the video receiver. None of
it removed a single line of C, and the reason is one call. streamconnection.c:1309 hands
packets to chiaki_video_receiver_av_packet, so videoreceiver.c stays, so
frameprocessor.c stays, so fec.c stays, and jerasure and gf-complete stay with them.
PP30 has read 13 sites through five ports for exactly that reason.

Which makes this the highest-leverage of the three and the hardest. It rides takion -
hence the dependency - and it is the file where the ordering of events IS the behaviour,
so a port that reproduces every function and not their sequence would pass a
message-level comparison and fail a session.

The managed pieces are waiting for it. ManagedVideoReceiver takes a four-method outbound
seam precisely so that whatever drives it does not need to be a session pointer, and
corrupt-frame and IDR requests are two of those four - both of them messages this file
sends.

Deleting is the deliverable, not just porting. The C video receiver leaving the build is
what makes the five ports beneath it real.

### §PP650 Which native decoder, and what each one costs

PP31 settled that the decoder is native. It did not settle which native, and its
rationale listed two candidates without pricing either - which is the shape PP163 was
criticised for one subsystem along.

FFmpeg through P/Invoke is the least change. It keeps every format and every hardware
path the project supports today: chiaki_decoder_choice selects between vulkan, d3d11va
and cuda, PP78's software fallback is the same library, and bitstream.c - which this
port has repaired four times, in PP68, PP69 and PP70 - feeds it. It also keeps a large
native dependency in a solution that was shedding them.

Media Foundation is native to Windows and ships with it. It integrates with the D3D11
texture path PP9's renderer already needs, and it covers d3d11va - which is the floor
row in docs/HARDWARE-CONTRACT.md and, under PP71's paced measurement, beat cuda anyway.
What it does not obviously cover is the software fallback, the parser's edge cases, and
the two paths that would leave with FFmpeg.

Neither half is measured. What would settle it is small and has not been done: enumerate
what Media Foundation offers for H.264 and HEVC on this machine, ask whether it decodes
to a D3D11 texture, and count what the port would lose - the decoder choices, the
fallback, and the megabytes FFmpeg costs the package. The last of those is the only
number anybody has guessed at.

### §PP652 The microphone with no input

PP32 asked whether this host captures a microphone or whether the line should say it
will not. It is neither, and MicrophoneSurface is the census: the port has committed to
the feature in four separate places and produces no samples in any of them.

The setting is declared with the rest of the profile's preferences and bound to a
checkbox on the audio screen. The in-stream menu has the button, with the Qt client's
inversion carried over - lit when the microphone is NOT muted. AudioRing drains the
microphone differently from playback, because the capture path has no target queue size
to stop at. And the DualSense report writes the mic light and the mic mute together,
because a pad left lit with an open mic is the state that matters to a person.

Nothing opens a device. No WASAPI, no NAudio, no MediaCapture, and no managed
counterpart to chiaki_audio_sender.

So the work is a capture path, and what it unblocks is larger than itself: libopus's
second consumer is the microphone's encoder, so the audio dependency cannot leave until
this exists, and the speex stages PP32 opened with have nothing to run on until it does
either. PP31's boundary is silent here on purpose - video decode has no managed answer
and audio capture on Windows has several.

### §PP668 The haptics bit the mirror cannot see

takion.c parses two AV layouts. The shim exposes v9's, and every AvPacket in the port is
built from that call, positionally, with IsHaptics defaulted to false. The default is
the C's own answer today: is_haptics is written exactly once, under `if(v12 &&
!packet->is_video)`, and StreamAvDispatchSource holds that reading so the day the C
changes, the mirror's default stops being honest and a test says so.

But the honest default is also a dead arm. PP667's route tests haptics before audio, as
PP366's third check demands, and on the managed side the test can only ever fail: no
packet the port parses carries the bit. A console that sends the v12 layout - which is
what a DualSense session negotiates - has its haptics packets handed to the speakers as
silence, which is exactly the inversion the ordering exists to prevent, arrived at from
the other direction.

What is owed is the v12 parse: either a second shim export beside the v9 one, or the
parse itself managed, since PP499 already bounded the layout in the C and the arithmetic
is small. Whichever, the join is the same - the parse sets the bit, and
StreamAvDispatchTests gains the one case it cannot write today, a v12 audio packet
routed to the pad.

### §PP671 The default that points at the oracle

Fec.Recovers(FecCase) forwards to Recovers(recorded, managed: false): the two-argument
form PP287 added made the C the default and the managed decoder the one a caller has to
name. That was the right default while the port was being judged against the C. It is
the wrong one for the day the C leaves.

PP670 guarded the two tests that use the default - FecVectorTests' recorded-erasure
theories - so the flip does not turn them red. It also means they decline on a bare
build, and a declined test is a pass that measured nothing (PP663's cost, counted by
OracleGuardCensus). The sixty-four recorded cases are the strongest evidence the port
has, and on a bare build they would assert nothing at all.

The change is one line on the flip: make managed: true the default, so the recorded
cases judge the managed decoder on every build and the differential in FecCodecTests is
the only place the C is still named. Sequenced with the flip rather than done now,
because today the default is what makes those two theories a check on the ORACLE - the
cases are the C's own, and a managed default now would silently stop asking the C
whether it still agrees with its own recording.

### §PP673 The message layer between the branch and the models

takion_handle_packet_message is the layer between the branch PP500 built and the two
models under it: TakionReceivePath hands a control datagram to a sink and stops,
TakionDataDrain models the data queue's flush and TakionDataAck reads the inbound ack,
and nothing joins them.

takion_parse_message refuses three things in order - a header under sixteen bytes, a tag
that is not tag_local, a length field disagreeing with the datagram - and commits the
key position on every message. Then the switch: DATA keeps the buffer, DATA_ACK runs the
ack handler and lets it go, anything else is logged and dropped. TakionMessageHeader
knows four chunk types and neither of these two.

HALF THE PARSE IS WRITTEN. PP672 needed the three refusals for the two acks it reads, so
TakionMessageHeader.TryReadInbound is internal, keeps the C's order, and is joined to
the C by TheCStillRefusesTheseThree. What it lacks is the commit - it hands the position
back as the wire's low half, which is PP677's ledger - so this lifts that reader rather
than writing a second one.

THE ORACLE IS THE CORPUS. PP510 keeps eighteen bytes a datagram and the control header
is sixteen of them, so every control datagram a PS5 sent runs through the parse: the
header tag is that session's one tag_local, and PP515's recorded length is what the
length field has to agree with. A parse refusing one of them, or accepting a head with a
byte moved, is wrong in a way four thousand real messages say so.

### §PP674 The data queue, one width wider

The C stamps its reorder queue out twice from one body, REORDER_QUEUE_INIT giving a
sixteen-bit and a thirty-two-bit init that differ only in the three sequence functions
injected. takion uses both: the video queue is the sixteen-bit one, seeded at
packet_index minus unit_index, and the data queue is the thirty-two-bit one, seeded with
tag_remote and given takion_data_drop.

The managed ReorderQueue is the sixteen-bit instantiation with the arithmetic hardwired:
its constructor takes a ushort, and Push and the comparisons cast to one. SeqNum already
carries the thirty-two-bit comparisons - TakionSendBuffer.Ack uses them across the wrap
- so the arithmetic exists and the queue does not. Neither does the shim export:
chiaki_shim_reorder_queue_create_16 has no sibling.

THE PUSH ABOVE IT IS MISSING TOO. takion_handle_packet_message_data warns when type_b is
not one, drops a payload shorter than nine bytes, and otherwise builds an entry from the
sequence number at the payload's start and the channel four bytes in, pushes it, and
drains. PP491 made both early returns free the packet; PP493 modelled the drain that
follows. What has no managed shape is the entry and the push between them.

TWO WAYS TO WIDEN IT, and the second is the honest one: a generic queue over an injected
comparison, as the C is, held against a create_32 export the same way PP108 and PP150
held the sixteen-bit one - random scripts, both sides stepped, count and drops compared
after every operation.

### §PP675 The sends, from bytes to socket

Every send in takion.c ends in chiaki_takion_send_raw, a send on the connected socket
that turns a negative result into a network error, and no managed file in the host sends
a UDP datagram at all. Above it chiaki_takion_send stamps the packet's MAC under the
cipher's lock and then sends. Above that, three builders: the data message with its
twenty-six bytes of overhead - type byte, header, then sequence number, channel, a zero
word and a type byte before the payload - the continuation that drops the type byte, and
the data ack, twenty-nine bytes with the cumulative sequence, the advertised window and
two zero words, sized for the ledger as a whole packet where the others pass the payload
alone.

WHAT IS RUNNABLE TODAY: TakionMessageHeader.Write for the header,
TakionKeyPosition.Advance for the ledger, TakionSendBuffer.Push for the hold,
TakionPacketMac.Apply for the MAC though it allocates. What is a model: TakionDataSend,
which scripts the failure order and emits no bytes. What is absent: DATA and DATA_ACK as
chunk types, the three byte layouts, the sequence counter, and the socket. The
congestion report is the fourth builder and runs only through the shim.

THE ORACLE IS THE EXPORTED C OVER THE LOOPBACK. chiaki_takion_send_message_data is
exported, and PP607's takion is connected to a socket this process reads. A shim call
that sends through that takion puts the C's own bytes on the peer, tag and all, for the
managed builder to be compared against.

### §PP676 Three sends outside the MAC table

Three sends do not go through chiaki_takion_packet_mac, and PP497's table is right to
know nothing of them. takion_send_feedback_packet takes a key position for the payload
plus one block, encrypts the body in place at that position plus sixteen, writes the
position at offset four, computes the GMAC over the whole packet into offset eight, and
sends raw. The feedback state and the feedback history both funnel through it, one with
a twelve-byte head over feedback.c's v9 or v12 controller layout, the other with the
same head over a history payload. The mic packet is the third: a nineteen-byte head,
twenty on a PS5, the position at fourteen and the MAC at ten.

ALL THREE HOLD THE CIPHER'S LOCK ACROSS THE SEQUENCE, which works because the mutex is
created recursive; TakionKeyPosition.ReentrantCallSites records the two and the reason.
A port that made the lock plain deadlocks on the first feedback packet.

Nothing managed writes any of the three, and feedback.c's two serialisers have no
counterpart either. The primitives are here: StreamAvDispatch.Decrypt is the key-stream
XOR that encryption also is, Ghash.Tag is the GMAC, GkCrypt holds a session's keys.
FeedbackState in app/Session is a name collision - PP5's model of the GUI's input
decisions - and not this packet.

THE ORACLE: the exports exist, so a loopback takion given a GkCrypt built from known
keys produces the C's bytes for the same state.

### §PP677 The key state, in managed code

chiaki_key_state_request_pos is the counter every encrypted byte of a session is keyed
by, expanded from the thirty-two bits on the wire to the sixty-four the cipher needs:
remember the high half, increment it when the low half wraps, and either commit the
result or only peek at it. A parse that may still turn out to be garbage peeks, so a
corrupt packet cannot drag the counter forward.

PP23 reached it through the shim as KeyState, PP111 fed the C suite's cases through that
wrapper, and PP519 fed it a console's own positions, where equal values occur twenty-six
times in two thousand. All three are the C running. SuiteCoverage answers keystate.c
with SeqNumTests and the selftest, and neither holds a managed body of the function,
because there is none.

EVERY MANAGED PARSE NEEDS IT. takion_parse_message commits on every control message;
chiaki_takion_packet_read_key_pos peeks before the MAC gate; av_packet_parse commits
inside the AV header. PP668's v12 parse and the control-message layer both stop at this
wrapper, and a transport whose hot path crosses the seam once per datagram to expand a
counter is not the transport PP44 budgeted.

THE ORACLE IS THE WRAPPER ITSELF, and the corpus: DatagramReplayReport.KeyPositions
already reads the positions off four thousand real heads. The same sequence through
both, committed and peeked, compared at every step.

### §PP678 A takion that owns things

takion_thread_func is more than the loop PP487 modelled. Before it: the handshake, the
data queue seeded from the remote tag, the send buffer, and the connected event. After
it: the send buffer and both queues finalised, anything still postponed released, the
disconnected event, and the socket closed where takion owns it. TakionReceiveLoop.Run
covers the middle and returns a trace; the bookends have no managed shape.

THE HOST IS A TEST DOUBLE THREE TIMES OVER. ITakionLoopHost asks for the cipher's
presence, the postponed packets, the next timeout, a receive into a buffer and a
dispatch, and every implementation lives in the test project. Nothing selects on a
socket with a stop pipe the way takion_recv does, and TakionDatagramRead triages a
result nothing produces.

AND NOTHING OWNS THE STATE. The local tag and the sequence counter, tag_remote, the key
position ledger, the cipher pair, the two queues, the postpone array, the send buffer:
each is modelled or runnable on its own and none has an owner, so the pieces cannot be
composed into a takion that a session could hold.

WHAT THIS IS: a managed takion object, built on the client handshake once that exists,
with the socket that handshake used as the loop's host. THE ORACLE IS PP606'S RESPONDER
for the connect and PP608's corpus for the loop, with PP44's zero allocation per
datagram measured the way PP633 measured it.

### §PP679 Version seven, and whose it is

takion.c carries three AV header parsers and the version chosen at connect picks one.
The v9 and v12 parsers are one body with a flag, which PP668 owns. The v7 parser is a
body of its own and differs in three places: its bound counts the NALU add for video as
well as audio, its packed word always takes the video layout whatever the base type, and
its key position is read raw as thirty-two bits with no key state behind it.

Beside it sits the file's only header FORMATTER,
chiaki_takion_v7_av_packet_format_header, and its two callers are in senkusha.c - the
MTU and bandwidth probe - not in takion's receive path. So when takion.c leaves the
build the formatter has to go somewhere, and PP27's fourth criterion names three files
and senkusha.c is not one of them.

NOTHING MANAGED ANSWERS EITHER. No shim export, no model, no test; the one mention in
the tree reads the v7 constant's name inside the v9 parse. NativeTakionLoopback accepts
version seven, so the C path is reachable for a comparison.

THE DECISION, and it is one: the parse ports with the other two as a third mode of one
function, or as its own body the way the C keeps it; the formatter moves with the parse,
or with senkusha's own port. What is refused is deleting a formatter whose callers still
stand.

### §PP680 The AV arm, assembled

takion_handle_packet_av is the AV branch of the dispatch and the managed side stops
where the branch does: TakionReceivePath classifies the datagram and hands the bytes to
a sink, and the sink in the tree copies them into a scratch buffer and counts.

WHAT THE C DOES AFTER THE BRANCH. Video is dropped when the session disabled it, before
any parse; the parse runs; audio is dropped when disabled unless the packet is haptics;
audio goes straight to the callback and its buffer is freed; video lazily creates the
reorder queue on its first packet - begin at packet_index minus unit_index, the drop
strategy walking begin forward, takion_av_drop as the callback - pushes an entry
carrying the buffer, the parsed header and the arrival stamp, charges the receive stage,
and flushes with the timeout. A failed queue init dispatches unreordered rather than
dropping.

WHAT EXISTS: AvReorderTimeout.Flush over the sixteen-bit queue, StreamAvDispatch as the
callback's far end, TakionReceiveBuffer.Retain for the copy. WHAT DOES NOT: the gates,
the lazy init, the entry, the stage stamp, and a queue whose slot can hold a datagram
rather than a long.

THE PARSE IS PP668'S and this waits on it; until then Takion.ParseV9 is the shim's
answer for a v9 stream. THE ORACLE IS THE CORPUS: PP608's heads carry every index the
queue is seeded and ordered by, so the managed arm's order of delivery and its drops are
compared against AvReorderTimeout's and the C's over a real stream.

### §PP681 The tenth wrapper's guard reads text

PP661 moved the nine wrappers' guard off the header's text and onto the built DLL,
because PP655's flip puts the declarations inside an #ifdef rather than deleting them,
and text that still says Wrapping over a DLL that exports nothing is how the first
attempt turned a hundred and twenty-eight assertions red. PP656 then made the device
id's oracle shape-aware - by reading the header for its name. That is the text again,
one wrapper over.

PP663 turned holepunch off by default, so every ordinary compile.cmd produces a shim
without chiaki_shim_generate_client_device_uid while chiaki_shim.h goes on declaring it.
The selftest asks TheFormatOracleIsAvailable, is told yes, calls
PsnAuth.NativeDeviceUid, and the process dies on an unhandled
EntryPointNotFoundException in the middle of the RpCrypt oracle - every check after that
line never runs. Found on 2026-09-03 running test.cmd after a plain build, and it had
been dying that way on every default build since the flag went in; the sibling line
under Block G says why nobody saw it.

THE FIX IS PP661'S, APPLIED TO THE TENTH: key the guard on OfTheBuild, which asks
chiaki_shim_has_holepunch of the DLL that was loaded, since the wrapper's C is in
holepunch.c and leaves with the nine. And the assertion that holds it: on a bare build
the guard says no where the header's text says yes, and ChiakiNg.exe --selftest exits
zero on the shim the gate itself built.

## Block G — Test discipline

### §PP642 Checking where a deleted design went

`ship --recorded-in` takes a path, requires it to resolve, and writes "(design recorded
in `x`)" into the ledger entry. That is the whole of the check. The file is not read, so
nothing distinguishes an entry whose paragraph moved from one whose paragraph was never
written - and the second is the easy mistake, because the flag is passed in the same
call that deletes the section it claims to have moved.

PP11 is the first entry in this ledger to carry the clause. What holds its recording is
a test written by hand for that one entry: it asserts the clause is in the ledger and
that four phrases of the constraint are in the file. That works and it does not
generalise - the next `--recorded-in` gets nothing unless somebody remembers to write
the same test again, which is the shape of a discipline that decays.

What a check could do without judging prose: for every ledger entry carrying the clause,
the path resolves from the repository root, and the file names the id. Naming the id is
the same join the assertion ratchet already uses and it is exactly as strong - it cannot
tell a recording from a mention, and it can tell a recording from nothing at all.

Where it lives is the open question. The ratchet reads the ledger already, so the
cheapest home is beside it; the alternative is roadkeep's own lint, which would make it
every project's rule rather than this one's.

### §PP643 A docstring on the wrong member

Two `<summary>` elements on one member is not a compiler error. The documentation
generator takes one and drops the other, and a reader of the source sees both - so a
docstring can describe a member two declarations further down while sitting on a member
it says nothing true about.

That happened in RenderProbeTests. PP322's attach test shipped with its docstring
stacked above `TheReadingsApparatusIsUnchanged`, which then carried two summaries and no
complaint, while `TheTwoLayerTreeAttachesToAWpfWindowAndDetaches` carried none at all.
It was found by reading, during PP11's ship, and corrected there.

This tree leans on docstrings harder than most: the assertion ratchet joins tasks to
tests by the id in a test's summary, so a summary attached to the wrong member is a
coverage claim made about the wrong thing. That is the reason to check it here rather
than treat it as a style preference.

The check is a scan and not a parse. Within a member's leading run of `///` lines, count
the `<summary>` opens; more than one is the finding, and the member it sits on is what
to name. It costs nothing over a tree the drift checks already walk, and it is filed as
an idea rather than designed because one occurrence is not yet evidence of a class.

### §PP659 A gate with a datagram in it

SessionRelayTests.TheConsolesAnswerComesBackMarked opens three UDP sockets on the
loopback, sends a datagram through the relay in each direction, and asserts on what the
tap recorded. It failed once on 2026-09-03 in a run whose two neighbours on either side
passed, and passed on its own immediately after.

Once is not a pattern and this is filed as an idea rather than a defect for that reason.
What makes it worth filing anyway is the shape: UDP on the loopback is allowed to drop,
the receives carry a five second timeout, and the assertion is on a list something else
fills from a relay thread. Every one of those is a way for the run rather than the code
to decide the answer.

The cost of leaving it is specific. This tree's gate is read - the ratchet, the counted
claims and the drift checks all report through it - and a check that fails one run in
some number teaches a reader to re-run rather than read. PP56 was the same problem
facing the other way, where a stale binary made the suite green about code that had
changed.

What would settle it is a count rather than an argument: run the file a few hundred
times and see whether it fails again. If it does, the fix is a bounded retry on the
receive or a tap the assertion can wait on, and which of those depends on where it
actually loses.

### §PP666 A test written from the table it checks

PP364 modelled the stream connection's six exit labels as a ladder: what was built
decides where a failure enters. Its test asserted that entering after N things built
runs N plus one labels. Both were wrong in the same direction - every rung one label too
early - and they passed each other for five months, because the test's arithmetic was
derived from the table it was checking. A connect failure entered at close_takion, which
would close a takion that never connected. Three other rungs were hidden by null-safe
frees.

What found it was PP295's managed run: a consumer that had to DRIVE the table against
the C with the file open beside it. A table nobody drives is a claim that only its own
test reads, and a test written from a table inherits its error.

Three more tables in this tree have that shape. PP623's deletion stages, PP639's
end-state waits, and PP640's six orderings are each a list something asserts the
presence of and nothing consumes. None is known to be wrong. What is known is that the
mechanism which found PP364's defect - a consumer, held against the source, not the
table - exists for none of them.

The fix is not a rule about tables. It is asking, for each, what would drive it, and
whether that thing is cheaper than the next five months.

### §PP682 A gate that cannot see a crash

cmd's `if errorlevel N` is true when the errorlevel is N OR ABOVE, so `if errorlevel 1`
is the idiom for "failed" only while failures are positive. An unhandled exception ends
a .NET process with 0xE0434352, which the shell holds as a negative number, and a
negative number is below one. test.cmd reads five verdicts that way - the host's
selftest, compare-baselines, measure-startup, roadkeep lint and dotnet test - and
compile.cmd reads dotnet build the same way.

WHAT IT HID. The selftest has been crashing on every default build since PP663 took
holepunch out of the ordinary shim: the device id guard reads the header's text and says
yes, NativeDeviceUid throws, the exception is unhandled, and the exit code is the
runtime's. The gate saw a number below one, kept CRC at zero, and printed OK over a
selftest that ran to the RpCrypt oracle and no further. That is the lie PP56, PP74 and
PP75 are each about - a green over assertions nobody ran - arriving through the shell's
comparison rather than through a binary nobody built.

THE FIX IS ONE COMPARISON, SIX TIMES: test the errorlevel for anything other than zero,
either `if not "%errorlevel%"=="0"` after each call or the pair `if errorlevel 1` and
`if not errorlevel 0`. And the assertion that holds it, in the shape PP588 and PP589
gave the gate's own text: a test reads test.cmd and compile.cmd and refuses a verdict
line that catches one sign only.

## Block H — Performance and telemetry

### §PP46 Two numbers that are easy and get assumed

QtWebEngine is in the build for one login screen and WebView2 replaces it with a control
the operating system already carries. The expected result is a smaller installer and a
faster cold start, and both are trivially measurable and routinely asserted without
being.

Cold start to the console list, installer size, and process working set at idle.
Recorded on the Qt build alongside the rest of the baseline, then again after, in the
same record the sink already writes.

Small task, and it is here because these are the two numbers most likely to be quoted in
a release note. A quoted number that nobody measured is the kind of claim that survives
long past the day it stopped being true.

### §PP55 The instrument outside the process

PP40 shipped the half that a regression test can use: input queueing, the console's
reported round trip and the decode-to-present handoff, summed into a floor. What it
cannot do is say whether that floor tracks the real click-to-photon delay, because every
term it is missing - the console's input handling, the game's render, the encoder, the
display's own pipeline - lives outside this process.

Reflex Latency Analyzer measures click to photon on a monitor that supports it, without
a camera rig. It does not apply to this client as a low latency mode: Reflex controls a
render queue this application does not have. As a measuring device it answers exactly
the question, and it is the reason this line is filed rather than folded into PP40.

The hardware is what blocks it: the development machine has an NVIDIA card but no
Reflex-capable monitor, so no number can be taken today. Taken later against a converted
tree, it measures the port instead of the client - the same window that closes on PP39
closes on this.

### §PP303 Whether PP46 still earns PP63

PP46 was filed when this port read as chiaki-ng continued by other means: same name,
same version, the next thing a user of it would install. Under that reading, "dropping
the bundled browser makes startup and the installer smaller" is a claim about an
upgrade, and measuring it against the build being replaced is the only honest way to
state it.

That reading was settled against on 2026-08-22, in PP277: this is a new application
rather than upstream's next version, it inherits nothing from an installed one, and its
installer now says so with an identity of its own. A delta measured against a Qt build
is then a comparison between two products, which is a different sentence and a weaker
one.

What it costs to keep is not small. PP63 is what produces the before, and PP63 is two
multi-gigabyte installs - Build Tools with the C++ workload, and Qt for msvc2022_64
carrying QtWebEngine from an account-gated installer - plus a second toolchain that the
task itself argues has to be kept away from ordinary work.

So the question is whether PP46 still earns PP63, and there are three answers. Keep
both. Re-base PP46 on this application alone - cold start and installer size as a budget
with a ceiling, needing no Qt at all. Or retire both, and let the browser this port does
not bundle be a fact rather than a measurement. What a number is for is the author's
call.

## Block I — NVIDIA path

### §PP47 The right NVIDIA feature, waiting on a switch

The shipped half: DLSS cannot apply here, RTX Video Super Resolution is the candidate,
and the floor is measured. The plain upscale from 1080p NV12 to 4K costs 262.9us mean
and 274.1us p99 on the RTX 4060 - 1.6% of a frame at 60fps. Whatever VSR costs, it costs
that plus something.

What is left is VSR's own number. The spike in spike/video-upscale sets the stream
extension and nothing changes: 0 of 8.3 million pixels differ, while nvsvsr.dll and
nvvitvsr.dll sit in the driver store, so the feature is installed and unreached.

Three candidates were filed and two are now dropped. The GUID is mpv's, corroborated
across three independent retrievals. Offscreen output is not disqualifying: mpv's own
filter writes to an ordinary texture and works.

What survives is the driver's own switch, and mpv documents it: the option "only enables
the appropriate processing extensions; whether it actually works depends on your
hardware and the settings in your GPU driver's control panel". The remaining step is a
human one: NVIDIA Control Panel, Video, RTX Video Enhancement, then re-run.

That is a finding rather than a defect, and it belongs to PP51 as much as here: a vendor
path needing a control panel visit has a different contract from one that does not, and
a user who never opens that panel gets the unaccelerated path silently.

The quality half stays unanswerable here regardless. It needs a real decoded frame,
which needs a console, so the synthetic pattern settles cost and never benefit.

### §PP49 HDR on a stream that does not carry it

The window already deals with HDR when the stream is HDR. The case this covers is the
other one: an SDR stream on a display capable of more, which is most sessions on most
titles.

The cost half is done and it came back cheap. RTX Video HDR engages on this card and
costs 29.0us a frame, 0.17% of a 60fps interval - see spike/video-hdr, and the ledger
for the number. So cost is not what decides this feature, which is worth saying because
it is the question this line was filed to answer.

A sentence that stood here is now false, and its correction is why the spike was written
carefully. This said RTX Video HDR "runs on the same NGX surface as the upscaler". It
does not: super resolution is the NVIDIA PPE interface at method 2 and true HDR is an
interface of its own at method 3, so a spike inheriting PP47's constant would have set
an extension the driver knows, been accepted, and reported PP47's finding as news.

What is left is the half a number cannot reach. An inferred HDR image is an opinion
about colour the source did not express, and on some content it looks worse - so the
picture has to be judged on a decoded console frame rather than on the synthetic chart
the cost was taken from, and whatever ships is a setting that turns off with a fidelity
mode bypassing it. Both are criteria on the line now.

### §PP52 A vendor feature that no longer pays a debt

This line was written about a dependency that has since left. streamsession.h carried
SpeexEchoState, an echo suppress level and two conversion buffers, and PP32 established
that all of it was the Qt client's - lib references speex nowhere, gui/ was the only
thing that linked it, and the probe now runs only where gui/ is built. So the half of
this task that was "delete a dependency the port has no answer for" is done, and it was
done by removing the client rather than by replacing the algorithm.

What is left is the other half, and it is an addition rather than a repayment. NVIDIA's
audio effects SDK does noise removal and echo cancellation on the GPU, which is the same
job with better results on a machine that has the card.

Three shipped findings bind it now. PP652 has to land first: nothing in this host opens
a capture device, so there are no samples for a stage to clean and no CPU cost to
compare against. PP648 measured that these features sit behind per-feature switches in
the vendor's control panel and that a call which succeeds is not a feature that ran - so
whatever ships reads back the effect. And PP647 put a floor row in
docs/HARDWARE-CONTRACT.md saying the present path names no vendor at all; this would be
the first vendor path in the audio one, and the non-goal binds it to a fallback that is
not visible to the user.

### §PP53 The one that removes waiting instead of adding work

Nothing in the window mentions VRR, G-SYNC or adaptive sync. Frames from a console
arrive when the network delivers them - irregularly by nature - and a fixed refresh
present rounds every one of them up to the next vblank. At 60Hz that is up to 16ms of
pure waiting, added to a frame that already travelled a network.

Variable refresh is the direct answer, and it is the only item in this block that makes
the picture arrive earlier rather than look better or arrive smoother.

The first thing measured was whether PP319's choice had already cost it. It has not, at
either depth. DXGI takes DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING on a composition swapchain
and presents with it, and PP646 asked again through a committed tree - the swapchain as
a visual's content on a real window - where it survives too. Both refuse that present
where the flag was not asked for, so the flags are read. A sentence here is corrected by
that: it said exclusive fullscreen is the usual precondition, and the tearing pair is
what replaced needing one.

What is left is the half an API cannot answer. A composed frame goes through DWM, so a
flag DXGI accepted is not a panel that followed - which is the mistake PP163 made one
subsystem along, and there is no display here that varies its refresh to check it on.
Below the display's minimum, low framerate compensation changes the behaviour again, and
that too is read rather than assumed.

### §PP76 The decoder half a spike cannot reach

PP48 settled what a generated stream can settle. All three hardware paths decode within
13% of each other on an RTX 4060, and what separates them is the per-frame copy
make_fallback_snapshot_frame runs for any hardware frame that is not AV_PIX_FMT_VULKAN -
793us on cuda, 2253us on d3d11va, nothing on vulkan. PP71 then paced the same three at
60fps and reversed the send ranking cuda was preferred for.

None of that is the number a user feels. Frames arrive late and out of order because the
network is what it is, and which decoder loses the fewest of them under that jitter is a
property of the live path rather than of the silicon. A generator can carry resolution
and bitrate; it cannot carry a congested link, and every attempt to synthesise one
measures the synthesiser.

This line said no new instrument was needed, and that was read rather than checked.
Neither loss counter attributes a loss to a decoder: frames_lost is the video receiver's
own total, counted upstream of every one of them, and frames_dropped went missing on two
decoder-dependent returns. PP528 repaired that counter, and
chiaki_session_baseline_decoder_drops names the subtraction the comparison rests on - a
floor on the decoder's own loss rather than a count of it.

What is left is the run, and it is a run: sessions per decoder on a link that jitters,
which takes a console and somebody playing on it. Reading either counter on its own is
what this waits to prevent.

## Block J — Public documentation
