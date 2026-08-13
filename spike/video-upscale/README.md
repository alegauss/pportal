# PP47 — RTX Video Super Resolution, and what it costs

```
dotnet build -c Release
bin\Release\net10.0-windows\video-upscale.exe [out.json]
```

Exit `0` the extension engaged and the numbers mean something · `1` it did not, and they do not.

## The feature is not the famous one

**DLSS Super Resolution cannot be applied to a remote play stream, and no amount of scheduling
changes that.** It is a render-time technique: it works because a game hands it motion vectors, a
depth buffer and a jittered camera, and reconstructs a higher resolution frame from information
the renderer already had. What arrives here is H.264 or HEVC that a console encoded, decoded into
a plain surface. There is nothing to hand DLSS and no way to produce it.

The feature that takes a decoded video frame and upscales it is **RTX Video Super Resolution**. It
is reached through the D3D11 video processor — `ID3D11VideoContext::VideoProcessorSetStreamExtension`
with an NVIDIA-defined GUID — rather than through any SDK, which is how browsers use it. VSR takes
input from **360p to 1440p**, so the 1080p this client streams is in range.

The two are confused often enough that the wrong one gets scheduled, and that confusion is the
whole reason this line exists. Settled: DLSS is out, VSR is the candidate.

## What this run measured, and what it did not

| | |
|---|---|
| adapter | NVIDIA GeForce RTX 4060, driver 32.0.16.1074 |
| path | 1920x1080 NV12 → 3840x2160 BGRA through `VideoProcessorBlt` |
| **plain video-processor upscale** | **262.9 µs mean, 274.1 µs p99** per frame |
| **VSR** | **did not engage — no cost measured** |

The 262.9 µs is a real and useful floor: it is what the video processor costs to take a 1080p NV12
frame to 4K with its ordinary scaler, on the card this port targets. At 60 fps that is 1.6% of a
frame interval. Whatever VSR costs, it costs that *plus* something.

**VSR itself produced no number, because it never ran.** The extension was set and the output was
byte-identical to the run without it — 0 of 8,294,400 pixels differ.

VSR *is* installed on this machine — `nvsvsr.dll` (2.0 MB) and `nvvitvsr.dll` (4.3 MB) sit in the
driver store beside `nvngx.dll`. So the feature is present and this spike is not reaching it.

### Why, and what a reader should do about it

**Setting the extension is not the same as turning the feature on.** mpv, whose GUID this is,
documents exactly that of its own `scaling-mode` option:

> Note that this only enables the appropriate processing extensions; whether it actually works or
> not depends on your hardware and the settings in your GPU driver's control panel.

So: **NVIDIA Control Panel → Video → Adjust video image settings → RTX Video Enhancement /
Super Resolution.** With it off, every call here succeeds and nothing happens, which is precisely
what this run shows. Turn it on and run again.

That is a finding rather than a defect, and it belongs to PP51 as much as to this line: a vendor
path that needs a visit to a control panel has a different contract from one that does not. A user
who never opens that panel gets the unaccelerated path and files no report.

Two candidates were considered and dropped:

- **A wrong GUID.** A first draft of this spike carried a different tail from memory. The value
  here is mpv's, corroborated across three independent retrievals — the last three bytes are
  exactly where a remembered GUID goes wrong, and a wrong one fails silently rather than loudly.
- **Offscreen output.** Chromium applies VSR while presenting a swapchain, which suggested the
  driver might require one. mpv's `vf_d3d11vpp` is a filter whose output is an ordinary texture and
  it works, so an offscreen blit is not disqualifying on its own.

A note on what the read-back does and does not prove. `VideoProcessorSetStreamExtension` returns
`void` and cannot refuse, so this spike also calls `VideoProcessorGetStreamExtension` and prints
what came back — `version=0 method=0 enable=0` on this run. An earlier version of this README read
that as "the driver does not recognise this GUID". **That inference was wrong and has been
removed:** Get is driver-defined exactly as Set is, so a driver that recognises the interface is
still entitled to leave the buffer alone. Zeros mean the driver wrote nothing, and nothing more.
The 0-pixel difference is the evidence; the read-back is a hint.

## Two instruments, and both were wrong first

Neither of the two things this spike measures was right on the first attempt, and both were caught
by a check rather than by reading the code.

**The engagement check caught a disabled feature.** The first version called
`VideoProcessorSetStreamAutoProcessingMode(processor, 0, false)`, on the reasoning that automatic
processing would contaminate a measurement. Driver-side enhancement is the mechanism super
resolution rides on, so that call disabled the thing being measured while every other call still
succeeded — an extension that costs nothing and changes nothing, which reads exactly like a
feature that is free. Auto processing is now left on, deliberately, with a comment saying why.

**The timing instrument was measuring the wrong queue.** GPU timestamp queries were the obvious
choice and they do not work here: D3D11 timestamps are taken on the 3D queue and `VideoProcessorBlt`
executes on the video engine. Measured rather than reasoned about — **194 of 200 intervals came
back with the end stamp not later than the begin stamp**, on a run whose disjoint flag was false
and whose frequency was a clean 1 GHz. A pair of stamps that never advances is not a fast blt, it
is a blt the clock being read cannot see.

What replaced it is wall clock over a batch of 25 blts, drained at the end by mapping a staging
copy. That measures throughput rather than one frame's latency, and the distribution is over batch
means — 20 samples, not 500.

**The drain was proven by removing it.** Without it the same run reports **0.2 µs** per frame
instead of 262.9 — a 1300× lie, because `Flush` submits work rather than waiting for it. A timing
harness that reports sub-microsecond GPU upscales is not fast, it is unplugged.

## The frame is synthetic, and that is a real limit

No console is reachable from this machine, so there is no remote play frame to feed the upscaler.
`Frame.cs` builds a deterministic NV12 pattern chosen to be hard in the ways an upscaler is judged
on: near-horizontal edges, aliasing rings, hard HUD-like strokes and a flat gradient where ringing
would show.

This is enough for the **cost**, because VSR is a fixed convolutional network evaluated per pixel
and its time follows the input and output resolutions rather than the content. It is **not** enough
for the **quality** half of §PP47. Whether the picture is better enough at 1080p→4K to pay for it
cannot be answered from a chart, and nothing here claims to have answered it.

`crop-off.png` and `crop-on.png` are 512x512 at 1:1 rather than the whole 8-megapixel frame, from
the wedge quadrant where staircasing shows. On this run they are byte-identical, which is the
result rather than an oversight.

## Committed run

[`release-4060-no-engage.json`](release-4060-no-engage.json) — the run the table above is read
from. `result.json` is gitignored: a committed result is one taken deliberately, not whatever the
last invocation left behind.
