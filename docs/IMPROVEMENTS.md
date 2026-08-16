# Improvements

## Block A — Core

### §PP1 The host the screens land in

The reference is not a taste: claude-tray is a Windows tray application this author
already ships, and its csproj settles every question this port would otherwise re-open.
WinExe on net10.0-windows, UseWPF for the windowed UI with the SDK's built-in Fluent
ThemeMode, WinForms kept only for the tray icon, and PublishSingleFile with
SelfContained on win-x64 so the whole application is one file an installer can carry.

What this task is NOT is a screen. It is the project, the manifest, the icon, the
version as a single source of truth, and a window that opens empty. Every screen below
is filed against a host that already builds, because the alternative is a first screen
that carries the build system on its back and cannot be reviewed as a screen at all.

The existing Qt executable stays until Block D empties. Two executables in one tree is
the ordinary shape of a port, and the one that is not shipped yet is the one being
written.

### §PP2 The store the port inherits

settings.cpp is 2518 lines and qmlsettings.cpp another 2086, and between them they own
the registered consoles, the PSN account id and refresh token, the per-profile overrides
and every video, audio and controller preference. All of it is written through
QSettings, which on Windows means the registry under the Qt organisation key.

The decision is which side moves. Reading QSettings from .NET is a registry read and
costs almost nothing; writing a new store and migrating on first run costs a migration
nobody can test against every version that ever shipped. The cheap path is to read what
is there, write the new store, and keep the old keys untouched so a rollback to the Qt
build still works.

What makes this a task and not a step inside the settings screen is the order: the
console list and the registration flow both read this store, and both are due long
before the settings screen is drawn.

### §PP3 One answer for where things live

sessionlog.cpp writes a log per session, the registration path writes key material, and
both resolve their directory through QStandardPaths. .NET's Environment.SpecialFolder
does not produce the same answer, so a ported build silently starts a second tree beside
the first.

The user-visible cost is small and permanent: a support request quotes a log the other
build cannot find. The fix is to state the paths once, in the new host, as the Qt paths
- not to improve them. Relocating the data is a separate decision, and taking it during
a port means never knowing which of the two changes broke a file that went missing.

## Block B — Native interop

### §PP4 Where managed code stops

lib/ is the part of this project that is not being ported. It is C, it is where the
protocol lives, and it is the reason a rewrite is out of the question. What has to exist
is the seam: which functions .NET calls, how a native callback reaches a managed handler
without the GC moving it, and who owns the buffers a video frame arrives in.

Two shapes are on the table. Direct P/Invoke over the existing headers is less code and
puts every marshalling decision in C#. A thin native shim - a C++ DLL exporting a flat,
callback-free surface the way the Qt GUI already consumes it - is more code and moves
the hard parts to the side of the boundary that already compiles them.

The measure is not elegance: it is how much of streamsession.cpp survives. That file
already adapts libchiaki to a GUI, and the shim question is whether the port re-adapts
it in C# or keeps a Qt-free copy of it in C++.

### §PP5 The session, without Qt

This is the file the whole port turns on. It holds the connect flow, the audio and video
callbacks, the keyboard and controller state that is sent upstream, the reconnect logic
and the teardown - and it expresses all of it in QObject, signals, QThread and QTimer.

Nothing about that logic is Qt. The dependency is in how it announces itself. So the
task is mechanical in shape and large in size: the same state machine with the framework
types removed, announcing over whatever the shim in PP4 settled on.

Doing it here, and not inside the first screen that needs a stream, is what keeps the
video work in Block C from being blocked behind a UI decision. It is also the only
honest way to size the port: until this file is Qt-free, every estimate below it is a
guess.

### §PP6 Discovery as the first proof

discoverymanager.cpp is 428 lines over a UDP broadcast, a reply parser and a wake
packet. It talks to libchiaki for the protocol and to Qt for the socket and the timer,
and the Qt half has a direct .NET equivalent in UdpClient.

It is filed early for its size rather than its importance. The console list is the first
screen worth drawing, discovery is what fills it, and a working list is the end-to-end
evidence that the managed side can call the native side and get an answer back. If the
boundary in PP4 is wrong, this is where it costs an afternoon instead of a block.

### §PP7 The browser the login needs

