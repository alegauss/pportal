# Roadmap (active backlog)

## Priority

- Block H
- Block I

## Block A — Core

## Block B — Native interop

- ⏳ **PP7** (deps: PP4 ✅) **PSN OAuth login runs in a QtWebEngine view, a bundled browser the WPF host has no equivalent for** — the WebView2 control itself, and the token exchange, which need a browser and a PSN account. → §PP7
- ⏳ **PP93** (deps: —) **the dpad touch path uses a third pair of touchpad bounds, matching neither console's pad** — the SDL touchpad path in controllermanager.cpp still scales by the fixed macros, having no session to ask which console is connected. → §PP93

## Block C — Video and input path

- ⏳ **PP9** (deps: PP5 ✅, PP43 ✅) **the video is presented by libplacebo into a Vulkan-backed QQuickWindow, which WPF cannot host** — a decoded frame through pl_render_image; the device, the share, the render and D3DImage all answered. → §PP9
- 📋 **PP10** (deps: PP9 ⏳) **the stream HUD and the in-stream menu are QML drawn over the video and disappear with the renderer** — 1740 lines of overlay assume the compositor they are drawn in, so what replaces the renderer decides whether they are XAML above a surface or drawn into the frame. → §PP10
- 📋 **PP11** (deps: PP9 ⏳) **fullscreen, HDR handoff and refresh-rate switching are handled by the Qt window** — These are the three settings a remote play session is actually judged by, and each is a Win32 or DXGI call the new window has to make for itself. → §PP11

## Block D — Screens

- 📋 **PP13** (deps: PP6 ✅, PP12 ✅) **the console list, the front door of the application, is QML bound to the discovery model** — the screen itself in XAML, its actions and the auto-connect path; the list model landed in PP138. → §PP13
- 📋 **PP14** (deps: PP6 ✅, PP12 ✅) **registration, manual host, console PIN and profile dialogs are QML with their own validation** — the four dialogs in XAML and the flow between them; all four validations are view models now. → §PP14
- 📋 **PP15** (deps: PP7 ⏳, PP12 ✅) **the PSN login and token dialogs are 882 lines of QML wrapped around the embedded browser** — The account link is what remote play outside the local network depends on, and these are the only screens whose content is a third party page. → §PP15
- 📋 **PP16** (deps: PP2 ✅, PP12 ✅) **the settings screen is 2984 lines of QML against 149 properties exposed from C++** — the screen itself in XAML; the property naming and the commit rules landed in PP140 and PP142. → §PP16
- 📋 **PP17** (deps: PP9 ⏳, PP12 ✅) **the renderer tuning and colour mapping screens are 2132 lines of QML over libplacebo options** — Every control on them writes an option that only exists while libplacebo does, so they can only be drawn once the renderer decision has been taken. → §PP17
- ⏳ **PP18** (deps: PP8 ✅, PP12 ✅) **the controller mapping screen is QML bound to the live SDL mapping strings** — A mapping screen is unusable without input arriving from the device being mapped, so it lands with the input path rather than with the other dialogs. → §PP18
- 📋 **PP19** (deps: PP12 ✅) **the confirm, remind, display, steam shortcut and dialog host screens are still QML** — They are small and repetitive, and taking them last means each is drawn in a control vocabulary the earlier screens already settled. → §PP19

## Block E — Windows-only build

- 📋 **PP21** (deps: Block D) **Qt6 is still required to build: Core, Gui, Quick, Qml, Svg, Widgets, Concurrent and WebEngineQuick** — The port is only finished when the toolchain says so, and dropping Qt is the check that no screen or service quietly still depends on it. → §PP21
- ⏳ **PP22** (deps: PP1 ✅) **every CI workflow was deleted, so nothing builds, signs or packages the application** — the native side through vcpkg, and the installer; the publish and its selftest gate landed. → §PP22
- 📋 **PP63** (deps: PP62 ✅) **nothing in the tree can configure a Qt build carrying WebEngine, so PP46's before cannot be produced at all** — MSYS2 has no qt6-webengine and no published Windows release carries Chromium, so an MSVC configure built once is the only reference the port can measure against. → §PP63

## Block F — Managed core

