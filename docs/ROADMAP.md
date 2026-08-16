# Roadmap (active backlog)

## Priority

- Block H
- Block I

## Block A — Core

- 📋 **PP1** (deps: —) **no .NET host exists, so a XAML window has nowhere to live** — The port needs a target before it needs a screen, and claude-tray already answers what that target is: net10.0-windows with UseWPF, the Fluent theme and one self-contained win-x64 exe. → §PP1
- 📋 **PP2** (deps: —) **settings, registered consoles and the PSN token live in QSettings, which the .NET host cannot read** — An upgrade that forgets every console a user registered is a reinstall, so the existing store is read by the new host before any screen is drawn. → §PP2
- 📋 **PP3** (deps: PP2) **app data, session logs and key files are placed by QStandardPaths, which the .NET host does not share** — Two processes disagreeing about where a log or a key file lives is the defect that outlives the port, so the locations are decided once against the Qt paths. → §PP3

## Block B — Native interop

- 📋 **PP4** (deps: —) **libchiaki is C with function-pointer callbacks and no managed binding, so no .NET code can start a session** — Everything above this line is a screen and everything below it is the protocol, so the boundary is the one piece the port cannot avoid writing. → §PP4
- 📋 **PP5** (deps: PP4) **streamsession.cpp drives the session through Qt signals and QThread, so a session cannot run without Qt** — 2862 lines mix protocol callbacks with Qt types, and a Qt-free session is what lets the interface be replaced once instead of rewritten twice. → §PP5
- 📋 **PP6** (deps: PP4) **console discovery and wake use QUdpSocket, so the console list has no source in a Qt-free host** — Discovery is small, self-contained and the first thing the front door needs, so it is the cheapest proof that the interop boundary holds. → §PP6
- 📋 **PP7** (deps: PP4) **PSN OAuth login runs in a QtWebEngine view, a bundled browser the WPF host has no equivalent for** — WebView2 ships with Windows and catches the same redirect, so the account link costs a control instead of a second rendering engine in the installer. → §PP7
- 📋 **PP8** (deps: PP4) **controller input is SDL wired into Qt events, and 950 lines of it decide what a button does** — A remote play client is driven by a gamepad before a mouse, so the input path is a first-class port and not a detail of whichever screen is drawn first. → §PP8

## Block C — Video and input path

- 📋 **PP9** (deps: PP5, PP43 ✅) **the video is presented by libplacebo into a Vulkan-backed QQuickWindow, which WPF cannot host** — WPF composes through D3D9 and cannot present a Vulkan swapchain, so the choice between a child window and a shared texture is what every stream screen is then built on. → §PP9
- 📋 **PP10** (deps: PP9) **the stream HUD and the in-stream menu are QML drawn over the video and disappear with the renderer** — 1740 lines of overlay assume the compositor they are drawn in, so what replaces the renderer decides whether they are XAML above a surface or drawn into the frame. → §PP10
- 📋 **PP11** (deps: PP9) **fullscreen, HDR handoff and refresh-rate switching are handled by the Qt window** — These are the three settings a remote play session is actually judged by, and each is a Win32 or DXGI call the new window has to make for itself. → §PP11

## Block D — Screens

- 📋 **PP12** (deps: PP1, PP8) **the seven custom QML controls carry the theme, and gamepad focus navigation is built into them** — Fluent gives the look but no gamepad focus engine, so the thing every screen inherits has to exist before the screens that inherit it. → §PP12
- 📋 **PP13** (deps: PP6, PP12) **the console list, the front door of the application, is QML bound to the discovery model** — It is the first screen a user sees and the first that proves the ported discovery, so it is the smallest slice that can be judged end to end. → §PP13
- 📋 **PP14** (deps: PP6, PP12) **registration, manual host, console PIN and profile dialogs are QML with their own validation** — Registering a console is the step between an installed application and a working one, and its four dialogs are one flow rather than four screens. → §PP14
- 📋 **PP15** (deps: PP7, PP12) **the PSN login and token dialogs are 882 lines of QML wrapped around the embedded browser** — The account link is what remote play outside the local network depends on, and these are the only screens whose content is a third party page. → §PP15
- 📋 **PP16** (deps: PP2, PP12) **the settings screen is 3271 lines of QML against 151 properties exposed from C++** — It is the largest single screen by a factor of three, and the property surface behind it is the real measure of the work rather than the markup. → §PP16
- 📋 **PP17** (deps: PP9, PP12) **the renderer tuning and colour mapping screens are 2132 lines of QML over libplacebo options** — Every control on them writes an option that only exists while libplacebo does, so they can only be drawn once the renderer decision has been taken. → §PP17
- 📋 **PP18** (deps: PP8, PP12) **the controller mapping screen is QML bound to the live SDL mapping strings** — A mapping screen is unusable without input arriving from the device being mapped, so it lands with the input path rather than with the other dialogs. → §PP18
- 📋 **PP19** (deps: PP12) **the confirm, remind, display, steam shortcut and dialog host screens are still QML** — They are small and repetitive, and taking them last means each is drawn in a control vocabulary the earlier screens already settled. → §PP19

