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

### §PP819 SDL is the port's own dependency now, not the C's

SDL is not in lib at all. It is in gui, which is being replaced, and in app itself:
eighteen entry points in Gamepads.cs, every one of them an event, joystick or
gamecontroller call. No audio, no video, no window and no rumble - PP97's fold has no
output call behind it yet. So once PP816 takes the render path off it, SDL2.dll stays in
the package for one job.

STRATEGY names this case in its first paragraph, and what the port pays is not the DLL.
It is the modelling around it: the index-versus-instance-id asymmetry a roster has to
carry, the SDL_Event layout PP579 holds against a header, the field SDL rewrites on any
pushed event, the four-millisecond pump PP118 gave a thread of its own, the load PP117
had to make report rather than hang.

Windows ships both halves. RawInput and the HID API carry a DualSense's touchpad,
gyroscope and trigger reports, which is the same data SDL parses out of them;
Windows.Gaming.Input covers XInput pads and their arrival without a pump.

One cost should decide the shape rather than be discovered late. SDL's controller
database covers thousands of third-party pads and native HID supports what this port
writes support for, so the first question is which devices the port owes support to -
and the mapping screen PP18 built against SDL's mapping strings changes with that
answer.

## Block C — Video and input path

### §PP821 The frame that leaves the GPU to arrive where it started

chiaki_shim.c downloads a hardware frame to system memory as NV12 and the presenter
uploads it back into an ID3D11Texture2D. The branch that decides is one test,
if(self->held->hw_frames_ctx), so every hardware decoder takes it - and the file says
why: it was written for AV_PIX_FMT_VULKAN, which a D3D11 presenter cannot share a
texture with.

That reasoning held while vulkan was the path. It does not hold for d3d11va, whose frame
is already an ID3D11Texture2D on a D3D11 device, and PP9's design says what should
happen to it: pl_d3d11_wrap takes such a texture directly. So the port pulls a frame off
the GPU and pushes it back to arrive at what it already had.

WHAT IT COSTS IS MEASURED, and only one half of it. spike/decode-path timed
av_hwframe_transfer_data alone at 2253.1 microseconds a frame for d3d11va - 13.5 percent
of a 60fps interval, and more than that decoder's whole decode. The upload back is not
in that number.

WHAT THIS IS NOT is a decoder change. The choice stays where PP77 put it and vulkan
keeps the download it needs. What changes is that the branch asks the frame's format
rather than whether it is hardware at all.

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

### §PP784 The caller that does not go with the stream

PP780 asked the linker what takion's deletion costs and got four lib files. Three of
them go when the stream connection does - streamconnection.c is its own subject, and
feedbacksender.c and congestioncontrol.c stop being linked at all once nothing pulls
them. Senkusha is the fourth and it goes with nothing: session.c runs it BEFORE the
stream phase, to measure the MTU and the round trip the launch spec spends.

FOUR CALLS, AND PP702 NAMED A COUNTERPART FOR EACH. connect and close are
ManagedTakion's own lifecycle, send_message_data is what TakionDataDatagrams writes, and
send_raw is TakionSendPath's. The fifth symbol - the v7 header formatter - lives in
takion.c and its only callers are senkusha's, which is why PP679 had to decide who owned
it first.

TWO SHAPES, AND PP702 NAMED BOTH RATHER THAN CHOOSING. Porting senkusha means taking on
a file whose subject is a measurement, and the port would then own the numbers the BIG
carries. Answering the four where they stand is a seam: senkusha keeps its logic in C
and its transport calls reach a managed takion through the shim, which is smaller and
leaves a file the port does not own calling into code it does.

PORTING IS THE CALL, and it is the author's. The seam was cheaper and leaves a file this
port does not own calling code it does - a boundary needing explaining at every later
reading. Five lines carry the port, and the file stops calling takion when the last
lands.

### §PP824 The stream's half of the ack

PP807 shipped one of the two routers. `StreamArrivals` has held the seam since that line
was filed - an `IDataAckSink? Acks` set after construction, called below the routing
exactly where `takion_flush_data_queue` sends - and `ManagedStreamPhase` is the
composition root that does not set it. One line, deliberately absent.

WHAT IT COST WHEN IT WAS PRESENT is measured, which is why it came out. Three live runs
on 2026-09-07 against a console in an active game:

    with the join     1171 and 1265 arrivals, 0 decoded, 0 audio, RemoteDisconnected
    without it        14158 arrivals, 984 decoded, 1863 audio, no disconnect

About 1170 unsigned control packets reached a console that verifies them, and it dropped
the session rather than the packets.

PP811 REMOVED THAT REASON AND DID NOT PROVE THE JOIN SAFE. The ack now goes through
`TakionSendPath`, so it is MAC'd under gkcrypt_local wherever a cipher exists - the C's
own behaviour. Nothing has measured a SIGNED ack against a console, and the numbers
above say nothing about one.