- ⏳ **PP23** (deps: —) **the protocol has no specification, so a managed rewrite has no oracle except the C code it replaces** — the rest of the modules, and the captured session replay that makes a comparison repeatable where a console is needed. → §PP23
- 📋 **PP26** (deps: PP23 ⏳, PP24 ✅) **3242 lines of crypto over OpenSSL sit between a registration and a session** — This is where a translation error is silent: a wrong byte does not throw, it produces a key that fails to open a session with no clue which of eight steps was wrong. → §PP26
- 📋 **PP27** (deps: PP23 ⏳, PP25 ✅, PP44 ✅) **takion, the transport the whole stream rides on, is 1845 lines of C over raw sockets and timers** — It is the layer where a managed rewrite is judged on latency rather than on output, because every millisecond it adds is one the picture is late by. → §PP27
- 📋 **PP28** (deps: PP23 ⏳, PP27) **session, ctrl and streamconnection are 3947 lines of state machine with no diagram** — This is the largest single translation in the core and the one where behaviour lives in the ordering of events rather than in any function worth reading alone. → §PP28
- 📋 **PP29** (deps: PP23 ⏳, PP26) **registration and discovery are 1775 lines that decide whether a console can be found and paired at all** — They are the first thing a fresh install runs and the smallest end-to-end proof that a managed core can talk to real hardware. → §PP29
- 📋 **PP30** (deps: PP23 ⏳, PP27) **forward error correction is two vendored C libraries doing Galois field arithmetic per lost packet** — jerasure and gf-complete are the only vendored code with no managed equivalent to install, so this is a port rather than a swap and it runs on every frame. → §PP30
- 📋 **PP31** (deps: PP28) **the video decoder is where 100% managed stops being achievable, and no task above says so** — There is no managed H.264 or HEVC decoder that holds 1080p60 at remote play latency, so this boundary is chosen deliberately or discovered late. → §PP31
- 📋 **PP32** (deps: PP28) **audio decode and resampling are Opus and speexdsp, both native and both on the latency path** — Managed Opus exists and speexdsp has no equivalent, so the two halves of the audio path have different answers and only one of them is a choice. → §PP32
- ⏳ **PP33** (deps: PP24 ✅) **HTTP and JSON in the core are curl and json-c, two vendored dependencies for what the runtime already does** — the JSON that json-c parses and the curl transfers; the response and request sides both landed. → §PP33
- 💭 **PP105** (deps: —) **chiaki_ecdh_derive_secret is handed the console's signature and the handshake key and reads neither** — the signature is produced in the other direction and checked in none, so it is a check on one side of the exchange only. → §PP105

## Block G — Test discipline

- 📋 **PP36** (deps: PP22 ⏳, PP24 ✅) **the only CI job is the roadkeep lint gate, so no test result can turn a branch red** — A suite nobody runs on a push goes red quietly, and Test Explorer is a local convenience rather than a gate that holds a policy in place. → §PP36
- 📋 **PP38** (deps: PP36) **nothing counts the shipped lines that carry no assertion, so the rule is a sentence rather than a gate** — A count that may only go down is what survives a busy week, and without it the discipline lasts exactly as long as the person remembering it. → §PP38

## Block H — Performance and telemetry

- ⏳ **PP46** (deps: PP42 ✅, PP63) **the claim that dropping the bundled browser makes startup and the installer smaller is untested** — A Chromium leaving the build should be visible in cold start and in megabytes, and stating it without measuring is how a port collects folklore. → §PP46
- 📋 **PP61** (deps: PP46 ⏳) **the startup harness labels a warm run cold, because the OS file cache outlives the process** — 3771ms on the first run after a build against 1218ms on re-invocation, and nothing in the report says which cache state produced the number. → §PP61

## Block I — NVIDIA path

- 📋 **PP49** (deps: PP11, PP47 ✅) **the console sends SDR on most titles and an HDR display shows it flat, with nothing in the client trying** — RTX Video HDR does this conversion on the presented frame, and it is the one vendor feature whose benefit is visible in a still image, not argued from a graph. → §PP49
- 📋 **PP52** (deps: PP32) **the microphone path runs speex echo cancellation on the CPU, and speexdsp has no managed replacement** — NVIDIA ships GPU noise and echo removal for exactly this, so one task can both improve the voice sent to the console and delete a dependency the port has no answer for. → §PP52
- 📋 **PP53** (deps: PP11, PP41 ✅) **frames arrive with network jitter and are presented against a fixed refresh, so each waits for a vblank it missed** — A variable refresh display can show a frame when it arrives rather than when the panel next allows it, which is latency removed and not an image improved. → §PP53

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
- **No vendor path whose absence is visible to the user** First is not only: a machine
  with no NVIDIA card keeps d3d11va decode, a neutral renderer and an SDR present, and a
  feature that is not there is not in the menu rather than explained in a dialog. The
  floor and what actually covers it are in docs/HARDWARE-CONTRACT.md.