PSNLoginDialog.qml imports QtWebEngine and hosts a WebEngineView because the login is
Sony's page, not this application's: what the flow needs is to watch for a redirect and
read the code out of it. QtWebEngine is also the single largest thing in the build - a
Chromium - for one screen.

WebView2 is the Windows answer and is part of the OS on Windows 11. It exposes the same
navigation event, so the flow is unchanged; what changes is that the installer stops
carrying a browser. jsonrequester.cpp and psntoken.cpp then become HttpClient calls,
which is the smallest part of this task and the part with no decision in it.

### §PP8 Input is not a screen detail

controllermanager.cpp is 950 lines: device hotplug, SDL mappings, the DualSense
specifics - touchpad, motion, haptics, trigger effects - and the translation into what
the session sends upstream. qmlcontroller.cpp is only the thin part that exposes it to
QML.

SDL is not Qt and does not have to move. What has to move is how the events arrive:
SDL's own event loop against a WPF Dispatcher, on a thread that does not stall on
rendering. The reason this is filed in the interop block rather than under a screen is
that two very different consumers need it - the session, which sends the state, and the
mapping screen, which shows it - and both are due before the settings screen.

## Block C — Video and input path

### §PP9 Vulkan under a D3D9 compositor

qmlmainwindow.cpp is 7538 lines, the largest file in the GUI, and almost all of it is
one job: take a decoded frame, run it through libplacebo on Vulkan, and present it in
the same window QML is drawing into. WPF cannot be that window. Its composition target
is D3D9Ex, and there is no path that hands it a Vulkan swapchain.

Three shapes, and the task is to pick one rather than to discover it mid-screen:

A child HWND hosted by HwndHost, rendering Vulkan directly. Fastest, and the option that
keeps libplacebo untouched - but an airspace child window sits above all WPF content, so
nothing can be drawn over the video, which the overlay task in this block then has to
answer.

A shared D3D11 texture presented through D3DImage. Composes properly with XAML above it,
at the cost of an interop copy per frame and the Vulkan-to-D3D11 sharing that makes it
possible.

Dropping libplacebo for a D3D11 renderer. Native to the target, and it discards the
shader work that is the reason the picture looks the way it does.

The right answer is not obvious and it is not cheap to change later, which is why it is
one task, taken before the two that follow it.

### §PP10 The overlay is the renderer decision, spelled out

StreamView.qml is 1305 lines and StreamMenuWindow.qml another 435: the connection state,
the latency and bitrate readouts, the loading and reconnect states, the touchpad hints,
and the menu a user opens without leaving the stream.

They are filed apart from the other screens because they are not a screen problem. If
PP9 lands on a child HWND, none of this can be XAML above the video and it has to be
drawn into the frame or into a layered window over it. If PP9 lands on D3DImage, all of
it is ordinary XAML and this task is the easiest one in Block D wearing a different
label.

That is the whole reason it is a dependent line and not a note inside PP9: the cost of
this one is not known until that one is decided.

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

## Block D — Screens

### §PP12 The control vocabulary, and the focus nobody ships

gui/src/qml/controls holds Button, CheckBox, ComboBox, RadioButton, Slider and TextField
in 263 lines. The size is misleading: they are what makes every screen below look like
one application, and they are where directional focus lives - which control the stick
moves to, what the cross button confirms, what circle cancels.

WPF's Fluent ThemeMode answers the first half for free and the second half not at all.
Keyboard tab order is not gamepad navigation: a couch application needs a focus engine
that takes a direction and a current element and picks the next one, plus a consistent
confirm and cancel binding, plus a focus visual that is legible from three metres.

Filed first in this block because every screen after it is drawn in whatever vocabulary
this task settles. Doing it after two or three screens means porting them twice.

### §PP13 The front door

MainView.qml is 437 lines and AutoConnectView.qml another 105: the discovered and
manually added consoles, their state, the wake and connect actions, the entry points
into registration and settings, and the path that connects without asking when a console
is already known.

It is the second thing to draw and the first thing to demo. Discovery underneath it is
real work already done, the controls are settled, and a list that fills itself with the
consoles on the network is the moment the port stops being a build system and starts
being an application.

### §PP14 Registration is one flow

