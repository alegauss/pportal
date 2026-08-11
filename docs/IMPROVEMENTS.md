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

SettingsDialog.qml is 3271 lines - larger than the next two screens together - and it is
the visible half of qmlsettings.h, which exposes 151 properties, and qmlsettings.cpp,
which is 2086 lines of getters, setters and change notifications over settings.cpp.

The markup is the cheap half. What has to be rebuilt is the binding surface: 151
properties that notify, validate, and in several cases only apply to a session that is
not running. WPF has an equivalent in INotifyPropertyChanged and the settings store from
PP2 underneath it, so the shape is known - the cost is the count.

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

### §PP20 The code for platforms that are gone

The non-Windows trees are already deleted - android, switch, setsu, steamdeck_native,
the macOS bundle files, the Linux desktop entry, the sd input code and the systemd
inhibitor. What is left is inside the files that stayed: 33 Q_OS_MAC branches, 17
Q_OS_LINUX, two raw __APPLE__ guards and the rest spread across the CMake and the
sources.

They compile to nothing on Windows, so this is not a defect a user meets. It is a cost
every reader of these files pays, and it is paid worst by whoever is porting them, who
has to decide for each branch whether it is dead or whether Windows quietly falls
through to it. Doing this before the port rather than after means the files being read
are the files that matter.

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
