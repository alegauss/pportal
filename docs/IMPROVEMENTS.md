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

### §PP707 The flip has nothing to flip to

PP696 says session.c stops asking, and asking is how this application streams. StreamRun
calls ChiakiSession.Start, which is chiaki_session_start, which runs the C's session
thread - and that thread calls chiaki_stream_connection_run. PP700 joined a decoder to
exactly that path and recorded a stream decoding for the first time.

ManagedStreamRun is constructed nowhere outside its own tests. Grep the assembly and the
only mentions are a docstring, a parameter's example, and two rows of PP669's census.
The same is true one layer down: PP703 records that ManagedTakion never opens a video
queue, and PP680's AV arm is built by tests alone.

SO THE FLIP LINKS AND THE APPLICATION STOPS. That is the failure worth writing down
rather than discovering in the commit: nothing in PP295's criteria is false, because
each of them is about a counterpart EXISTING, and none of them is about one being
reached. PP669 verified the mapping by reflection and a mapping is not a call.

WHAT IS OWED is the session's own seam: something that starts a stream through the
managed run rather than through chiaki_session_start, with the decoder and the pad
hanging off it as StreamRun already hangs them off the C. PP703 and PP706 are two of the
joins under it and there will be more.

Recorded as a dep of PP696 rather than folded into it, because it is a different piece
of work and PP696's own design is right about everything except what happens next.

### §PP722 The other eight events

PP719 named nine of ChiakiEventType's seventeen as the frame path's, and left the other
eight unanswered rather than absent. Seven have raisers: ctrl.c raises three keyboard
events - open, remote close, text change - and session.c four, being the login pin
request, the quit, the auto-regist and the nickname.

THE EIGHTH IS RAISED BY NOTHING. CHIAKI_EVENT_HOLEPUNCH is declared in session.h and
assigned nowhere in lib/src; the only code mentioning it is gui/src/streamsession.cpp,
which switches on an event the C cannot produce. Upstream's holepunch raised it and PP33
removed that file, so the arm answers a message that stopped existing - and the member
stays because deleting it renumbers every value after it, which NativeEnumMirrors is the
check for.

WHY THIS IS A CENSUS AND NOT A PORT. PP712's lesson is that the count is worth having
before the work: seven owed members read as four subsystems until somebody asked which.
The same question here is which of the seven are one piece - the three keyboard events
are one screen - and the port already consumes two of them off the C, the pin request
and the quit, through ConsoleSession.Translate.

Nothing here waits on PP696: ctrl.c and session.c are not the frame path and no deletion
turns on them. What waits is a managed session raising anything at all outside a stream.

### §PP724 The move nothing asks for

PP714 ported congestion control and NativeWaits went on calling congestioncontrol.c
unported through a green gate. PP718 fixed that case: an unported row whose note says a
FILE is unported, while a mirrored row already names that file, contradicts itself.

THE FIRST READING OF THIS TASK WAS WRONG, and measuring it is what showed that. It
counted one note in ten carrying the phrase and proposed asking the question of the file
instead of the sentence. That rule fires falsely: holepunch.c is in both groups today,
legitimately - much of it is ported and one wait is not - and PP718's own text says a
half-ported file is not the failure. The other nine notes claim something about a WAIT
rather than about a file, so a file rule has nothing to say about them either.

THE REAL GAP IS THE MOVE. What goes false is a row left in the unported group after its
wait gains a counterpart, and nothing looks for that. It has happened once: PP723 wrote
the feedback sender, both FEEDBACK_STATE macros gained managed constants, and the census
stayed green because that commit moved the rows by hand. A commit that had not would
have left two false rows behind a passing gate.

WHAT WOULD HOLD IT is a question about the managed side rather than the C: does a
constant answering this macro now exist? The port has no file-to-type map to ask
through, and building one for eleven rows is the cost to weigh.

### §PP734 A counterpart that is only a seam

PP713 made every row of the frame path's census say what kind of counterpart it names,
and asking that question exposed a different one it does not ask: WHETHER the thing
named is reached.

TWO ROWS NAME AN INTERFACE MEMBER AND ONLY ONE OF THEM IS FILLED.
stream_connection_send_idr_request resolves to IVideoReceiverOutbound.SendIdrRequest,
and StreamOutbound implements that interface in app - PP684 wrote it as the seam's first
implementation that is not a double. chiaki_stream_connection_stop resolves to
IStreamRunHost.ShouldStop, and the only implementations of THAT interface are doubles in
the test project.

THE CHECK CANNOT SEE THE DIFFERENCE, because both resolve and both name a member that
exists. So the census reports the same confidence about a call answered by shipping code
and a call answered by a shape.

WHICH MATTERS BECAUSE THE CENSUS IS A CRITERION. PP295's third is what the deletion of
four C files is measured against, and PP669's own rule is that a mapping is not a call.
An interface member with no implementation outside the tests is a mapping one level
further out: it is a promise that something COULD answer, held against a criterion that
reads as something DOES.

The fix is a verdict per row rather than a new rule - implemented in app, or a seam
whose filling is somebody's open work - and the reflection to decide it is three lines,
since the assembly is already loaded.

### §PP736 A count and a span in different units

chiaki_packet_stats_get subtracts a floor from a ceiling to get a span, and compares it
against a COUNT of pushes. PP715 established what the span is when it goes negative.
This is about the ordinary case.

THE TWO NUMBERS ADVANCE ON DIFFERENT THINGS. An audio AV packet carries
source_units_count units and covers frame indices from its own upward, one per unit -
but audioreceiver.c pushes exactly one number per packet, the first. So the count rises
once per packet while the ceiling rises once per UNIT, and the span is the
units-per-packet ratio times the count.

