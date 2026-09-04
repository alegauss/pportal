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

### §PP694 The encoder that was waiting for an input

PP32 measured that libopus has two consumers and not one. `opusdecoder.c` is the
playback path and `opusencoder.c` is the microphone's, so porting the decoder alone
removes no dependency: the library stays for the encoder and the DLL stays in the
package.

Its own sentence named the blocker. "The encoder is on the path that has no input... the
dependency leaves when the microphone question is answered, and not before."

PP652 answered it. `WasapiCapture` opens the default communications endpoint and
delivers whole 960-byte units at a hundred a second, in exactly the format
`streamconnection.c` announces - one channel, 16-bit, 48000 Hz, 480 frames. Windows does
every conversion, so what arrives needs nothing before an encoder.

PP651 measured the other half already: managed Opus costs 1.58 times the native median
at 24.9 microseconds a frame, which is a quarter of one percent of a ten-millisecond
frame. Cost decides nothing here, and the dependency is the whole argument.

So the work is a managed encoder taking those units and producing Opus frames, held
against `opusencoder.c` through the shim the way every other port in this tree is. What
it unblocks is the pair: with both consumers managed, libopus leaves the build and the
package, which is the saving PP32 found was not available for the decoder alone.

`audiosender.c` is not a third consumer - it names a parameter `opus_sender` and calls
nothing in the library.

### §PP696 The one commit that edits the C

PP623 gave PP33's deletion three steps and PP634 said the plan is reusable. PP630 to
PP632 ran it once for the holepunch seam, and the middle step - the only one that edits
`lib/` - was one commit that touched no test file, because every assertion it moved had
already been taught where it would land.

The frame path owes the same three and the first is done: PP670 made the oracles
two-shape. PP671 rides ON this commit rather than before it, by its own design's
reasoning.

This is the middle step. `FramePathConsumers` already reads what it must answer for,
which is why no number here is typed: session.c's calls into the stream connection, the
shim's wrappers over the frame path, and the C test files `test/CMakeLists.txt` lists.
Each is read from the tree and each has a counterpart PP669 verified by reflection.

What lands in one transaction: session.c stops asking, the shim's wrappers go behind the
option PP663 put the holepunch ones behind, the suite's four files leave its list with
the floor moving to match, and the four library files leave the build.

What does NOT land in it is any test file. That is the discipline of PP623's shape - a
commit editing `lib/` and a test in the same breath cannot tell a mistake in the C from
a model converted wrongly, and there is no green tree between them to ask.

PP295's fourth criterion is what this closes, and PP27's waits on that.

### §PP697 The prose that outlives the C it describes

PP623's third step, for the frame path.

PP634 corrected what that step is. It had said "the models drop the first of their two
states", written before either of the earlier steps had landed, and the landing showed
it wrong. The predicates ARE the guard: each is a different shape the C could come back
in, and PP630's counterpart catches only the wholesale return, which is a tripwire's
granularity rather than a guard's.

So the predicates stay. What goes stale is the present tense around them - a docstring
saying `streamconnection.c:1309 hands packets to chiaki_video_receiver_av_packet` reads
as a fact about the tree, and after the flip it is a fact about the tree's history.

The work is to turn that prose over rather than delete it, the way PP591 turned the
harness's assertions and PP652 turned the microphone census. A sentence that says what
WAS is worth as much as one saying what is, and worth nothing at all if a reader cannot
tell which it is.

What makes this its own line rather than part of the flip is PP623's own discipline: the
flip edits `lib/` and no test file, so every prose change waits for a green tree after
it. Doing both at once is the thing that plan exists to prevent.

### §PP698 The other half of what an echo canceller hears

Acoustic echo cancellation works by subtracting what the speakers are playing from what
the microphone hears. So it needs both, and `spike/audio-effects` measured the shape
that requirement takes: in filter mode the Voice Capture DSP declares **two inputs and
one output**, the host feeding the microphone and a reference of the render stream.

PP652 built the first. `WasapiCapture` opens a capture endpoint and delivers whole units
of the format the console was told about. There is no equivalent for the render side.

WASAPI has one: a loopback client, opened on a RENDER endpoint with
`AUDCLNT_STREAMFLAGS_LOOPBACK`, which reads back what is being played rather than what a
device hears. It is the same `IAudioClient` and the same capture client below it, so the
interop is largely the one that exists.

