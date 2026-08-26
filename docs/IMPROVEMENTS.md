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

### §PP11 What the window owns

Beyond presenting frames, the Qt window decides how the session meets the display. Two
of the three are answered: fullscreen shipped as a state machine, and PP321 took the
refresh rate, which this window reads as an input on every tick rather than sets on the
panel.

What is left is HDR. PP163 measured that WPF's D3DImage refuses ten bits, and PP319
chose between the three paths that left: the overlay goes above the video in the
compositor's own tree, because a child HWND costs PP10's overlay outright and SDR on
purpose costs the picture. A container visual carrying a ten-bit swapchain below and an
eight-bit premultiplied surface above it commits, which is what makes that choice a
measurement rather than a preference.

So this half is not blocked on a decision any more. It waits on PP322 - the pixel nobody
has looked at yet, which is the exact mistake PP163 made one layer down - and then on
PP10's screen being rebuilt against a compositor rather than against XAML.

It stays a separate line from PP9 for the reason it always was: this is Win32 and DXGI
work that does not depend on which of the renderer shapes wins, only on there being a
window.

### §PP322 The reading the two-layer choice still owes

PP319 measured that a container visual takes a ten-bit swapchain below and an eight-bit
premultiplied surface above it, ordered by reference rather than by call order, all the
way to Commit. That is the same depth PP281 to PP283 reached one layer down, and PP284
then read a pixel none of them had predicted.

So the shape of the risk is known exactly: a compositor accepting a tree says nothing
about what lands on the glass. What is unread is whether the overlay visual draws OVER
the video plane rather than under it or not at all, and whether an eight-bit
premultiplied surface composes over a ten-bit plane without the alpha being taken twice.
The second is the one with no error path anywhere: it looks like a slightly wrong
colour.

The apparatus is built. `--dcomp-demo --layers` puts both planes on a real WPF window -
the video filled red, a green overlay offset in from the corner so the plane surrounds
it, its right half at half alpha - and DcompDemo writes down what each possible reading
decides. It shares the builder the assertion calls, so what is looked at is what was
measured.

What is left is the looking, and that is why this stays open. A composed window does not
screenshot reliably, so a session can run this and cannot read it. If the reading
refuses it, PP319's choice falls to SDR on purpose.

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

### §PP23 The oracle this block cannot be written without

chiaki exists because the PlayStation remote play protocol was reverse engineered. There
is no document to implement against: the 24639 lines of C in lib/src are the
specification, and a managed rewrite that reads them and reproduces them is a
translation whose only correctness test is behavioural.

That is true of the protocol as a whole and NOT true everywhere. There are 6544 lines of
C in test - munit cases over gkcrypt, rpcrypt, takion, bitstream, the reorder queue and
the decoder - plus 3081 lines of recorded FEC cases in fec_test_cases.inl. Where those
exist, the expected output is already agreed with real hardware and the rewrite is
checked against a fixture rather than against a running console.

Where they do not exist is the whole of what is left. Counted: every module this port
has ported has a test/ counterpart - fec, frameprocessor, videoreceiver, bitstream,
reorderqueue, rpcrypt, gkcrypt, takion, regist. Four have none at all: session.c,
ctrl.c, streamconnection.c and senkusha.c. Those are PP28's three files and the one
beneath them, which is to say the entire remaining translation.

So the port has advanced exactly as far as the oracle reaches, and that is not a
coincidence. What this task adds is the rest: a captured exchange replayed against both
implementations, because a state machine cannot be compared by running it twice the way
a buffer function can.

### §PP27 The transport, and the only place GC is a real question

takion.c is 1868 lines plus takionsendbuffer.c at 267 and reorderqueue.c at 200: the
sequencing, the retransmission, the send window and the reordering that a video stream
over UDP needs.

This is the one task in the block where the runtime is a genuine risk rather than a
prejudice. A pause at the wrong moment is a dropped frame, and the traffic is thousands
of small packets a second, each of which is an allocation if written carelessly. .NET
has the answer - Span, ArrayPool, Socket with SocketAsyncEventArgs - but the answer has
to be chosen deliberately, which is what makes this different from the tasks above it.

