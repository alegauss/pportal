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

### §PP851 The half of the haptics a DualSense can hear

PushHapticsFrame has two arms and PP846 ported one. The fold to a single rumble strength
is what the Qt client does when haptics_output is 0 - a pad with no haptic actuators, or
a user who turned the output off. Below that early return is the other arm, and it is
the one a DualSense is for.

WHAT IT DOES. The stereo frame is resampled through an SDL_AudioCVT, remixed from two
channels to four, and written to an audio device that IS the pad: a DualSense enumerates
as an output endpoint. The actuators are wide-band, so what arrives is a waveform rather
than a strength - which is exactly the information the fold throws away.

SO THE PORT'S PS5 PATH IS THE PS4 PATH. PP846's routing sends a DualSense the 47-byte
effects report, which drives its rumble motors - real, and the same two motors a
DualShock 4 has. Its own design called that outcome a worse result than the fold is for,
and its ledger sentence does not claim otherwise.

WHAT THIS NEEDS THAT PP846 DID NOT. An output device rather than a controller handle,
chosen by name among the endpoints Windows reports; a resampler; and the channel remix,
which has a scaling override in the client nothing has read yet. The port has an audio
engine already - PP820's - and whether the pad is a second one of those is the first
question rather than a detail.

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

## Block G — Test discipline

### §PP849 A census that states its own counts in prose

RoadmapProseReaders judges every string literal in app/ that also occurs in a governed
file, and its own summary states the taxonomy as numbers: four fragile, six address,
four held, nine failing open, ten incidental, and "the other twenty-nine" as their sum.

Five of those six were correct until PP844 added a row, at which point two were wrong
and the sum with them. Nothing noticed: CountedClaim reads the governed documents and
this is a docstring, so the drift is invisible by construction - which is the rule this
tree already writes down, that a source comment stays qualitative because no gate can
check it.

WHAT MAKES THIS MORE THAN THAT RULE BROKEN ONCE. Count(ProseReading) is a public method
on the same class, it answers every one of the six, and it has no caller in app/ or
tests/. The numbers are not merely unchecked - they are unchecked beside the thing that
computes them, and the reason a reader trusts the summary is that it looks derived.

Two shapes, and the choice is the point rather than the edit. Turn the sentence
qualitative and delete the method: the rule applied honestly, losing a reader's sense of
the proportions. Or assert the six from Count in the census's own test, which keeps them
and makes the next row's arithmetic a gate. Only the second gives Count a caller.

Either is minutes. What neither is: the numbers as they stand, true today and false for
the length of one commit.

### §PP850 The argument list as a third spelling

PP386 found 47 drift predicates pinning a bare call to its exact spelling, semicolon
included, and four of them broke in one block. Its fix routed the 27 occurrence
predicates through CCall, which compacts whitespace and forgives the terminator - so
`free(notif)` and `free(notif);` became one question.

THE ARGUMENT LIST IS THE SPELLING IT DID NOT REACH. CCall.Happens matches the call text
whole, argument list and closing paren included, so a check written for a two-argument
call is a check about that arity. Nothing here can ask "is this callee called with this
first argument" without naming everything after it.

TWO WENT RED IN PP845, both about something else. StreamPhaseFlipTests asked for the
literal `new PadFeed(phase.Pad)` and is about which SINK the pad pushes at;
ManagedControllerStateSinkTests called Single on the constructor's parameters and is
about which SEAM it takes. The console's touchpad broke both, and neither failure named
the thing its check exists for - the sentence PP844's two controls earned, one shape
over.

The fixes were one line each, by hand, as PP386's four were before it built the reader.
Two tests written under PP845 join the population, which is the honest half of filing
this: both are call-shaped literals and one names an argument list on purpose.

What a reader needs is a predicate over a callee and a PREFIX of its arguments. Whether
that belongs in CCall, which reads C, is the question this does not answer.

### §PP852 A premise measured once and guarded by nothing

CFunction.RawBody finds a definition by looking for the name, then the first
parenthesis, then a brace. Inside a `/* */` comment those three sit exactly as they do
in code, so a commented-out function is a definition to it - and what saves the `//`
form is only that the next line's marker interrupts the gap between the parenthesis and
the brace.

PP847 ANSWERED THIS ONCE, WITH A SCRIPT IT THREW AWAY. Zero block-commented definitions
across lib/src and shim; sixteen comments holding unbalanced braces, of which two sit
inside a body and cancel. Both numbers went into a docstring, where CountedClaim cannot
see them and this tree's own rule says a source comment stays qualitative because no
gate can check one.

THE PREMISE IS LOAD-BEARING AND THAT IS THE DIFFERENCE. RawBody has two callers, both
asking about prose the C's author left behind - the commented-out wire field beside the
tag, and the switch admitting its own shape is wrong. Neither would notice a
block-commented function arriving in the file it reads; it would simply start answering
about one.

WHAT WOULD SETTLE IT is a sweep, and it is small: every `/* */` in the swept C, refused
if it holds a parameter list followed by a brace. The same pass answers the brace
question, an unbalanced one inside a body being the other half of what stripping made
safe. Whether it belongs beside CFunction or in a census already walking this C is the
open choice.

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