RegistDialog.qml is 264 lines, ManualHostDialog.qml 78, ConsolePinDialog.qml 42 and
ProfileDialog.qml 87. They read as four dialogs and behave as one path: find or type a
console, enter the PIN the console shows, exchange it for the key material that is
stored, and end with a console the list can connect to.

Porting them together is what keeps the validation consistent - the PIN length, the host
reachability, the error a wrong PIN produces - and what makes the flow testable as a
flow. It also completes the first genuinely useful build: with this and the front door,
a fresh install can register a console and reach the stream screen.

### §PP15 The two screens this application does not own

PSNLoginDialog.qml is 455 lines and PSNTokenDialog.qml 427. Between them they host
Sony's login page, catch the redirect, take the code, and offer the manual path for the
user whose browser flow failed - paste the redirect URL by hand.

They are the screens where a port most easily loses behaviour, because most of what they
do is react to a page nobody here controls: a login that changes its markup, a redirect
that arrives with a different query, a token that expires mid-flow. The manual fallback
exists for exactly that reason and has to survive the port intact.

### §PP16 The screen the port is sized by

SettingsDialog.qml is 2984 lines - larger than the next two screens together - and it is
the visible half of qmlsettings.h, which exposes 149 properties, and qmlsettings.cpp,
which is 2086 lines of getters, setters and change notifications over settings.cpp.

The markup is the cheap half. What has to be rebuilt is the binding surface: properties
that notify, validate, and in several cases only apply to a session that is not running.
WPF has an equivalent in INotifyPropertyChanged and the settings store from PP2
underneath it, so the shape is known - the cost is the count.

The count is therefore declared rather than written down, so `remaining PP16` answers it
from the tree instead of from whenever this paragraph was last edited. It was 151 when
this line was filed and 149 on 2026-08-16, which is the drift PP73 exists about.

```roadkeep-remaining
gui/include/qmlsettings.h :: Q_PROPERTY
```

It is filed after the front door and registration deliberately. A user can register and
stream with defaults; nobody can stream at all if the console list does not work.
Splitting it further by tab is the obvious escape hatch if this line proves too large to
hold, and the split should follow the tabs the current screen already has.

### §PP17 The screens that belong to the renderer

PlaceboSettingsDialog.qml is 1192 lines and PlaceboColorMappingDialog.qml 940:
upscalers, dithering, deband, tone mapping curves, gamut mapping, and the presets over
them. Every one of these is a libplacebo option, named the way libplacebo names it.

Which is why they hang off the renderer decision and not off the settings screen. If the
renderer keeps libplacebo, this is a large but mechanical port of a form. If it does
not, most of these controls have nothing to write to and the honest outcome is a much
smaller screen - a decision that belongs to whoever takes PP9, recorded there rather
than discovered here.

### §PP18 Mapping needs the device in the room

ControllerMappingDialog.qml is 350 lines: it shows the pad, lights up whatever the user
presses, and writes an SDL mapping string out the other end.

Every other dialog can be drawn against a mock. This one cannot - the screen IS the live
event stream, and a port that renders it correctly with no device attached has proved
nothing. It is also the cheapest place to find out whether the input path from PP8
delivers events to the UI thread promptly enough, which is why it is worth doing before
the small dialogs rather than after.

### §PP19 The tail

ConfirmDialog.qml at 104 lines, RemindDialog.qml 135, DisplaySettingsDialog.qml 193,
SteamShortcutDialog.qml 194 and DialogView.qml 131, which is the host the others open
inside.

Nothing here is hard and nothing here is interesting, which is the argument for doing it
last: by then the confirm and cancel bindings, the focus behaviour and the dialog chrome
are decided by six screens that had to decide them, and this becomes transcription.
Taken first, each of these would be a small independent invention of the same three
things.

SteamShortcutDialog is the one to look at twice - it writes into Steam's own
configuration, and whether that still belongs in a Windows-only build is a question for
whoever takes it.

## Block E — Windows-only build

### §PP21 The dependency that says it is over

Eight Qt modules are named in gui/CMakeLists.txt, WebEngineQuick among them - a whole
Chromium for one login screen. As long as the find_package line stays, a screen can
still be left behind and nobody notices, because it still builds.

Removing it is a one-line edit and a long tail: QString and QList in files nobody
thought of as UI, QSettings under the settings store, QNetworkAccessManager under the
PSN calls, the resource system behind the icons. That tail is the real content of this
task, and it is also the honest completion criterion for the whole port - the day Qt is
not a dependency is the day the QML is actually gone, rather than merely unused.

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