The measurement is not opinion either: the C implementation is right there, and the
oracle can run both against the same captured traffic and compare timing, not just
bytes.

### §PP28 The state machines

session.c is 1219 lines, ctrl.c 1534 and streamconnection.c 1326. Together they are the
connection: what is sent in which order, what is waited for, what a timeout means at
each point, and how a session comes apart when the console stops answering.

There is no diagram and the code is the diagram. Translating it means reading control
flow that was written to match observed behaviour, not designed - and the honest
expectation is that some of it looks wrong and is not.

Two consequences for how this is taken. It should be split when it is started rather
than now, along the three files, because a single review of 3977 translated lines is not
a review. And it is the task that most benefits from the oracle running a full captured
session end to end, since almost nothing here has a fixed input and a fixed output the
way the crypto does.

### §PP29 The first thing that can be proved against a console

regist.c is 910 lines, discovery.c 492 and discoveryservice.c 384: the broadcast that
finds a console, the reply that describes it, the wake packet, and the PIN exchange that
ends with key material stored.

Unlike the transport, these are request and response over well-defined boundaries, and
unlike the state machines they are short. That combination makes this the slice to run
against real hardware first: it needs the crypto to be right, it needs nothing from the
video path, and it either finds the console on the network or it does not.

It is also what makes the rest of the block testable at all, since a session cannot be
opened against a console the managed side has never registered with.

### §PP30 Reed-Solomon, by hand

third-party/jerasure and third-party/gf-complete implement erasure coding over GF(2^8),
and frameprocessor.c is what calls them: when packets of a video frame are missing, the
FEC blocks are what reconstruct them instead of asking for a retransmission that would
arrive too late to matter.

The surface to port is the call sites rather than the vendored source, so that is what
is declared here and `remaining PP30` counts it - 14 on 2026-08-16, across common.c,
fec.c and frameprocessor.c.

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

### §PP31 The line managed code should not cross

ffmpegdecoder.c is 376 lines and bitstream.c 450, and behind them is FFmpeg doing
hardware accelerated H.264 and HEVC decode. Nothing in .NET replaces that. A pure
managed decoder is possible in the sense that it can be written and impossible in the
sense that it would not hold the frame rate, and it would ignore the GPU that is already
doing this work for free.

So this task is a decision, not a translation, and the honest options are all native:

Keep FFmpeg, called through P/Invoke. Least change, keeps every format and every
hardware path the project already supports, and keeps a large native dependency in a
solution that was trying to shed them.

Use Media Foundation with D3D11VA. Native to Windows, ships with the OS, integrates with
the D3D11 texture path the renderer decision may already need - and supports fewer edge
cases than FFmpeg does.

Either way the claim to correct is the framing: the goal that is reachable is a project
that is 100% Windows and builds in Visual Studio, not one that is 100% managed. The
decoder is the counter-example, and it is better stated here than found at the end.

### §PP32 Audio, where one half has a managed answer

audioreceiver.c is 363 lines over Opus, and speexdsp does the resampling that keeps
playback aligned with a stream that does not share the sound card's clock.

Opus has a managed implementation and a native one, and the decision between them is
measurable rather than theoretical: decode cost per packet against the extra dependency.
speexdsp has no managed counterpart worth the name, and the alternatives are a native
call or writing the resampler - which is a smaller job than it sounds and a worse one to
get subtly wrong, since drift is heard as a click every few minutes rather than as a
failure.

Output is the easy half: WASAPI through NAudio or the platform APIs is what the .NET
host would use regardless of this block.

### §PP33 Two dependencies that simply leave

holepunch.c is 5786 lines and is the only translation unit in this tree that still needs
either library: 234 curl_easy calls, 4 curl_ws, and the json_object and json_tokener
sites beside them. http.c is not among them. It is 262 lines over rudp and winsock and
carries no curl symbol at all, which is not what the first version of this section said
it was built around.

