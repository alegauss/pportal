# PP49 — RTX Video HDR, and what it costs

```
dotnet build -c Release
bin\Release\net10.0-windows\video-hdr.exe [out.json]
```

Exit `0` the extension engaged and the numbers mean something · `1` it did not, and they do not.

## What this run measured

| | |
|---|---|
| adapter | NVIDIA GeForce RTX 4060, vendor 0x10de device 0x2882 |
| path | 1920x1080 NV12 BT.709 limited → 1920x1080 R10G10B10A2 ST.2084 BT.2020, through `VideoProcessorBlt` |
| **plain SDR→PQ conversion** | **69.6 µs mean, 68.3 µs p50, 81.5 µs p99** per frame |
| **with RTX Video HDR** | **98.6 µs mean, 98.2 µs p50, 101.3 µs p99** per frame |
| **RTX Video HDR's own share** | **29.0 µs per frame** |
| engagement | **2,073,580 of 2,073,600 pixels differ**, mean \|delta\| 263.2/1023, max 512 |

**It engages.** That is the difference between this line and §PP47, whose extension was accepted and
changed nothing on the same machine and the same afternoon of driver settings. Twenty of the 2.07
million pixels are unchanged and every other one moved, by a quarter of the ten-bit range on
average — this is not a subtle correction, it is a different tone curve.

29.0 µs is **0.17% of a 60 fps frame interval**. The whole conversion including it is 0.59%. Cost is
not what decides this feature.

## The GUID is not §PP47's, and that is the first thing this had to get right

Super resolution and true HDR are **two different driver interfaces**, not two methods on one:

| | GUID | version | method |
|---|---|---|---|
| RTX Video Super Resolution (§PP47) | `d43ce1b3-1f4b-48ac-baee-c3c25375e6f7` | 1 | 2 |
| RTX Video HDR (this) | `fdd62bb4-620b-4fd7-9ab3-1e59d0d544b3` | 4 | 3 |

Both come from mpv's `vf_d3d11vpp.c` — the second from the commit that added `nvidia-true-hdr`,
which is a different commit from the one that added `scaling-mode=nvidia`.

Corroborated across three independent retrievals before a line of this spike was written, which is
§PP47's discipline applied to the trap §PP47 documented. **The first retrieval got it wrong in the
informative direction:** asked for "the NVIDIA stream extension", it returned §PP47's PPE GUID
beside true HDR's struct. A spike built on that would have set an extension the driver knows, been
accepted, changed nothing, and reported §PP47's finding a second time as if it were news.

## What this settles about §PP47's read-back

§PP47 called `VideoProcessorGetStreamExtension` and printed what came back — `version=0 method=0
enable=0` — and its README, having first read that as "the driver does not recognise this GUID",
corrected itself to "zeros mean the driver wrote nothing, and nothing more".

**This run proves the correction.** The read-back here echoes `version=0 method=0 enable=0` too, on
an extension that demonstrably works: 2.07 million pixels moved. So the echo is not a signal in
either direction, and no future spike should spend a line on it. The pixel comparison is the
evidence and the read-back is not even a hint.

## The experiment toggles one thing

**Both runs write ten-bit output with the output colour space set to ST.2084 in BT.2020 primaries.**
Only the extension differs. That matters because the obvious way to write this spike — SDR out
without the feature, HDR out with it — would produce a large pixel difference from the colour
conversion alone, and the engagement check would pass on a run where the driver did nothing.

The 1-suffixed entry points are used for the same reason: the pre-1
`VideoProcessorSetOutputColorSpace` takes a struct with a one-bit nominal range and no way to name
ST.2084 at all.

Auto processing is left **on**, which is §PP47's finding inherited rather than rediscovered:
turning it off disables the mechanism these extensions ride on while every call still succeeds.

## The instrument

Wall clock over a drained batch of 25 blts, 20 batches, 30 warmup — §PP47's instrument, for the
reason it recorded: D3D11 timestamp queries are taken on the 3D queue and `VideoProcessorBlt` runs
on the video engine, and 194 of 200 intervals came back with the end stamp not later than the begin
stamp when that was tried.

**Read the p50, not the mean, on a noisy run.** Six runs were taken before the committed one and
four of them carried outliers of 200–300 µs on both sides — another process reaching the same
engine. The p50s across all six sit at 65–75 µs off and 92–113 µs on, so the delta is stable at
roughly 30 µs however the mean lands. The committed run is one whose p99 is within 20% of its p50
on both sides, which is what makes its mean readable at all.

The **pixel result was identical in all six runs** — 2,073,580 changed, mean 263.214, max 512. That
is the number this line turns on, and it does not move.

## The frame is synthetic, and that is a real limit

`Frame.cs` is linked from `../video-upscale`, unchanged. It is the same deterministic 1080p NV12
pattern §PP47 fed the upscaler, and it was built to be hard in the ways an **upscaler** is judged
on — near-horizontal edges, aliasing rings, hard strokes, and one clean two-axis gradient.

That is right for the **cost**, which follows the resolution rather than the content. It is **not**
what a tone expansion should be judged on: a real frame's benefit from RTX Video HDR depends on
where its highlights and shadows sit, and a synthetic chart has neither in the quantities a game
does. Whether the picture is *better* needs a decoded remote play frame, which needs a console.

`crop-off.png` and `crop-on.png` are 512x512 at 1:1 from the **gradient** quadrant — §PP47's crop
came from the ringing quadrant, which is where an upscaler shows itself and not where a tone curve
does. Both PNGs are eight bits and the comparison is not, so the crops show the shape of the change
and cannot show what the extra range holds.

## What this does not decide

**It does not decide that the feature should ship.** §PP49's own caution stands and is not softened
by the number: an inferred HDR image is an opinion about colour the source did not express, and on
some content it looks worse. Whatever ships is a setting the user can turn off, and a fidelity mode
bypasses it entirely.

It also inherits §PP47's control-panel finding, untested here because the switch was already on
when this ran: a vendor path that needs a visit to NVIDIA Control Panel has a different contract
from one that does not, and the non-goal in `docs/ROADMAP.md` binds any proposal to the floor in
`docs/HARDWARE-CONTRACT.md`.

## Committed run

[`release-4060-engaged.json`](release-4060-engaged.json) — the run the table above is read from.
`result.json` is gitignored: a committed result is one taken deliberately, not whatever the last
invocation left behind.