AND PP823 IS WHY THIS WAITS ON MORE THAN A RUN. The MAC is computed AT the key position,
and the port takes that position from the receive-side ledger rather than from the send
counter the C keeps. A signed ack at a position the console did not advance is a tag it
cannot verify - arguably worse than an unsigned one, because the console now has grounds
to reject rather than a field to ignore. So the order is PP823 first, then this, then a
run.

THE RE-ENTRY TRIGGER is PP823 landing plus one live session in rest mode.

### §PP825 The microphone packet nothing puts on a wire

PP823 found this by counting rather than by reading. `TakionKeyPosition.CallSites`
records SIX sites in the C that ask the ledger for a position, and ManagedTakion has
FOUR sends that take one. The two missing are `chiaki_takion_send_message_data_cont` and
`chiaki_takion_send_mic_packet`.

THE MICROPHONE IS THE REAL ONE. Every piece exists: PP652 opens the capture, PP694
encodes, PP706 runs all four stages to a formatted packet, PP676 ported the send
sequence itself, and `TakionFeedbackSends.MicrophoneFor` gives it the layout - nineteen
bytes of head, twenty on a PS5. Nothing calls that overload outside the test project,
and ManagedTakion has no mic send at all. So a port that can capture, clean, encode and
format cannot transmit.

THE CONTINUATION IS NOT CLAIMED HERE. `TakionDataDatagrams.WriteContinuation` has the
bytes, and whether the port reaches a message long enough to need one is a separate
question - `BigFragments` splits the BIG above this layer.

THE SHAPE IS THE ONE THIS SEAM KEEPS PRODUCING. PP748 was a data message with no join,
PP811 a send path with no caller, PP823 a ledger with no caller - each ported correctly,
tested, and reached by nothing. `SeamReach` catches the interface version of this, and a
static class with no implementations to count is invisible to it: worth knowing about
the check as much as about the microphone.

WHAT IT NEEDS FIRST is the send, then somewhere in the session owning a capture to call
it.

### §PP828 The copy PP826 proved and did not price

PP818 credited Media Foundation with no readback and PP826 read the bind flags: the
decoded surface is `D3D11_BIND_DECODER` with no `BIND_SHADER_RESOURCE`, in an array of
eight, NV12 at 1920x1088. A renderer cannot sample it, so a copy exists. What nobody has
is its cost, and until somebody does the comparison PP650 opened has one column that is
a decode plus a question mark.

IT IS NOT `d3d11va`'S COPY AND MUST NOT BE PRICED AS ONE. That 2 253us is
`av_hwframe_transfer_data` pulling a frame into SYSTEM memory; this is a
`CopySubresourceRegion` from one GPU texture to another. It should be an order of
magnitude cheaper - and "should be" is the kind of reasoning PP826 just overturned,
which is why this is a line rather than a sentence in a README.

THE MEASUREMENT NEEDS A GPU CLOCK. A copy is queued, not performed, when
CopySubresourceRegion returns, so a Stopwatch around it times the queueing. What answers
is a D3D11 timestamp query pair against a disjoint query, or a flush and an event query
- and getting it wrong reports a copy costing nothing, which is the answer this chain
has already been wrong about once.

THE DESTINATION IS PART OF THE QUESTION. The copy is into a texture the renderer can
sample, so its format and flags are the present path's choice: PP319 picked that path
and spike/present-path is where a real one exists. A copy into a texture nobody would
render from would measure the wrong thing.

### §PP830 The stream the relay carries for 85 milliseconds

PP829 forwarded senkusha's port and the session now gets past its first handshake: the
C's senkusha completes through the relay, takes its ten pongs and measures an outbound
MTU of 1453. What follows is `[E] StreamConnection run failed`, three times on
2026-09-08.

READING THE CAPTURE CORRECTED THIS SECTION'S FIRST ANSWER. It counted 14 control, 11
audio and one video and called that the stream starting. Every one of the 26 rows is
SENKUSHA: the handshake, ten 548-byte pongs, both MTU probes and the data acks between
them. The whole capture spans 85ms, which is one senkusha run.

SO THE STREAM RECEIVED NOTHING AT ALL. The capture keeps only what came FROM the
console, so what this says is that the console sent the stream nothing through the
relay.

AND A PER-LEG COUNT SAYS WHERE TO LOOK: `relay carried 0 on the stream leg and 51 on
senkusha's`. Nobody dialled 9296, which is PP769 - the stream phase ADOPTS session.c's
data_sock rather than connecting, so it rides senkusha's leg. That leg learns its client
from whoever last spoke on it, and senkusha closed its takion before the stream began.
The phase then waits about twenty seconds and gives up without logging a word.

THE PONGS ARE WORTH KEEPING SEPARATELY. Their bytes are in this capture, tag and unit
index intact - the first recording this tree has of the console echoing, which PP813 had
to infer from the C completing.

### §PP831 The same inversion, twice

