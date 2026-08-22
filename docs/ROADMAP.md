# Roadmap (active backlog)

## Priority

- Block H
- Block I

## Block A — Core

## Block B — Native interop

## Block C — Video and input path

- ⏳ **PP11** (deps: PP9 ✅, PP163) **fullscreen, HDR handoff and refresh-rate switching are handled by the Qt window** — the refresh-rate switching, and the HDR half that PP163 has now shown needs a different presentation path. → §PP11
- 📋 **PP163** (deps: PP9 ✅) **an HDR picture cannot reach the display through D3DImage, which refuses any surface wider than eight bits** — one question left, and it needs a screenshot: PP283 has the compositor taking the tree on a real WPF window, but not what WPF draws over it. → §PP163
- 📋 **PP292** (deps: —) **the frames-lost count subtracts a wrapped sequence number as a plain int, so a loss across the wrap counts -65528** — int32 minus a promoted ChiakiSeqNum16 does not wrap, so frames_lost and frames_lost_total go hugely negative about every eighteen minutes at 60fps. → §PP292

## Block D — Screens

## Block E — Windows-only build

- ⏳ **PP22** (deps: PP1 ✅) **every CI workflow was deleted, so nothing builds, signs or packages the application** — the native side through vcpkg, in CI; the publish landed, and PP273 and PP274 leave a proved payload and an installer over it. → §PP22
- 📋 **PP63** (deps: PP62 ✅) **nothing in the tree can configure a Qt build carrying WebEngine, so PP46's before cannot be produced at all** — MSYS2 has no qt6-webengine and no published Windows release carries Chromium, so an MSVC configure built once is the only reference the port can measure against. → §PP63
- 📋 **PP277** (deps: —) **the installer keeps upstream's AppId, so it upgrades an installed chiaki-ng in place rather than sitting beside it** — The payloads share no file name but chiaki.exe and 34 Qt DLLs are not in the new file list, so they survive the upgrade in {app}. → §PP277

## Block F — Managed core

- ⏳ **PP23** (deps: —) **the protocol has no specification, so a managed rewrite has no oracle except the C code it replaces** — the curl transfers, 420 call sites of them, and a decoder the frame path has no equivalent for yet. → §PP23
- 📋 **PP26** (deps: PP23 ⏳, PP24 ✅) **3242 lines of crypto over OpenSSL sit between a registration and a session** — This is where a translation error is silent: a wrong byte does not throw, it produces a key that fails to open a session with no clue which of eight steps was wrong. → §PP26
- 📋 **PP27** (deps: PP23 ⏳, PP25 ✅, PP44 ✅) **takion, the transport the whole stream rides on, is 1868 lines of C over raw sockets and timers** — It is the layer where a managed rewrite is judged on latency rather than on output, because every millisecond it adds is one the picture is late by. → §PP27
- 📋 **PP28** (deps: PP293, PP294, PP295, PP23 ⏳) **session, ctrl and streamconnection are 3977 lines of state machine with no diagram** — the three together, once PP293, PP294 and PP295 have each landed: what is left here is the ordering between them. → §PP28
- 📋 **PP29** (deps: PP23 ⏳, PP26) **registration and discovery are 1775 lines that decide whether a console can be found and paired at all** — They are the first thing a fresh install runs and the smallest end-to-end proof that a managed core can talk to real hardware. → §PP29
- 📋 **PP30** (deps: PP23 ⏳, PP27) **forward error correction is two vendored C libraries doing Galois field arithmetic per lost packet** — 13 sites and none of them arithmetic: chiaki_fec_decode has one caller left, frameprocessor.c, which PP289 is about. → §PP30
- 📋 **PP31** (deps: PP28) **the video decoder is where 100% managed stops being achievable, and no task above says so** — There is no managed H.264 or HEVC decoder that holds 1080p60 at remote play latency, so this boundary is chosen deliberately or discovered late. → §PP31
- 📋 **PP32** (deps: PP28) **audio decode and resampling are Opus and speexdsp, both native and both on the latency path** — Managed Opus exists and speexdsp has no equivalent, so the two halves of the audio path have different answers and only one of them is a choice. → §PP32
- ⏳ **PP33** (deps: PP24 ✅) **HTTP and JSON in the core are curl and json-c, two vendored dependencies for what the runtime already does** — the deletion, now that PP231 and PP266 closed the two named behaviours: holepunch.c is the last unit needing either library and session.c still calls it. → §PP33
- 📋 **PP293** (deps: —) **session.c is 1182 lines and owns the session lifetime, and PP28 sizes it together with two files it does not resemble** — It is the login, the wakeup and the connect sequence, and it is the only one of the three whose oracle is a captured exchange rather than a running console. → §PP293
- 📋 **PP294** (deps: —) **ctrl.c is 1469 lines of control channel and PP28 sizes it together with two files it does not resemble** — It is the longest of the three and the one with the most message types, and none of them are on the frame path so latency is not the measure. → §PP294
- 📋 **PP295** (deps: PP27) **streamconnection.c is 1326 lines and is the last C caller of the video receiver, so every deletion below waits on it** — PP286 to PP291 ported the frame path bottom-up and none of it removed C, because this is what still calls the native receiver. → §PP295