```roadkeep-remaining
lib/src/**/*.c :: curl_easy_|json_object|json_tokener
```

Read that count carefully: it reports 420 sites in "46 files", and 46 is every .c under
lib/src - the glob's reach, not the hits. Every one of the 420 is in holepunch.c.

What the count does not say is who calls it. session.c has NINE call sites over seven
functions - the ctrl and data sockets, the offer, the punch, the regist info, the
selected address, the ctrl port and the fini - the shim one and qmlbackend.cpp three. An
earlier version of this counted a holepunch-test.c that is not in the tree.

The behaviours are largely ported. PP231 stated the websocket auto-ACK, PP266 performs
the five session calls over a real HttpClient, and the shim exposes json-c's accessors
deliberately: an oracle the managed parser is held against (PP215), not a dependency
waiting to go.

So what remains is not translation. It is session.c no longer asking, and that is a task
nothing here names yet.

### §PP107 The two nobody called

chiaki_reorder_queue_drop and chiaki_reorder_queue_peek are the two functions of this
module the C suite never calls, and both are broken.

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

### §PP294 The control channel, on its own

The second of PP28's three. ctrl.c is the longest at 1534 lines and carries the most
message types - the control connection a session opens alongside the stream, over which
the console reports state changes, accepts requests and answers keepalives.

None of it is on the frame path, which is the useful thing about it. PP27 is judged on
latency because every millisecond takion adds is one the picture is late by; this is
judged on whether the right message was sent in the right state, and a millisecond here
costs nothing. So the measurement that matters is a recorded exchange compared message
for message, not a timing histogram.

The message types are the work rather than the line count. A control channel is a switch
over a wire format, and the risk is a type handled in the wrong state rather than an
algorithm translated wrongly - which means the oracle has to drive states as well as
messages, and a table of message-in, message-out pairs would pass while missing the
ordering entirely.

It is also the file most likely to hold behaviour nobody has exercised. A control
message that arrives once in a thousand sessions is one nobody has watched, and the C is
the only record of what it does.

### §PP295 The file every deletion is waiting on

The third of PP28's three, and the one that decides when C starts leaving this build.

PP286 through PP291 ported the frame path from the bottom up: the Galois field, the
Cauchy matrix, the Reed-Solomon codec, the frame processor, the video receiver. None of
it removed a single line of C, and the reason is one call. streamconnection.c:1262 hands
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

### §PP313 The second door out of PP33

PP33's first three criteria are met. Every curl and json-c area in holepunch.c has a
named counterpart in app/Protocol, the websocket thread's auto-ACK is stated, and PP266
made the five session calls real HttpClient transfers rather than descriptions.

The fourth cannot be met the same way, and measuring it says why: holepunch.c is the
only translation unit in lib/src that names either library, and session.c is its only
caller. So the libraries leave when holepunch.c leaves the build, and holepunch.c leaves
when the managed session takes over - PP293, behind PP297's capture, behind a console.
That is now a dep rather than a surprise.

There is a second door and it is worth stating before somebody finds it in a hurry. The
remote path is already a tri_option; built OFF, libchiaki configures and links with
neither library, and neither is fetched, built or shipped. The criterion's own words -
"until then both are still fetched, still built, and still shipped beside a managed
replacement that duplicates them" - would be satisfied.

What that costs is a feature, and the honest question is whether it costs anything
TODAY. The Qt client is off by default, the managed host does not stream yet, and
nothing here reaches a console over the internet. So probably not - and "probably" is
why this is an idea rather than a task. Turning a capability off to make a count reach
zero should be decided rather than discovered.

### §PP331 A message the library will not name

The PP297 capture is the first time anything in this tree watched a real control
channel, and it found a message the enum has no name for. Type 0x41, payload
00-00-00-00-02-01-00-00, sent by the console shortly after DISPLAYB on a session that
connected and stayed up. Not an error path and not a corner case: it arrived on the
first capture ever taken.