### §PP24 What Visual Studio has to open

Today the tree is CMakeLists.txt plus a vcpkg.json naming eleven ports. Visual Studio
can open that as a CMake folder, and it is not the same thing as a solution: no project
references, no NuGet, no F5 with a managed debugger attached to the code being written.

The target is ordinary for .NET and unremarkable to describe - a .sln, one csproj per
component, NuGet where vcpkg was, MSBuild all the way down. It is filed second rather
than first because the oracle above has to exist even if this never happens, but it is
filed early because everything after it is easier inside a solution than beside one.

Whatever stays native - and the decoder task in this block argues that at least the
decoder does - keeps its own build and is consumed as a binary or a vcxproj the solution
references, which is a thing a solution does well.

### §PP25 The part that is generated, not written

lib/protobuf/takion.proto is the schema and nanopb is only the generator that turns it
into C. Google.Protobuf runs the same file and produces C# with no translation and no
judgement call.

That makes it the natural first piece of the rewrite: it is a build step, it is
verifiable by round-tripping bytes the C build produced, and every later task in this
block sends or receives these messages. It also deletes a vendored dependency outright
rather than replacing one, which is the only kind of dependency change that is free.

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

The line count is the wrong measure of it, though, and this is the line that shows why.
What has to be replaced is the API surface: `remaining PP33` counts 420 call sites
across lib/src on 2026-08-16, most of them in the hole-punching code rather than in
http.c. HttpClient and System.Text.Json replace the libraries, but they do not replace
those calls one for one.

```roadkeep-remaining
lib/src/**/*.c :: curl_easy_|json_object|json_tokener
```

There is no design decision here and that is why it is worth filing separately: it is
the cheapest visible progress in the block, it deletes two dependencies from the build,
and it can be taken by whoever wants to see the shape of a translated file before
starting one that matters. The 420 is what stops "cheapest" being read as "small".

### §PP34 The layer that disappears

thread.c is 270 lines and log.c 232. The first is a portability shim - threads, mutexes,
condition variables, one API over pthreads and Win32 - and the second is the logging
every other file calls.

In managed code the first has no reason to exist: Task, lock, SemaphoreSlim and
CancellationToken are the platform, and a translation that reproduces the shim
faithfully would be the clearest sign that the port was mechanical rather than
considered. The second becomes whatever the .NET host already logs through, which
matters because the session log is a support artefact users are asked to attach.

Filed early despite being small: every file translated after it inherits how these two
are spelled, and changing that later means touching all of them.

## Block G — Test discipline

### §PP35 The suite that is already written

test/ holds 2095 lines of munit tests and 3417 lines of captured vectors: gkcrypt at 440
lines, rpcrypt at 311, takion at 232, bitstream at 207, ffmpegdecoder at 201,
reorderqueue at 185, and fec_test_cases.inl alone at 3081 lines of recorded erasure
cases with a real video packet parse beside it.

This changes what the managed rewrite is. Reading it as a translation with no
specification is only true of the parts nobody tested; for crypto, FEC, bitstream and
the reorder queue there are fixed inputs and expected outputs already agreed with a real
console, and they are the exact modules where a silent translation error is most
expensive.

So this is filed as the first test task and as a dependency of the rewrite rather than a
chore after it. Ported to xUnit, these run in Test Explorer and in CI against the
managed implementation, and every one of them that stays green is a claim the C build
already backed.

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

### §PP37 Testable screens, or eight that are not

The QML being replaced kept its logic in C++ behind a property surface, which is why
qmlsettings and qmlbackend are testable objects and the markup is thin. A WPF port can
reproduce that with view models, or it can put the same logic in code-behind and lose
it.

The difference is not style. A view model can assert the things that actually break: a
PIN field that enables the button one character early, a console list that keeps a stale
entry after a failed refresh, a settings property that writes on every keystroke rather
than on commit, a dialog that stays open after the operation it was waiting on failed.
None of those are reachable from a test that has to instantiate a window.

Filed against the control vocabulary rather than against any one screen, because it is a
decision taken once and inherited by all eight - and taken late, it is eight rewrites.

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