## Block G — Test discipline

- 📋 **PP36** (deps: PP22 ⏳, PP24 ✅) **the only CI job is the roadkeep lint gate, so no test result can turn a branch red** — A suite nobody runs on a push goes red quietly, and Test Explorer is a local convenience rather than a gate that holds a policy in place. → §PP36
- 📋 **PP38** (deps: PP36) **nothing counts the shipped lines that carry no assertion, so the rule is a sentence rather than a gate** — A count that may only go down is what survives a busy week, and without it the discipline lasts exactly as long as the person remembering it. → §PP38

## Block H — Performance and telemetry

- ⏳ **PP46** (deps: PP42 ✅, PP63) **the claim that dropping the bundled browser makes startup and the installer smaller is untested** — A Chromium leaving the build should be visible in cold start and in megabytes, and stating it without measuring is how a port collects folklore. → §PP46

## Block I — NVIDIA path

- 📋 **PP49** (deps: PP11 ⏳, PP47 ✅) **the console sends SDR on most titles and an HDR display shows it flat, with nothing in the client trying** — RTX Video HDR does this conversion on the presented frame, and it is the one vendor feature whose benefit is visible in a still image, not argued from a graph. → §PP49
- 📋 **PP52** (deps: PP32) **the microphone path runs speex echo cancellation on the CPU, and speexdsp has no managed replacement** — NVIDIA ships GPU noise and echo removal for exactly this, so one task can both improve the voice sent to the console and delete a dependency the port has no answer for. → §PP52
- 📋 **PP53** (deps: PP11 ⏳, PP41 ✅) **frames arrive with network jitter and are presented against a fixed refresh, so each waits for a vblank it missed** — A variable refresh display can show a frame when it arrives rather than when the panel next allows it, which is latency removed and not an image improved. → §PP53

## Done when — PP33

- **Every curl and json-c call site in holepunch.c has a named counterpart** The
  `remaining` query below finds them; each area answered by a class in app/Protocol
  whose source assertions read the same file. What is not yet answered is named in the
  criteria under this one, rather than left for the count to imply.
- **The websocket thread's auto-ACK of offers is stated** The rule is a state mask -
  auto-ACK while a control offer is received and not yet established, or once a data
  offer is - and the `continue` on a parse failure skips the enqueue, losing the
  notification. Both stated, both asserted against the file.
- **The session HTTP calls run through HttpClient rather than curl** The four session
  calls and the wakeup reach PSN through HttpClient, with the response codes and the
  failure paths the curl setup encodes - CURLOPT_FAILONERROR among them, which turns an
  HTTP error into a transfer error and is not the default anywhere else.
- **libchiaki builds with neither curl nor json-c** The build configures and links
  without either, which is the first moment the deletion is real rather than planned:
  until then both are still fetched, still built, and still shipped beside a managed
  replacement that duplicates them.
- **The remaining query reads zero** And not before. The query counts C that porting
  does not remove, so it reads its full count until the criterion above is met and then
  reads zero. It is an end state, not a progress bar, and reading it as one is what made
  four shipped tasks look like none.

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