What ctrl.c does with it is the part worth filing. The default branch of the type switch
hexdumps the payload at WARNING level, and the line above it - the one naming which type
arrived - is commented out, and has been since the fork. The log shows eight anonymous
bytes at a level meaning something is wrong, with nothing saying what was received or
that anything is unhandled.

Two things follow and only one is this task. Naming the type is research, and the
capture is now how to do it: take one with the console doing different things and see
when 0x41 changes. Reporting it is not research. A message the library cannot name
should say so, with its number, at a level meaning unhandled rather than broken.

The port has a reason beyond tidiness. PP294 rewrites ctrl.c against this recording, and
a type the C silently drops is one the rewrite will silently drop differently - the
replay agrees, both are wrong, and the disagreement surfaces as a stream behaving oddly
much later. A named unknown is something a replay can assert about.

### §PP340 What has to be true before the file can go

PP33's end state is that chiaki-lib stops compiling holepunch.c and the two link lines
go. Its own criteria say so. What it does not say is what has to be true first, and
reading the callers is how that became visible: session.c drives the whole PSN path from
C, across nine call sites.

They are not incidental. The ctrl socket and the data socket, the offer, the hole punch,
the registration info, the selected address, the ctrl port, and the fini - each is a
step of the connect sequence for a session played over the internet rather than the
local network. Delete the file today and the build breaks; make the build pass by
deleting the callers too, and remote play over PSN goes with them.

That is why this is a line of its own rather than a bullet under PP33. The deletion is a
consequence; the work is the managed side owning the flow - a seam the session thread
asks for a socket and an address through, with the C behind it until managed code is.
PP266 already performs the five session HTTP calls over HttpClient, so the pattern
exists; what has no counterpart is the websocket, the STUN exchange and the punch
itself.

Until then PP33 is correctly blocked and its remaining query correctly reads 420.
Reading that number as the size of the job is what its own section warns against: it is
one file, and the work is at the other end.

### §PP345 A failure that arrives as an accusation

Three functions carry a login PIN from the person who typed it to the console, and only
two of them can report a failure.

chiaki_session_set_login_pin returns ChiakiErrorCode and answers CHIAKI_ERR_MEMORY where
its malloc fails. The session thread then forwards the PIN with
chiaki_ctrl_set_login_pin, which returns void - and its first statement is a malloc that
returns early on failure, before login_pin_entered is set and before the notify pipe is
poked. Nothing anywhere learns that the PIN was dropped.

What the user sees is the interesting part, and PP335 is why. The ctrl thread never
sends the PIN, so the console never accepts it and asks again; the session thread's loop
treats a second request as the refusal it always treats one as, and the next prompt says
the last PIN was wrong. It was not wrong. There was no memory.

The failure is rare and the report is the point. A person told their PIN was wrong types
it again, carefully, and is told the same thing - and the log says nothing about it
either, because the early return does not log.

The fix is a return type and a caller that reads it. What the session thread should do
with a failure is a real question and not a small one: the PIN has already been consumed
and freed on its side by then, so there is nothing left to retry with, and ending the
session with a reason naming memory is more honest than a third prompt.

### §PP354 Two buffers, one fill

ChiakiCtrl has two receive buffers and one size field:

    uint8_t recv_buf[512];
    uint8_t rudp_recv_buf[520];
    size_t recv_buf_size;

recv_buf_size tracks recv_buf, which is what the framing loop consumes from.
rudp_recv_buf has no size of its own, and the one place its capacity is used mixes the
two:

    chiaki_rudp_recv_only(rudp, sizeof(ctrl->rudp_recv_buf) - ctrl->recv_buf_size, &message);

That subtracts one buffer's fill level from the other buffer's capacity. It is not a
crash - the limit comes out smaller than rudp_recv_buf whenever recv_buf holds anything,
so the receive is conservative rather than over-long - but it is not the number anybody
meant. What it says is "how much room is left in rudp_recv_buf" and what it computes is
"520 minus how full a different buffer is".

