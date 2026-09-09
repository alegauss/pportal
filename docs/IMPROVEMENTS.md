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

### §PP845 The pad's other two halves, ported and unplugged

PadFeed folds an SDL event into a translation and pushes the result. WriteTo sets three
fields - the button mask, both triggers and the four stick axes - and that is the whole
of what a console is told. The touchpad's coordinates and the gyroscope's readings are
fields on the same state and nothing ever writes them.

WHAT IS MISSING IS THE WIRE AND NOT THE PORT, which is what makes this a line rather
than an estimate. PP129 mapped SDL's touchpad-and-finger naming onto the console's pool
of two ids. PP130 ported the sensor fusion, down to the stateful filter. PP93 fixed the
dpad touch bounds to the console's own. Four tasks, all shipped, all reachable, none
reached: TouchpadTracker and MotionTracker have no caller outside their tests.

PP838 IS THE PRECEDENT AND THE WARNING. A pad plugged in after the picture appeared was
never opened, because PadFeed's event arm had no case for arrival - and the run reported
`armed True, 0 state(s) pushed`, which reads as a pad nobody touched. This is the same
shape one field along, and it reports nothing at all: a live run says 635 states pushed
whether or not any of them carried a finger.

The trigger is the pads the port supports. A PS4 or PS5 pad has both a touchpad and
sensors, the console expects input from each, and today a player using one has a gamepad
with the two things that make it that pad switched off.

### §PP846 A pad that is only an input

Gamepads' own summary says it: eighteen SDL entry points, every one an event, joystick
or gamecontroller call, and no output among them. PadFeed reads a pad and pushes a
state. The stream carries haptics the other way and nothing consumes them.

BOTH ANSWERS ARE ALREADY WRITTEN, which is what makes this a wire. PP97 ported the
haptics-to-rumble fold for pads with no haptic motors; PP98 fixed the wrap that told the
loudest frame to rumble at zero; PP127 built the 47-byte DualSense effects report field
by field against the struct's own layout, with the intensity ladder beside it. All
shipped, all correct, all called by nothing but their own tests.

THE TWO PADS WANT DIFFERENT THINGS and that is the substance rather than a detail. A PS4
pad has rumble motors, so the fold is the whole answer for it. A PS5 pad has haptic
actuators and adaptive triggers, and folding the console's haptics to rumble on one
throws away what the console sent - a worse result than the fold is FOR, and the reason
PP127 exists at all.

The hint pair is the evidence this was always the plan: Gamepads sets
SDL_JOYSTICK_HIDAPI_PS4_RUMBLE and its PS5 twin before SDL_Init, because without them a
pad on USB does not rumble. Two hints set for an output call nobody wrote. A run cannot
report the gap either, which is the shape PP838 was: the pad works, the picture arrives,
and the only thing missing is something nobody counted.

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

### §PP848 Prose that credits a reverted commit

92 mentions across 51 files in app/ and tests/ name PP696, two of them PP843's own
account of the reversal. That commit took the frame path's four files out of the build,
put the shim's fourteen behind an #ifdef and replaced session.c's run with a callback;
PP763 reverted all three, because it shipped a client that could not stream.

EACH HALF WAS RE-ESTABLISHED BY A DIFFERENT COMMIT. PP27 took the files out again and
added the option; PP783 and PP784 made session.c's two phases callbacks. So a sentence
reading "PP696 took the four files out" is true as history and false as explanation, and
explanation is what a reader wants from it: the state it describes holds, for reasons
that are three other tasks.

PP843 IS ONE FILE OF THE 51, corrected by hand. It was worth its own line because that
summary is where somebody looks up whether the flip has happened, and the wrong answer
sent a session after an option that did not exist. Fifty more hand edits is not
obviously the right shape.

WHAT THIS ASKS, AND THE CENSUS AN ANSWER NEEDS. The ledger carries an `undone_by` field
per entry, and `delivered F` reports PP696's as null - so the reversal is recorded
nowhere a check can read. Registering it would let one reader ask, of any id a docstring
credits, whether the ledger calls it undone. Whether that is the fix, or whether the
attributions should stop naming commits, has not been read.

## Block G — Test discipline

### §PP847 A body reader that cannot tell a mention from a use

CFunction.Body is PP343's: it finds a C function's body by counting braces rather than
trusting a closing one in column zero, and it returns that span verbatim - comments
included.

Every reader beside it strips. CCall.Code blanks comments and string contents, and it is
what PP437's entry-point census, PP540's sweep and PP793's export sweep read instead of
the raw text, each for the same reason: a symbol named in prose is not a reference to
it.

WHAT IT COST, MEASURED. PP30 deleted galois_init_default_field from chiaki_lib_init and
left a note where the call had been, naming the function it had just removed. The
assertion that the field init is gone read the body, found the note, and went red - in
the commit that did the work, on the file that proved it done. The remedy was three
characters at the call site and a gate run to find.

A caller CAN pass the stripped source, and PP30 now does. That is the argument for
moving it rather than fixing it again: the mistake is available at every call site, and
this is the third time a check here has found its own explanation.

Two shapes. Strip inside the reader, or rename the raw form so a caller has to ask for
it. The first is smaller and leaves nobody silently on the old behaviour; the brace
count survives it, because the stripper replaces a comment with the newlines it spanned
and touches no brace outside a comment or string.

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