## Block E — Windows-only build

- 📋 **PP21** (deps: Block D) **Qt6 is still required to build: Core, Gui, Quick, Qml, Svg, Widgets, Concurrent and WebEngineQuick** — The port is only finished when the toolchain says so, and dropping Qt is the check that no screen or service quietly still depends on it. → §PP21
- 📋 **PP22** (deps: PP1) **every CI workflow was deleted, so nothing builds, signs or packages the application** — A Windows-only application with no Windows build is a source tree, and claude-tray already has the shape: publish one self-contained exe, then wrap it in an installer. → §PP22
- 📋 **PP63** (deps: PP62 ✅) **nothing in the tree can configure a Qt build carrying WebEngine, so PP46's before cannot be produced at all** — MSYS2 has no qt6-webengine and no published Windows release carries Chromium, so an MSVC configure built once is the only reference the port can measure against. → §PP63

## Block F — Managed core

- 📋 **PP23** (deps: —) **the protocol has no specification, so a managed rewrite has no oracle except the C code it replaces** — Every line in this block is judged by whether a console still answers, and that verdict has to be automatic before the first byte is rewritten. → §PP23
- 📋 **PP24** (deps: —) **the build is CMake with vcpkg, so Visual Studio opens a folder rather than a solution** — A .NET project is an MSBuild project, and until the tree is one, every managed task below is written in an environment that treats it as a guest. → §PP24
- 📋 **PP25** (deps: PP24) **the wire format is generated by nanopb, a C generator with no managed output** — takion.proto is checked in, so the messages are the one part of this core that is regenerated rather than translated, and that makes it the cheapest first slice. → §PP25
- 📋 **PP26** (deps: PP23, PP24) **3242 lines of crypto over OpenSSL sit between a registration and a session** — This is where a translation error is silent: a wrong byte does not throw, it produces a key that fails to open a session with no clue which of eight steps was wrong. → §PP26
- 📋 **PP27** (deps: PP23, PP25, PP44 ✅) **takion, the transport the whole stream rides on, is 1845 lines of C over raw sockets and timers** — It is the layer where a managed rewrite is judged on latency rather than on output, because every millisecond it adds is one the picture is late by. → §PP27
- 📋 **PP28** (deps: PP23, PP27) **session, ctrl and streamconnection are 3947 lines of state machine with no diagram** — This is the largest single translation in the core and the one where behaviour lives in the ordering of events rather than in any function worth reading alone. → §PP28
- 📋 **PP29** (deps: PP23, PP26) **registration and discovery are 1775 lines that decide whether a console can be found and paired at all** — They are the first thing a fresh install runs and the smallest end-to-end proof that a managed core can talk to real hardware. → §PP29
- 📋 **PP30** (deps: PP23, PP27) **forward error correction is two vendored C libraries doing Galois field arithmetic per lost packet** — jerasure and gf-complete are the only vendored code with no managed equivalent to install, so this is a port rather than a swap and it runs on every frame. → §PP30
- 📋 **PP31** (deps: PP28) **the video decoder is where 100% managed stops being achievable, and no task above says so** — There is no managed H.264 or HEVC decoder that holds 1080p60 at remote play latency, so this boundary is chosen deliberately or discovered late. → §PP31
- 📋 **PP32** (deps: PP28) **audio decode and resampling are Opus and speexdsp, both native and both on the latency path** — Managed Opus exists and speexdsp has no equivalent, so the two halves of the audio path have different answers and only one of them is a choice. → §PP32
- 📋 **PP33** (deps: PP24) **HTTP and JSON in the core are curl and json-c, two vendored dependencies for what the runtime already does** — HttpClient and System.Text.Json replace them outright, so this is the one part of the core that gets smaller instead of merely moving. → §PP33
- 📋 **PP34** (deps: PP24) **the threading and logging layer exists to paper over pthreads and Win32, an abstraction .NET does not need** — 502 lines that would be deleted rather than translated, and every file in the core calls them, so the shape they take decides how the translated files read. → §PP34

## Block G — Test discipline