### §PP61 A cold start that is only cold once

measure-startup reports its first run apart from the rest and calls it cold, which is
right within one invocation and wrong across them. Measured in the same session: 3771 ms
on the first run after the build, then 1218 ms as the "cold" run of every later
invocation. A 3.1x spread, and the tool reports the second figure with the same label as
the first.

The cause is that the OS file cache outlives the process. After one launch the loader,
the Qt plugins and the QML cache are resident, so run 1 of invocation 2 is a warm start
wearing the cold label. Nothing in the report says which state the machine was in, so
two cold-start numbers from two sessions are not comparable and nobody reading them can
tell.

This matters because cold start is the number PP46 exists to produce and the one most
likely to reach a release note. A figure that moves 3x with invisible machine state is
not a measurement, and the harness currently makes it look like one.

The fix is to control the variable or record it. Controlling it means dropping the
standby list before the first run, which needs elevation and is worth deciding rather
than assuming. Recording it means stamping something honest into the report - a
cache_state the caller sets - so a reader can refuse to compare numbers taken under
different conditions, the way compare-baselines already refuses mismatched settings.

### §PP66 A result that cannot say which card it came from

The decode-path harness records the ffmpeg it linked and the stream it read, and then
names the card nowhere. release-4060.json says RTX 4060 in its filename and in the
README beside it, which is exactly as durable as a filename: a second machine's run
copied into the same directory under any other name is unattributable, and the numbers
most worth comparing are the ones taken on different cards.

spike/video-upscale does not have this gap. It creates a D3D11 device to do its work
anyway, so DescribeAdapter reads the DXGI description and the vendor and device ids
straight out of it, and its committed JSON carries them. decode-path creates no such
device - it asks libavcodec for a hardware context and never touches DXGI - which is why
the field was never there rather than why it should not be.

The cheap route is the context it already builds. av_hwdevice_ctx_create for d3d11va
yields an AVD3D11VADeviceContext holding an ID3D11Device, and one QueryInterface to
IDXGIDevice reaches the same description video-upscale prints. That reports the adapter
ffmpeg actually chose, which is worth more than one enumerated independently: a machine
with an RTX 4060 and an Intel UHD 770 has two, and the run belongs to whichever the
driver handed over.

The rule this restores is the one the other spike already follows: a committed result
should be readable years later by someone who has only the file, and a number whose
machine is a guess is a number nobody can reuse.

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

### §PP48 The NVIDIA path that already exists

The shipped half settled the cost. All three hardware paths decode within 13% of each
other on an RTX 4060, cuda and vulkan inside 0.1% because Vulkan Video and NVDEC are the
same silicon, so decode speed does not separate them. What does is a copy:
make_fallback_snapshot_frame runs on every queued frame and calls
av_hwframe_transfer_data for any hardware frame that is not AV_PIX_FMT_VULKAN - 793us on
cuda, 2253us on d3d11va, nothing on vulkan. The preference buys a cheaper copy rather
than a faster decode, and the auto ordering is right for that reason rather than the one
it was written for.

What is left is the frame-drop half, and what cannot be synthesised is what shapes it.
Decode cost follows resolution and bitrate, so a generated stream carries it. Frames
dropped under network jitter follow the network, and no encoder here produces that. It
needs a live session, which needs a console. No new instrument is needed: the PP42
telemetry row already names the decoder that produced it, so one session per decoder
answers this and a spike never will.

Filed rather than explained: d3d11va's send is bimodal, a 103us median against a 26990us
p99. A submission that sometimes takes 1.6 frame intervals is a stall, and its mean is
an average of two behaviours rather than a description of either.

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

### §PP51 First, not only

The direction is that NVIDIA is where the tuning goes: the decoder that gets measured,
the upscaler that gets integrated, the path that is regression tested. That is a
reasonable focus and it is not the same statement as requiring the hardware.

What this task writes down is the second half. Which paths must keep working - d3d11va
decode, the neutral renderer, an SDR present without NGX. What the client does when NGX
is absent, which is to say nothing visible except that the option is not offered. And
what the gate runs on, since a vendor path that is the only one with a test is a vendor
requirement with extra steps.

The cost of not stating it is specific: a Windows machine with Intel graphics is an
ordinary laptop, and an application that fails on one has not shipped, it has narrowed.

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
