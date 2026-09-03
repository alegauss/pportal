# What this client requires of a GPU

PP51. The direction of Block I is that NVIDIA is where the tuning goes — the decoder that gets
measured, the upscaler that gets integrated, the path that gets a number. This file is the other
half of that sentence, written down so it is a commitment rather than an intention: **first is not
only.** A Windows machine with Intel graphics is an ordinary laptop, and an application that fails
on one has not shipped, it has narrowed.

## The floor

Four things must keep working on a machine with no NVIDIA card at all. They are not aspirations;
a change that breaks one of them is a regression whether or not anything else improved.

**A block heading is a schedule, not a requirement.** §PP647 is why that sentence is here: Block I
is titled "NVIDIA path" because it is the push that was put second on instruction, and roadkeep.toml
already noted that two of its lines were never image quality. §PP53 is one of them and it is
vendor-neutral outright — which is now a row below rather than a note, because a reader scanning the
heading would have taken a latency win as gated on hardware it does not need.

| Must keep working | Where it is decided today |
|---|---|
| **Hardware decode via d3d11va** | [`qmlsettings.cpp`](../gui/src/qmlsettings.cpp) lists `vulkan`, `d3d11va` and `cuda`, each filtered by `hwDecoderRuntimeAvailable`. The choice itself is [`chiaki_decoder_choice`](../lib/src/decoderchoice.c): cuda only when the window reports an NVIDIA card *and* ffmpeg lists it; otherwise vulkan, then d3d11va. **This row is the one with a test.** [`test/decoderchoice.c`](../test/decoderchoice.c) asserts it on the machine it is written for - no NVIDIA card, an OpenGL window, d3d11va listed - and dropping the d3d11va arm turns the suite red (§PP77). |
| **A vendor-neutral renderer** | Vulkan is the default and OpenGL is the fallback when Vulkan initialisation fails ([`qmlmainwindow.cpp`](../gui/src/qmlmainwindow.cpp)). Neither is an NVIDIA path, and the fallback is taken on the driver's answer rather than on the vendor. |
| **A present that can tear, with no vendor extension** | Variable refresh is not an NVIDIA feature and PP53 measured it as DXGI's: `DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING` with `DXGI_PRESENT_ALLOW_TEARING`, which is the same pair FreeSync and VESA Adaptive-Sync answer to. **This row has a test.** `VendorNeutralPresentTests` reads every file of the render shim — the swapchain probes, the DirectComposition trees, both tearing probes — and fails if any of them names a GPU vendor at all, with `chiaki_shim.c` as the control that shows the check can find one (§PP647). |
| **An SDR present with no NGX** | Nothing in the tree loads NGX, and the present path asks for neither vendor feature. Both are now priced and neither is integrated: RTX Video Super Resolution did not engage on this card ([`spike/video-upscale`](../spike/video-upscale), §PP47), and RTX Video HDR does engage, cheaply ([`spike/video-hdr`](../spike/video-hdr), §PP49). A feature that is measured is still not a feature that is shipped, and this row is about the second. |

## What absence looks like

**Nothing visible except that the option is not offered.** A machine without cuda does not see cuda
in the decoder list, because the list is built from what ffmpeg reports and what the runtime can
actually open. There is no error, no warning, and no degraded mode to explain — a feature that is
not there is simply not in the menu.

The same rule binds every vendor feature added later: the absence path is the *quiet* one. A dialog
saying an NVIDIA card would be better is a requirement with a friendly voice.

**And there is a third shape, which §PP648 measured rather than predicted.** A driver feature can be
present, offered, and switched off in the vendor's own control panel — and then the option *is* in
the menu, every call succeeds, and nothing happens. RTX Video Super Resolution does not engage on
this RTX 4060 ([`spike/video-upscale`](../spike/video-upscale), §PP47 and §PP648) while RTX Video
HDR does ([`spike/video-hdr`](../spike/video-hdr), §PP49) — same card, same driver, same API. So the
toggles are per feature.

That binds anything built on one of them: **a vendor feature that reports success is not a vendor
feature that ran.** Whatever ships has to read back the effect rather than the return code, the way
both spikes compare pixels, or the setting will claim to be on for users whose panel says otherwise.

## What the evidence actually covers

Worth stating plainly, because the tables in `spike/` read stronger than they are:

- All three hardware decode paths are measured, not just cuda (§PP48, ledger), and the paced
  numbers put cuda **last** of the three (§PP71). The non-NVIDIA path is not the unmeasured one.
- Every one of those runs was taken on the same machine — an RTX 4060, named in each committed
  result since §PP66. **`d3d11va` has been measured on an NVIDIA card running d3d11va, never on
  Intel or AMD silicon.** There is no such machine here.
- The two NGX evaluations say nothing about the floor either way, and are listed above only so the
  row is not read as "nobody looked". Neither ran on non-NVIDIA silicon and neither could: an
  absent driver extension is the case they would be measuring, and its cost is zero by definition.

So the floor above is a contract held by code review, not by measurement. Saying otherwise would be
the folklore this block exists to refuse.

## What the gate runs on

Today: nothing. [`.github/workflows/roadkeep.yml`](../.github/workflows/roadkeep.yml) is the only
workflow in the tree and it lints the roadmap documents. There is no CI build, no CI test run and
no GPU of any vendor in CI — §PP22 and §PP36 are the lines that change that.

When they do, this is the requirement they inherit: **a vendor path that is the only one with a test
is a vendor requirement with extra steps.** Whatever the gate ends up running, the non-NVIDIA
decode path is in it.