- 📋 **PP35** (deps: PP24) **5512 lines of munit tests cover the modules this port rewrites, and nothing in a managed tree runs them** — They carry captured FEC cases and a real video packet, which makes them the closest thing this protocol has to a specification and the cheapest baseline to inherit. → §PP35
- 📋 **PP36** (deps: PP22, PP24) **the only CI job is the roadkeep lint gate, so no test result can turn a branch red** — A suite nobody runs on a push goes red quietly, and Test Explorer is a local convenience rather than a gate that holds a policy in place. → §PP36
- 📋 **PP37** (deps: PP12) **a screen ported into code-behind can only be exercised by opening a window** — What is worth asserting about a screen is its view model, so a port that keeps logic behind the window makes eight screens untestable by construction. → §PP37
- 📋 **PP38** (deps: PP36) **nothing counts the shipped lines that carry no assertion, so the rule is a sentence rather than a gate** — A count that may only go down is what survives a busy week, and without it the discipline lasts exactly as long as the person remembering it. → §PP38

## Block H — Performance and telemetry

- ⏳ **PP46** (deps: PP42 ✅, PP63) **the claim that dropping the bundled browser makes startup and the installer smaller is untested** — A Chromium leaving the build should be visible in cold start and in megabytes, and stating it without measuring is how a port collects folklore. → §PP46
- 📋 **PP61** (deps: PP46 ⏳) **the startup harness labels a warm run cold, because the OS file cache outlives the process** — 3771ms on the first run after a build against 1218ms on re-invocation, and nothing in the report says which cache state produced the number. → §PP61
- 📋 **PP66** (deps: PP48 ⏳) **spike/decode-path writes a result.json naming no adapter, so two runs cannot be told apart by the file** — A measurement whose machine is carried only by its filename is one rename away from meaningless, and spike/video-upscale already records the card and driver it ran on. → §PP66

## Block I — NVIDIA path

- ⏳ **PP47** (deps: PP43 ✅) **DLSS needs motion vectors and a depth buffer, and a decoded video stream carries neither** — The feature that applies to video is RTX Video Super Resolution, not DLSS, and the two are confused often enough that the wrong one gets scheduled. → §PP47
- ⏳ **PP48** (deps: PP41 ✅) **the client already prefers the cuda decoder on an NVIDIA card, and nothing measures whether that helps** — qmlbackend picks it from a card detection with no number behind the choice, so the vendor path is already here and already unevidenced. → §PP48
- 📋 **PP49** (deps: PP11, PP47 ⏳) **the console sends SDR on most titles and an HDR display shows it flat, with nothing in the client trying** — RTX Video HDR does this conversion on the presented frame, and it is the one vendor feature whose benefit is visible on a still image rather than argued from a graph. → §PP49
- 📋 **PP50** (deps: PP40 ✅, PP47 ⏳) **frame generation would smooth a 30fps stream and cost a frame of latency to do it** — Interpolation needs the frame after the one being shown, so it buys smoothness with exactly the quantity remote play is judged on and the trade has to be measured. → §PP50
- 📋 **PP51** (deps: PP48 ⏳) **NVIDIA first has no stated contract for what happens on an AMD or Intel machine** — A vendor path with no declared fallback becomes a vendor requirement by accident, one unmeasured decision at a time, and the users who lose the app never file a report. → §PP51
- 📋 **PP52** (deps: PP32) **the microphone path runs speex echo cancellation on the CPU, and speexdsp has no managed replacement** — NVIDIA ships GPU noise and echo removal for exactly this, so one task can both improve the voice sent to the console and delete a dependency the port has no answer for. → §PP52
- 📋 **PP53** (deps: PP11, PP41 ✅) **frames arrive with network jitter and are presented against a fixed refresh, so each waits for a vblank it missed** — A variable refresh display can show a frame when it arrives rather than when the panel next allows it, which is latency removed and not an image improved. → §PP53
- 📋 **PP72** (deps: —) **the auto decoder order prefers cuda over d3d11va on an OpenGL renderer, and the paced numbers now say the opposite** — PP71 measured cuda slowest of the three at the rate a console sends, so the one fallback that choice governs is picked against its own evidence. → §PP72

## Non-goals

- **No Linux, macOS, Android, FreeBSD or Switch build** Those trees are already deleted
  and the target framework is Windows-only by construction, so a line proposing to keep
  one portable is proposing a second application.
- **No cross-platform UI toolkit as a hedge** Avalonia or MAUI would keep the port
  portable and give back none of the Win32, DXGI and WebView2 access the screens depend
  on, which is the whole reason WPF was chosen.
- **No redesign while porting** A screen that changes shape in the same commit that
  changes framework cannot be judged against the one it replaced, so behaviour is
  reproduced and improvements are filed apart.
- **No line ships without an assertion that fails without it** A test written after the
  fact asserts what the code does instead of what it should do, so it lands in the same
  commit as the line it holds or the line is not shipped.
- **No GPU vendor feature for the network path** Nothing NVIDIA ships touches a UDP
  socket, so the connection is improved by transport work and congestion control or not
  at all, whatever the card is.
