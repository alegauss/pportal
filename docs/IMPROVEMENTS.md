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

Beyond presenting frames, the Qt window decides how the session meets the display:
exclusive or borderless fullscreen, whether the swapchain is HDR and how the metadata is
passed on, and whether the refresh rate follows the stream. Qt answers some of these and
the code around it answers the rest.

None of it survives the window being replaced, and none of it is optional - a stream
that tears, or that shows an SDR picture on an HDR display, is the complaint that
reaches the issue tracker first. It is a separate line from PP9 because it is Win32 and
DXGI work that does not depend on which of the three renderer shapes wins, only on there
being a window.

### §PP163 The eight-bit ceiling under PP9

D3DImage is D3D9Ex, and PP9 chose it without establishing what it can carry. Measured
rather than read: chiaki_render_share_to_d3d9_format builds the same share in either
format, and the two halves of the answer are apart from each other.

The ten-bit surface EXISTS. D3D11 creates R10G10B10A2_UNORM, DXGI shares the handle, and
D3D9Ex opens it as D3DFMT_A2B10G10R10. That pairing is forced rather than chosen: DXGI
has no B-first ten-bit format, so there is no second spelling to have got wrong. Nothing
in the graphics stack refuses HDR.

WPF does. SetBackBuffer throws NotSupportedException - unsupported pixel format - for
that surface. The composition path carries eight bits per channel and nothing wider, and
the buffer is refused before any question of metadata or tone mapping arises.

So HDR needs a presentation path that is not D3DImage. Two shapes are worth pricing. A
DXGI swapchain in a child HWND is the one PP9 rejected, because nothing can be drawn
over an HWND - and PP10 has since built the overlay as XAML over a D3DImage, so taking
it now costs that screen. A DirectComposition visual composes a swapchain with WPF
content above it, which is the only path that leaves PP10 standing.

Not a defect in PP9. Eight-bit SDR works and that is most sessions; this is the ceiling
that decision has, now measured, with the call that stops it named.

## Block D — Screens

## Block E — Windows-only build

### §PP22 Getting it onto a machine

The AppImage, Flatpak, macOS and Switch workflows went with the platforms they built
for, and nothing replaced them. What a Windows-only build needs is narrow: compile the
native side, publish the .NET host as a single self-contained win-x64 file, and produce
an installer.

claude-tray answers each of these already - PublishSingleFile with SelfContained, an
Inno Setup script that reads its version from the built exe, a build script that sweeps
the SDK's temp projects. The parts that do not carry over are the native ones:
libchiaki, FFmpeg, SDL and whatever survives of libplacebo still have to be built or
fetched, and the current tree does that through vcpkg.

Filed early because a build that only exists on one machine is how a port acquires an
undocumented step, and late enough that there is a host worth publishing.

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

### §PP274 The installer's other half

PP273 answers what an installer ships: build\chiaki-ng-package holds the published host,
the three native libraries the resolver loads and the closure of what they import, and
package.cmd proves the set by running it under TEMP where no walk up into a checkout can
rescue a file it missed.

What still names the old world is scripts\chiaki-ng.iss. It is upstream's, and it
defines MyAppPath as ..\chiaki-ng-Win and MyAppExeName as chiaki.exe - a directory that
resolves to the repository root rather than to build\, and an executable PP21 turned off
by default. Its [Files] section then copies that directory whole, which would carry 34
Qt DLLs, a chiaki.exe and windeployqt's plugin trees into an installer for an
application that loads none of them.

The version mechanism is the part worth keeping: GetVersionComponents reads x.y.z off
the packaged exe, and the csproj already keeps its informational version free of a
commit suffix so that an installer can reuse it verbatim. Pointed at the staged
ChiakiNg.exe it reads 1.10.0, which is what CMakeLists sets for the Qt client - one
version for the two executables, without a second place to update.

Filed apart from PP273 rather than with it because Inno Setup is not on this machine,
and a script whose compiler has never run on it is a guess. The payload it would package
is not.

### §PP275 An ignore rule inherited from a generator

Line 24 of .gitignore is scripts/chiaki-ng.iss, and it arrived with the file. Upstream
generated the Inno Setup script from a wizard as part of a release job, so ignoring it
was correct there: it was output, not source.

Here it is neither generated nor generated-from-anything. It is a tracked file that
PP274 is about to edit by hand, and it will keep being edited by hand every time the
payload's shape changes. Git honours the rule only for untracked paths, which is why the
file is in the repository at all and why nothing has gone wrong yet.

What the rule costs is one specific move: a checkout where the file is removed and
written again - a revert, a bad merge resolved by deleting and restoring, a fresh copy
from another branch - has an untracked scripts/chiaki-ng.iss that git add silently
declines to stage. The commit lands without it, and the next clone has no installer
script. There is no error at any step, because declining to stage an ignored path is
what the rule asks for.

The fix is deleting the line. What is worth keeping is scripts/Output beside it: that
one really is ISCC's default output directory, and a hand-run compile that does not pass
OutputDir still lands there.

## Block F — Managed core

### §PP23 The oracle this block cannot be written without