PP347 bounded the copies OUT of the rudp path into recv_buf, which is where the overflow
was. This is the other end and a different question: whether the eight extra bytes are
deliberate, whether rudp_recv_buf needs a fill of its own, and whether one of the two
buffers exists only because the other could not be reused.

It is a design question rather than a defect, which is why PP347 named it and did not
answer it. Answering it wrongly is how a second buffer becomes a second thing that can
be out of step with the first.

### §PP355 One of two owned things freed at teardown

ctrl_message_queue_free exists and has exactly one caller: the drain inside the loop's
cancelled branch. chiaki_ctrl_fini frees ctrl->login_pin and never touches
ctrl->msg_queue.

That is fine on the path everybody takes. A stop pokes the notify pipe, the loop wakes,
and its order is queue-then-PIN-then-stop - so the drain empties the queue before the
stop is read, and fini finds nothing left. The queue is empty at teardown because the
loop drained it, not because anything at teardown looks.

Every other exit from the loop skips the drain. The overflow branch breaks. So do a
failed select, a recv error, a failed rudp receive, a short rudp message and a rudp
finish message. Anything queued when one of those happens is a linked list of malloc'd
nodes, each with a malloc'd payload, that nothing frees.

It is small - a handful of allocations, once per session, bounded by what a screen had
queued - and it is reachable by queueing anything at the moment the socket errors.
goto-bed from the power menu during a network drop is the shape of it.

The asymmetry is the interesting part. fini DOES free login_pin, the other thing an
outside caller allocates into ctrl and hands over. So ownership at teardown was thought
about and one of the two was missed - which is why this is a line rather than a note.
The fix is a loop in fini calling the free that already exists.

### §PP357 A bound that is not in the binary

Both keyboard receive handlers check that the header arrived and then trust an assert
for everything after it:

    if(payload_size < sizeof(CtrlKeyboardOpenMessage))
        return;
    msg->text_length = ntohl(msg->text_length);
    assert(payload_size == sizeof(CtrlKeyboardOpenMessage) + msg->text_length);
    buffer = malloc((size_t)msg->text_length + 1);
    memcpy(buffer, payload + sizeof(CtrlKeyboardOpenMessage), msg->text_length);

The guard covers 32 bytes of header. The relationship between what arrived and what the
header CLAIMS arrived is covered by the assert, and this project builds Release with
-DNDEBUG - so in the binary it ships, that line is nothing. The keyboard text-change
handler is the same shape against its own 44-byte header.

A message announcing a text length larger than it carried therefore mallocs that length
and memcpys it out of a 512-byte buffer. A modest lie - a thousand bytes claimed, forty
arrived - reads half a kilobyte past the end and hands it to a screen as the text the
user is editing. A large one asks for four gigabytes, and where the allocation succeeds
reads that far.

The length is not authenticated in any useful sense: it is inside the encrypted payload,
so it is whatever decrypted, and a decrypt that produced garbage produces a garbage
length rather than an error.

The fix is the check the assert was standing in for, which every other handler in the
file writes out. Whether asserts should be relied on anywhere in a library built with
NDEBUG is the larger question this is one instance of.

### §PP358 The parser PP296 did not reach

This tree has two HTTP response parsers for two handshakes. PP296 changed one of them
and left the other, and the argument PP296 made applies to both word for word.

parse_session_response matches RP-Nonce, RP-Version and RP-Application-Reason with
strcasecmp, because an HTTP field name is case-insensitive and a console spelling one
otherwise was the defect PP296 was filed for. parse_ctrl_response, thirty lines further
down the same kind of function, matches RP-Server-Type and RP-Prohibit with strcmp.

So a console answering the ctrl request with "rp-server-type" is a console whose server
type this port does not read. What follows from that is not an error: server_type_valid
stays false, the branch logs "No valid Server Type in ctrl response", and the connect
carries on - without the two downgrades that branch performs. A regular PS4 asked for
1080p would be asked for 1080p, and a PS4 asked for H265 would be asked for H265, both
of which it does not support.