THIS EXACT MISTAKE HAS NOW BEEN MADE TWICE. PP778 found every stream message going out
with the data type in the chunk flags and the channel left at zero, and fixed it. PP813
found senkusha doing the same thing, six tasks later, shipped green the whole time.
Nothing connects the two, and nothing would catch a third.

WHAT THE BUILDERS CANNOT SEE is the shape of it. SenkushaMessages and StreamMessages
both carry the right number - one for a version request, eight for an echo command - and
both are asserted against the C field for field. The number is correct either way; only
the ARGUMENT it is spent in differs, decided one layer down.

THE C IS THE ORACLE AND IT IS ALREADY READ. MessageTapSource holds senkusha_send_data's
call text verbatim - chunk_flags first, the varying value second. So the C's argument
order is pinned here; what is missing is the join from that order to what the managed
caller does with it.

THE CONSOLE IS NOT A CHECK. Takion acks a data message by sequence number and reads no
channel, so a command on the wrong one is acknowledged and then ignored. Both faults
presented as silence: the port asked, the console agreed, nothing happened. A live run
found each, at the cost of a whole task.

WHAT WOULD SETTLE IT is a check over every managed call into a takion send, naming the C
call site it ports and comparing the two mappings position by position.

### §PP832 The keyframe nobody counts

A USER REPORTED ARTEFACTS AT THE BOTTOM OF THE SCREEN and this tree could not answer the
first question about them. Corruption that persists across frames means a reference
frame was lost and never replaced; the remedy is an IDR request, and whether one went
out is the difference between a decoder fault and a request the port never made.

THE REPORT ALREADY CARRIES ITS NEIGHBOURS. A stream run prints what was decoded, what
was shown, what the network lost, what the drain superseded and what the decoder is
answerable for - five numbers, all about frames arriving. Nothing counts what the
session ASKED FOR. So an investigation reaches for the one figure that is missing and
falls back on reading the picture, which is what a report exists to avoid.

CORRUPTION IS THE CASE THE COUNTS ALREADY DISAGREE ABOUT. The run that prompted this
showed 4539 decoded against 4314 shown with 126 lost, which is a healthy stream by every
line printed and a damaged one on the screen. A count of requests sent would have said
in one number whether the port noticed.

THE SEND MAY NOT EXIST YET, which is the half to read before filing a fix. Whether the
port ever emits a corrupt-frame report or an IDR request is a separate question from
whether the report counts one, and the answer decides whether this line is a counter or
a send. Reading it is the work; the counter is what the reading is for.

### §PP833 The queue with no floor

PP814 MEASURED IT AND THE NUMBER IS NOT ZERO. A twenty-five second run against PS5-385
submitted 2361 frames, decoded 2361, concealed none, refused none - and the engine ran
dry seven times, touching a queue depth of one. Roughly one gap every three seconds, in
a stream every other counter calls healthy.

WHAT FEEDS THE ENGINE IS THE DECODER'S OWN THREAD. XAudio2Sink hands each frame over the
moment Opus produces it. There is no floor: if a datagram arrives late, or the decode
thread is preempted, the queue empties and the voice plays silence until the next frame
lands. Nothing holds a reserve, because PP816 deliberately deleted the ring.

THAT DELETION WAS RIGHT AND IS NOT WHAT THIS UNDOES. AudioRing was a second queue in
front of the engine's, drained by a pump doing room arithmetic - PP810 is one line of
that arithmetic dropping the tail of a unit. The engine already has a queue and already
reports its depth. What is missing is a cushion, not another buffer.

THE OBVIOUS SHAPE IS A PREFILL: hold the first few frames and submit them together, so
the engine begins with depth to spend and a late frame costs margin rather than silence.
The cost is latency, exactly as many frames as are held, and this is a remote play
client - so the number is a trade to be measured rather than chosen. PP814's counter is
what measures it, on the same run and the same console.

## Block G — Test discipline

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

### §PP822 The instrument PP53's criterion describes

PP53 asks whether a frame arrives unpaced against a fixed refresh, and its own criterion
refuses the easy answer: a frame-time trace on a varying panel, not a flag DXGI
accepted. PP163 is this tree's record of what an API's return value is worth as a
prediction about a pixel.

IDXGISwapChain::GetFrameStatistics is the instrument that criterion describes. It
reports PresentCount, PresentRefreshCount, SyncRefreshCount and SyncQPCTime - the
composition's own view of when a present actually reached a vblank, on the same clock
QueryPerformanceCounter reads. That is a measurement of what happened rather than of
what was asked for, which is the distinction PP53 is built on.

WHY THIS IS AN IDEA AND NOT A TASK. Nothing in the port presents through a swapchain of
its own yet. PP49 and PP53 both name that as their blocker in the same words - the video
plane owning its own swapchain - and frame statistics come off a swapchain. So this
waits on what they wait on, and is written down now so the instrument is not invented a
third time when the plane exists.

It also needs no variable-refresh display, which PP53 does. On a fixed panel it still
says whether a present met its vblank or missed one, which is the half of PP53's
question that hardware is not gating.

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

## Block J — Public documentation