chiaki exists because the PlayStation remote play protocol was reverse engineered. There
is no document to implement against: the 16935 lines in lib/src are the specification,
and a managed rewrite that reads them and reproduces them is a translation whose only
correctness test is behavioural.

That is true of the protocol as a whole and NOT true everywhere, which is a correction
worth carrying here rather than leaving as a pleasant surprise. test/ holds 5512 lines -
munit cases over gkcrypt, rpcrypt, takion, bitstream, the reorder queue and the decoder,
plus 3081 lines of recorded FEC cases and a captured video packet. Where those exist,
the expected output is already agreed with real hardware and the rewrite is checked
against a fixture rather than against a running console.

What this task adds is the rest: run both implementations against the same input and
compare. The same registration exchange, the same key derivation from the same seed, the
same takion frames in and the same feedback out - and where a console is needed, a
captured session replayed against both, which is what makes the comparison repeatable at
all.

The alternative is what a rewrite of a protocol usually looks like: it works against one
console on one firmware, and every report afterwards is a guess about which of 16935
translated lines was wrong.

### §PP26 Crypto is where a rewrite dies quietly

rpcrypt.c is 2428 lines, gkcrypt.c 574 and ecdh.c 240, all of it over OpenSSL's EVP,
HMAC, SHA, RAND, EC and BN. Underneath, it is P-256 ECDH, AES in the modes the protocol
uses, HMAC-SHA256 and a key derivation with the console's own quirks baked in.

.NET has all of the primitives. System.Security.Cryptography covers ECDiffieHellman,
AesGcm, HMACSHA256 and RandomNumberGenerator, so nothing here needs a third party
library and nothing here needs unsafe code. The difficulty is not the primitives, it is
the sequence: which bytes, in which order, with which padding, are hashed into which
key.

Which is why this task depends on the oracle rather than merely benefiting from it.
Every step of the derivation has a fixed input and a fixed output, so the whole of it
can be tested against the C implementation offline, without a console in the room - and
it should be, because the failure it prevents is one where nothing appears wrong except
that the session never opens.

### §PP27 The transport, and the only place GC is a real question

takion.c is 1845 lines plus takionsendbuffer.c at 267 and reorderqueue.c at 200: the
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

session.c is 1182 lines, ctrl.c 1469 and streamconnection.c 1296. Together they are the
connection: what is sent in which order, what is waited for, what a timeout means at
each point, and how a session comes apart when the console stops answering.

There is no diagram and the code is the diagram. Translating it means reading control
flow that was written to match observed behaviour, not designed - and the honest
expectation is that some of it looks wrong and is not.

Two consequences for how this is taken. It should be split when it is started rather
than now, along the three files, because a single review of 3947 translated lines is not
a review. And it is the task that most benefits from the oracle running a full captured
session end to end, since almost nothing here has a fixed input and a fixed output the
way the crypto does.

### §PP29 The first thing that can be proved against a console

regist.c is 910 lines, discovery.c 481 and discoveryservice.c 384: the broadcast that
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

ffmpegdecoder.c is 354 lines and bitstream.c 406, and behind them is FFmpeg doing
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

http.c is 232 lines around curl, and json-c parses what comes back. Both are vendored or
fetched, both exist to do something the .NET base class library does without a
reference, and neither is on the latency path.

The line count is the wrong measure of it, and so is the one below. `remaining PP33`
counts 420 curl and json-c call sites across lib/src, most of them in the hole-punching
code rather than in http.c - and porting INTO app/ removes none of them. That number
reads 420 until libchiaki stops fetching both libraries, and then it reads zero. It is
an end state and not a progress bar; the `## Done when` list is what says how far along
this is, and it is there because reading this query as a burndown made four shipped
tasks look like none.

```roadkeep-remaining
lib/src/**/*.c :: curl_easy_|json_object|json_tokener
```

There is no design decision here and that is why it is worth filing separately: it
deletes two dependencies from the build, and it can be taken by whoever wants to see the
shape of a translated file before starting one that matters. The 420 is what stops
"cheapest" being read as "small".

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

## Block G — Test discipline

### §PP36 Where a red test has to stop something

After the chiaki-ng workflows were removed, .github/workflows holds one file and it
lints the roadmap. Nothing compiles, nothing runs a test, and nothing on a push can fail
for a reason that is about the code.

What this needs is small and specific: dotnet test in the same workflow that builds,
failing the job on a red assertion, with the results readable without opening a machine.
Visual Studio's Test Explorer answers the inner loop and answers nothing about a branch
somebody else pushed.

It is filed early in this block because the two tasks after it are both worthless
without it - a suite and a ratchet that only run when someone remembers are a suite and
a ratchet that report on the day they were written.

### §PP38 The ratchet

The non-goal says no line ships without an assertion that fails without it. Stated and
unmeasured, that is a sentence in a file, and the first week under pressure is when it
stops being true.

What makes it hold is a count in CI: how many shipped lines have no test naming them,
allowed to fall and never to rise. It does not demand that the debt be paid at once,
which is what makes it survivable - it demands only that it stop growing.

It needs the ledger and the suite to be joinable, which is the one piece of design in
this task: a test has to be able to name the line it holds, whether by convention in the
test name or by an attribute the count can read. Without that join, the number is a
guess and a gate on a guess is worse than no gate.

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