The failure is therefore a stream that does not start, or starts wrong, on a console
that answered correctly in a spelling nobody thought to allow. And it is invisible from
this side: the log line says the header was not valid, which reads as the console not
having sent one.

Two parsers, one rule, one of them fixed. The other is this.

### §PP359 A third writer to a two-flag machine

PP353 ported the display state as a table over two flags, because neither means anything
alone and only DISPLAYB tells the client the stream cannot be shown. There is a third
caller of that same callback and it is in the connect:

    if(response.rp_prohibit)
        ctrl->session->display_sink.cantdisplay_cb(ctrl->session->display_sink.user, true);

It touches neither flag. So a prohibited session starts with the client hiding the
stream while cant_displaya and cant_displayb both read false - a state PP353's table has
no name for, because the table is what the client believes and this is the one path that
sets that belief from outside.

What follows is worse than an inconsistency. The only thing that ever tells the client
the stream is back is a DISPLAYA carrying 0x0, guarded on the second flag being down -
and it is down. So the first unrelated DISPLAYA 0x0 the console sends un-hides a stream
the console said was prohibited, and nothing in the machine remembers that it was.

RP-Prohibit is also read with atoi, so any value that is not the text "1" means not
prohibited - including a value that failed to decrypt, and including the empty string.

Which of the two this should be is a real question: a third flag that the DISPLAYA
branch also guards on, or a prohibition that is not expressed through the display
machine at all. The port reproduces the C for now, and cannot reproduce it faithfully
without saying which.

### §PP360 The response side, and the other counter

What remains of ctrl.c after the connect: the response side of the handshake, the login
state switch, the three keyboard messages the console sends, and the three small
senders.

THE CTRL REQUEST IS RETRIED EXACTLY ONCE, on timeout, and on the TCP path the socket is
torn down and reconnected before the second attempt. A one-shot flag, like PP334's
ladder is a count and not a loop - and for the same reason.

THE REMOTE COUNTER IS ALSO PRE-SPENT, which is PP356's finding from the other side.
Where the response carried a well-formed RP-Server-Type it is decrypted at
crypt_counter_remote++, so the first RECEIVED ctrl message decrypts at one. Where the
header was absent or the wrong length it decrypts at zero. The starting point is
therefore conditional on what the console sent, which is the same trap as the local
counter with an extra branch in it.

The server type drives two downgrades: a regular PS4 asked for 1080p is dropped to 720p
keeping its frame rate, and a PS4 or PS4 Pro asked for anything but H264 is forced to
H264. Both only where the header was valid, which is what PP358 is about.

The three senders are fixed payloads with one variable bit: the microphone toggle's
third byte, where zero is muted and one is not - and the corpus confirms the layout,
00-01-01-59, twice.

### §PP361 A log that lies and a switch that admits it

Two small things found while reading the last of ctrl.c, kept together because each is
one line and neither is worth a task of its own.

THE MICROPHONE TOGGLE'S LOG IS INVERTED:

    CHIAKI_LOGV(log, "Ctrl sending toggle microphone mute message: %s", muted ? "unmute": "mute");
    uint8_t toggle[0x4] = {0, 1, 1, 89};
    if(muted)
        toggle[2] = 0;

muted true writes zero into the third byte and logs "unmute". The wire is right and the
sentence is backwards, so a verbose log read while chasing a microphone problem says the
opposite of what was sent. The corpus confirms which way the byte goes:
ctrl_enable_features calls this twice with false and the recording holds 00-01-01-59
twice.

THE SUBTYPE SWITCH SAYS SO ITSELF. The rudp arm of the read loop switches on
message.subtype with the comment "wrong but works", and the arms fall through
deliberately - 0x12, 0x26 and 0x36 all land in 0x02 after acking. It is upstream's own
admission that the dispatch is not the shape the protocol has, and it is the one place
in the file where a port cannot claim to be reproducing intent, only behaviour.

Neither changes what goes on the wire. Both are the kind of thing a reader trusts and
should not: one lies in the log, the other says out loud that it is wrong.

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