Two things are not, and are why this is its own line. A loopback client on a silent
render endpoint produces NOTHING rather than silence - a documented behaviour, and the
shape PP695 taught this port to notice. And the reference must be time-aligned with the
microphone for the subtraction to mean anything, which is a buffering question the
capture path has never had to answer.

Source mode is the alternative: the DSP opens both devices itself and hands back one
cleaned stream. That replaces `WasapiCapture` rather than following it, and takes the
device choice with it - which PP695 showed this port needs to keep.

## Block G — Test discipline

### §PP683 The census stops at the test project

PP665 made the gate say which oracles a build had and how many comparisons their absence
cost, counted from the files that guard rather than declared beside them.
OracleGuardCensus.Files names eleven, all under tests/ChiakiNg.Tests, and its own note
says the list is test files only because a class defining a guard is not a use of one.

The host's selftest guards too. It asks whether the device id's format oracle is there
before comparing the managed id against the C's, which is the same shape as the eleven
and is counted by nothing - so the number the gate prints understates what a bare build
actually skipped. PP681 is why that matters rather than being tidy: the one guarded
comparison outside the census is exactly the one whose guard was wrong, and a census
reaching it would have had a row to be surprised by.

WHAT IS OWED IS A ROW, not a second census. app/SelfTest.cs joins Files with the guard
it uses, and GuardsIn counts it there as it does in a test file. The care needed is that
not every branch in the selftest is an oracle guard - PP74's shape means some are about
a machine rather than a build - so the row names the guard by its call, as the eleven
do.

Whether any OTHER guarded comparison sits outside both is a sweep, and not this line.

### §PP691 A check that matches wording rather than meaning

Two readers went red inside PP666, for the same reason and neither about a defect.

`CriterionBlockers.ShippedIn` matched `- ✅ **PPn` and stopped at the digits, so `ship
--part`'s entry - `**PP295 (the run's ordering)**` - counted as a whole ship. An open
line then read as finished, and a criterion correctly waiting on it was reported as
waiting on something already delivered.

`TransportOrder.TheEndStateIsStillTheEndState` held PP27's fourth criterion against the
literal string "the end state and not a progress bar". Rewording that criterion to name
PP295 outright - a STRONGER statement of the very premise the check exists to hold -
failed it.

Both were written in the same sitting as the text they read, which is how the wording
got into the assertion. Neither test could fail for the reason its own docstring gives.

This is PP666's shape one field over: PP666 was a table nobody drove, and this is a
sentence matched rather than understood. The difference is that a table has one consumer
and prose has many spellings, so the fix is not a driver but a rule about what a check
may match on - a phrase that survives rewording, or a derivation, never a sentence.

What is not known is how many there are. Two were found by tripping over them; nothing
has looked. The work is to count the string constants in app/Session that hold roadmap
prose, decide which are load-bearing, and say for each whether a better sentence would
break it.

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

### §PP52 The vendor answer that is not on the machine

This line was written about a dependency that has since left. PP32 established that
speexdsp was the Qt client's - lib references it nowhere - so the half that was "delete
a dependency the port has no answer for" is done, and removing the client did it.

What was left was an addition: NVIDIA's audio effects SDK, doing noise removal and echo
cancellation on the GPU. PP647's contract binds a vendor path to an absence a user
cannot see, and PP648 found that a call which succeeds is not a feature that ran. Both
bind a path that exists.

`spike/audio-effects` asked the prior question. On this machine - an RTX 4060, current
driver, the NVIDIA App installed - the SDK is not reachable. `NVAFX_SDK_DIR` is unset,
NVIDIA Broadcast is absent, and a sweep of both NVIDIA trees finds no audio-effects
runtime. It is not a driver feature but a redistributable this port would ship, models
included.

Windows carries a Voice Capture DSP in the box. `CLSID_CWMAudioAEC` is registered in
both hives with `mfwmaaec.dll` present, and it has done echo cancellation and noise
suppression for communications audio since Vista. It ships nothing.

Two non-goals bound this and forbid neither: "No GPU vendor feature for the network
path" is about a different path, and "No vendor path whose absence is visible to the
user" allows one with a quiet fallback. The reading makes the question moot rather than
forbidden - the fallback is better placed than what it would fall back from.

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
