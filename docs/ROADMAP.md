# Roadmap (active backlog)

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

- 📋 **PP9** (deps: PP5) **the video is presented by libplacebo into a Vulkan-backed QQuickWindow, which WPF cannot host** — WPF composes through D3D9 and cannot present a Vulkan swapchain, so the choice between a child window and a shared texture is what every stream screen is then built on. → §PP9
- 📋 **PP10** (deps: PP9) **the stream HUD and the in-stream menu are QML drawn over the video and disappear with the renderer** — 1740 lines of overlay assume the compositor they are drawn in, so what replaces the renderer decides whether they are XAML above a surface or drawn into the frame. → §PP10
- 📋 **PP11** (deps: PP9) **fullscreen, HDR handoff and refresh-rate switching are handled by the Qt window** — These are the three settings a remote play session is actually judged by, and each is a Win32 or DXGI call the new window has to make for itself. → §PP11

## Block D — Screens

- 📋 **PP12** (deps: PP1, PP8) **the seven custom QML controls carry the theme, and gamepad focus navigation is built into them** — Fluent gives the look but no gamepad focus engine, so the thing every screen inherits has to exist before the screens that inherit it. → §PP12
- 📋 **PP13** (deps: PP12, PP6) **the console list, the front door of the application, is QML bound to the discovery model** — It is the first screen a user sees and the first that proves the ported discovery, so it is the smallest slice that can be judged end to end. → §PP13
- 📋 **PP14** (deps: PP12, PP6) **registration, manual host, console PIN and profile dialogs are QML with their own validation** — Registering a console is the step between an installed application and a working one, and its four dialogs are one flow rather than four screens. → §PP14
- 📋 **PP15** (deps: PP12, PP7) **the PSN login and token dialogs are 882 lines of QML wrapped around the embedded browser** — The account link is what remote play outside the local network depends on, and these are the only screens whose content is a third party page. → §PP15
- 📋 **PP16** (deps: PP12, PP2) **the settings screen is 3271 lines of QML against 151 properties exposed from C++** — It is the largest single screen by a factor of three, and the property surface behind it is the real measure of the work rather than the markup. → §PP16
- 📋 **PP17** (deps: PP12, PP9) **the renderer tuning and colour mapping screens are 2132 lines of QML over libplacebo options** — Every control on them writes an option that only exists while libplacebo does, so they can only be drawn once the renderer decision has been taken. → §PP17
- 📋 **PP18** (deps: PP12, PP8) **the controller mapping screen is QML bound to the live SDL mapping strings** — A mapping screen is unusable without input arriving from the device being mapped, so it lands with the input path rather than with the other dialogs. → §PP18
- 📋 **PP19** (deps: PP12) **the confirm, remind, display, steam shortcut and dialog host screens are still QML** — They are small and repetitive, and taking them last means each is drawn in a control vocabulary the earlier screens already settled. → §PP19

## Block E — Windows-only build

- 📋 **PP20** (deps: —) **171 platform conditionals remain in gui, 33 of them macOS and 17 Linux, after those trees were deleted** — Dead branches for platforms that no longer have a build are what makes a port look larger than it is, and every one of them is read at least once. → §PP20
- 📋 **PP21** (deps: Block D) **Qt6 is still required to build: Core, Gui, Quick, Qml, Svg, Widgets, Concurrent and WebEngineQuick** — The port is only finished when the toolchain says so, and dropping Qt is the check that no screen or service quietly still depends on it. → §PP21
- 📋 **PP22** (deps: PP1) **every CI workflow was deleted, so nothing builds, signs or packages the application** — A Windows-only application with no Windows build is a source tree, and claude-tray already has the shape: publish one self-contained exe, then wrap it in an installer. → §PP22

## Block F — Managed core

- 📋 **PP23** (deps: —) **the protocol has no specification, so a managed rewrite has no oracle except the C code it replaces** — Every line in this block is judged by whether a console still answers, and that verdict has to be automatic before the first byte is rewritten. → §PP23
- 📋 **PP24** (deps: —) **the build is CMake with vcpkg, so Visual Studio opens a folder rather than a solution** — A .NET project is an MSBuild project, and until the tree is one, every managed task below is written in an environment that treats it as a guest. → §PP24
- 📋 **PP25** (deps: PP24) **the wire format is generated by nanopb, a C generator with no managed output** — takion.proto is checked in, so the messages are the one part of this core that is regenerated rather than translated, and that makes it the cheapest first slice. → §PP25
- 📋 **PP26** (deps: PP23, PP24) **3242 lines of crypto over OpenSSL sit between a registration and a session** — This is where a translation error is silent: a wrong byte does not throw, it produces a key that fails to open a session with no clue which of eight steps was wrong. → §PP26
- 📋 **PP27** (deps: PP23, PP25) **takion, the transport the whole stream rides on, is 1845 lines of C over raw sockets and timers** — It is the layer where a managed rewrite is judged on latency rather than on output, because every millisecond it adds is one the picture is late by. → §PP27
- 📋 **PP28** (deps: PP23, PP27) **session, ctrl and streamconnection are 3947 lines of state machine with no diagram** — This is the largest single translation in the core and the one where behaviour lives in the ordering of events rather than in any function worth reading alone. → §PP28
- 📋 **PP29** (deps: PP23, PP26) **registration and discovery are 1775 lines that decide whether a console can be found and paired at all** — They are the first thing a fresh install runs and the smallest end-to-end proof that a managed core can talk to real hardware. → §PP29
- 📋 **PP30** (deps: PP23, PP27) **forward error correction is two vendored C libraries doing Galois field arithmetic per lost packet** — jerasure and gf-complete are the only vendored code with no managed equivalent to install, so this is a port rather than a swap and it runs on every frame. → §PP30
- 📋 **PP31** (deps: PP28) **the video decoder is where 100% managed stops being achievable, and no task above says so** — There is no managed H.264 or HEVC decoder that holds 1080p60 at remote play latency, so this boundary is chosen deliberately or discovered late. → §PP31
- 📋 **PP32** (deps: PP28) **audio decode and resampling are Opus and speexdsp, both native and both on the latency path** — Managed Opus exists and speexdsp has no equivalent, so the two halves of the audio path have different answers and only one of them is a choice. → §PP32
- 📋 **PP33** (deps: PP24) **HTTP and JSON in the core are curl and json-c, two vendored dependencies for what the runtime already does** — HttpClient and System.Text.Json replace them outright, so this is the one part of the core that gets smaller instead of merely moving. → §PP33
- 📋 **PP34** (deps: PP24) **the threading and logging layer exists to paper over pthreads and Win32, an abstraction .NET does not need** — 502 lines that would be deleted rather than translated, and every file in the core calls them, so the shape they take decides how the translated files read. → §PP34

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