RTX Video HDR is the driver-side answer and it runs on the same NGX surface as the
upscaler, which is why it is filed beside it and after the window owns its own
swapchain. The two together are the whole of what the vendor offers for the picture.

It comes with a caution worth stating rather than discovering: an inferred HDR image is
an opinion about colour the source did not express, and on some content it looks worse.
Whatever ships has to be a setting the user can turn off, and the fidelity mode the
conformance work already cares about has to bypass it entirely.

### §PP50 The one that trades the wrong currency

NVIDIA's optical flow accelerator can interpolate between two decoded frames, and the
driver exposes a smoothing feature built on it. Applied to a 30fps stream it produces
60, and it does so by holding a frame back until its successor arrives.

That is the trade laid bare: smoothness is bought with latency, in a client whose entire
quality argument is latency. It is not obviously wrong - a 30fps title that streams
smoothly may feel better to some users than a stutter-free 30 - but it is obviously not
a default, and the only way to decide is the glass-to-glass number this depends on.

Filed so the idea has an address, and filed with the cost in its own symptom so nobody
schedules it believing it is free.

### §PP52 Where a vendor feature also pays a debt

streamsession.h carries SpeexEchoState, an echo suppress level, and two conversion
buffers for the mic path. The audio task in the managed core block names speexdsp as the
piece with no managed counterpart worth the name - one of the few places that block has
to write rather than reference.

NVIDIA's audio effects SDK does noise removal and echo cancellation on the GPU, which is
the same job with better results on a machine that has the card. That makes this the
only item in this block that is not purely an addition: on an NVIDIA machine it replaces
code the port would otherwise have to carry.

It does not remove the fallback, and the fallback is where the dependency question stays
open - which is the other half of what the first-not-only task has to state.

### §PP53 The one that removes waiting instead of adding work

Nothing in the window mentions VRR, G-SYNC or adaptive sync. Frames from a console
arrive when the network delivers them - irregularly by nature - and a fixed refresh
present rounds every one of them up to the next vblank. At 60Hz that is up to 16ms of
pure waiting, added to a frame that already travelled a network.

Variable refresh is the direct answer, and it is the only item in this block that makes
the picture arrive earlier rather than look better or arrive smoother. It also composes
with everything else here instead of competing with it.

Two caveats to hold. Below the display's minimum refresh, low framerate compensation
changes the behaviour and the result has to be checked rather than assumed. And
exclusive fullscreen is usually the precondition, which is why this hangs off the task
where the window takes ownership of how it meets the display.

### §PP72 A preference its own numbers no longer support

qmlbackend sets prefer_cuda from a card detection, and two places act on it: the auto
path takes cuda when the renderer is not Vulkan, and the OpenGL fallback drops to cuda
rather than d3d11va. PP71 measured all three at the rate a console sends and cuda came
last, on median and p99. With each path's readback added, the per-frame totals are about
400us for vulkan, 2550 for d3d11va, 2900 for cuda.

So the ordering is right where it matters and wrong where it does not. Vulkan first is
right by more than the unpaced numbers suggested. The cuda-over-d3d11va preference is
the part that does not survive, and it governs one case: an OpenGL renderer, which
cannot hold a vulkan frame and pays a copy whichever of the two it takes.

What this line is not is a swap. One card, one stream, one machine, and PP71 left cuda's
tail narrowed but unproven - clocks falling with the idleness pacing creates, not
confirmed by pinning them. Changing a decoder preference on that would repeat the
mistake PP48 was filed against: choosing a vendor path from something other than
evidence.

The step that fits is to make the choice answerable rather than reverse it. The session
record already names the decoder behind each row, so a client that ran either path on
the OpenGL fallback would settle this from real sessions rather than a synthetic stream.
Whether that is worth a knob, a default change or nothing is what this line decides.

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

No new instrument is needed. The PP42 telemetry row already names the decoder that
produced it, so one session per decoder against a real console answers this, and the
work is a run rather than a build. That is why this is filed as its own line instead of
held open inside PP48: the cost question had an answer here and this one does not.