WHICH MEANS THE ANSWER TURNS ON A NUMBER NOBODY HERE HAS. At one unit per packet the two
agree and a clean window reports nothing lost. At two, a window in which every packet
arrived reports half of them missing, congestion control measures 50%, and the clamp
pins the report at the configured ceiling for the whole session - not once per wrap, but
always.

NOTHING IN THIS TREE STATES IT. The port has no recorded audio AV packet to read
source_units_count from; PP297's capture is ctrl and the session request, and the
datagram captures PP516 replays have not been asked this question.

So the work is to measure it before deciding anything: one audio packet, its unit count,
and the arithmetic above either collapses to zero or becomes the loudest finding in the
congestion path.

### §PP737 The stream that allocates per block

PP731 left the C's key buffer out on the grounds that it is a cache: the bytes are
identical and there is no thread to release. That is true about the OUTPUT and says
nothing about what producing it costs.

FOUR ALLOCATIONS, AND ONE OF THEM IS A KEY SCHEDULE. GkKeyStream.Generate calls
Aes.Create per invocation, assigns the key - which copies it and builds the schedule -
allocates the output array, and allocates a fresh sixteen-byte counter block for every
block it writes. The C does the schedule once per crypt and fills an aligned ring ahead
of the stream, so the per-packet cost there is the AES and nothing else.

PP44 SET THE BUDGET BEFORE ANY OF THIS EXISTED, and its own words are the case:
thousands of small packets a second, each an allocation if written carelessly, with Span
and ArrayPool named as the answer. The transport's criterion says the budget is met; the
decrypt path has never been measured against it, and PP731 is what made this the only
path there is.

WHAT IS OWED IS A NUMBER FIRST. Allocations per packet at a real frame rate, taken the
way PP610 and PP633 took their timings, and then the decision: a reusable AES per crypt
is nearly free to make and the counter block can be a stack span, while a prefetch ring
is the C's answer and a thread this port has so far done without.

## Block G — Test discipline

### §PP728 A criterion counting something that moved

One criterion of the run's host said "Seven have none, and they are four subsystems",
naming all four. Four tasks then wrote those subsystems over four commits, each one
shortening the census the sentence was copied from, and the sentence did not move. Every
one of those commits was green.

IT WAS FALSE BY DEGREES, which is what makes it worth a check rather than a proofread.
After the first ship it named a subsystem that existed; after the fourth it stated seven
where the answer was zero. Nothing in between reads as wrong at a glance, and the number
is exactly what somebody planning the work would take from it.

THE SHAPE IS PP690'S, ONE FIELD OVER. That check holds a criterion's BLOCKER claim
against the ledger, because a sentence naming a finished task understates the work to
zero. This is the same sentence's COUNT, and the same argument: a person deciding what
is left reads it.

AND THE JOIN IS AVAILABLE, which is the part that makes this cheap. The census is a
program value, not a document - a list in app whose length is the number the criterion
states. CountedClaim already does this for line counts in C files, matching a filename
and a number in the same sentence and recounting it. The same reading over "N have none"
against a named list would have gone red on the first of the four commits rather than on
none of them.

### §PP733 A census of the readers

PP730 measured that the managed parser accepts messages nanopb refuses and put the check
in the bang; PP732 carried it to the disconnect and the streaminfo. Three sites, found
by grepping for ParseFrom, and the list is now a thing somebody remembered rather than a
thing something reads.

WHICH IS THE SHAPE THIS PORT KEEPS PAYING FOR. PP279 found it in the root-file list,
PP718 in the wait census, PP720 in a staleness warning and PP724 in a check whose own
commit reworded two rows out of its reach. A hand-kept list guards what its author
thought of, and the fourth reader is by definition the one nobody thought of.

AND ONE OF THE THREE HAD ALREADY BEEN WRONG IN A TEST. StreamInfoMessage read an empty
buffer as "not a streaminfo", and a test asserted it - green for as long as it stood,
because a message with no type at all reads perfectly well through protoc and is refused
by nanopb. The correction came from applying the check, not from anybody noticing.

THE CENSUS IS AVAILABLE, in PP669's shape: every ParseFrom in app resolved by a sweep,
each with a verdict a person wrote - checks the required set, or says why it does not
need to. The two in SelfTest are round trips rather than decisions, and that is a
verdict too.

### §PP735 A model is not a caller

PP290 sweeps the tree for exported C functions nothing refers to, and counts a reference
as any word-boundary match outside the line that exports it. A census naming a symbol in
a string therefore looks exactly like a call.

PP716 HIT IT AND WORKED AROUND IT. Its list names five packetstats functions so a check
can read which of them take the mutex, and one of the five is on the dead list. Naming
it in a table does not give it a caller, so the sweep would have retired a row that is
still true - and the answer was to exclude that one file by name, which is a hand-kept
list guarding what its author thought of. PP279, PP718, PP720, PP724 and PP733 are the
same shape, and this is the sixth.

A RULE IS AVAILABLE AND IS NOT FREE. A model names its symbols inside string literals; a
caller writes them as code. Every P/Invoke entry point in app is either a shim wrapper
or a chiaki_render name, and neither is declared in the headers this sweep reads - so no
tracked export is reached through a literal today. But dozens of tracked names DO appear
in literals across the other censuses, which means the rule would move an unknown number
of exports onto the dead list at once.

That count is the task, and it is the interesting half: each name it adds is C nobody
has decided about, which is what PP290 exists to find.

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
