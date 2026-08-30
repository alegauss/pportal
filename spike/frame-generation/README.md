# PP50 — frame generation, and the frame it would hold

```
dotnet build -c Release
bin\Release\net10.0-windows\frame-generation.exe [out.json]
```

Exit `0` interpolation engaged and the numbers below mean something · `1` it did not, and they do
not.

## The feature is not the famous one, again

**DLSS Frame Generation cannot be applied to a remote play stream**, for the same reason PP47
recorded of DLSS Super Resolution one line over. It is a render-time technique: it works because a
game hands it motion vectors and a depth buffer, from a renderer that already had them. What
arrives here is H.264 or HEVC that a console encoded, decoded into a plain surface. There is
nothing to hand it.

What can interpolate a decoded video frame is the **D3D11 video processor's own frame-rate
conversion**, advertised as `D3D11_VIDEO_PROCESSOR_PROCESSOR_CAPS_FRAME_RATE_CONVERSION` and driven
through `VideoProcessorSetStreamOutputRate`, whose `RepeatFrame` flag is literally the switch
between duplicating a frame and inventing one. That interface is the only way an application can
ask for a decoded surface to be interpolated; whatever a driver does for games outside it is not
addressable from a video path.

## What the driver offers

| | |
|---|---|
| adapter | NVIDIA GeForce RTX 4060, driver 32.0.16.1074 |
| rate-conversion groups | **one**, identical under `PLAYBACK_NORMAL`, `OPTIMAL_SPEED` and `OPTIMAL_QUALITY` |
| its caps | `0x17` — blend, bob and adaptive deinterlacing, inverse telecine |
| **`FRAME_RATE_CONVERSION`** | **absent** |
| frames it requires | 2 past, 1 future |
| custom rates | 5/2, 2/1 interlaced, **2/1 (2 out per 1 in)**, 4/1 interlaced, 4/1, 5/1 |

**The doubling is on the menu and the interpolation is not.** That is the whole finding, and the
two are easy to confuse because the offer that looks like frame generation — one input frame in,
two output frames out — is right there in the custom rate list. What decides whether those two
output frames are *different pictures* is the caps bit beside it, and on this card the bit is
clear.

Asked under all three video usages, in case the feature were withheld from one argument and
offered to another. Same group, same bits, three times.

## What it actually produces

So the run takes the driver up on the 2-out-per-1-in it does advertise, and looks at what comes
back. A 1080p NV12 pattern pans 32 px per frame, and the second output frame is compared against
the two input frames it sits between:

| comparison | pixels differing of 2,073,600 |
|---|---|
| generated vs the frame shown | **0** |
| generated vs the next frame | 1,720,012 (82.95%), mean \|Δ\| 68.15/255 |
| `RepeatFrame` true vs false | **0** |

**The generated frame is a byte-exact copy of the one before it, and the flag that is supposed to
choose otherwise changes nothing.** The frame counter reads 60 and the picture updates 30 times a
second. `crop-shown.png` and `crop-generated.png` are byte-identical files, which is the result
rather than an oversight; `crop-next.png` is where the pattern has moved to.

Costs, for completeness: **53.4 µs mean per output frame** with the flag off, 54.6 µs with it on —
one number, measured twice, because there is only one behaviour behind it. A duplicated frame is
not free; it is a full `VideoProcessorBlt`.

## The price, priced

The driver's own caps say the group needs **1 future frame**. Interpolation cannot begin before the
frame it interpolates towards has arrived, so that one frame is the hold, and at the rate this
feature would be wanted it is worth:

**33.3 ms at 30 fps.**

That is the number §PP50's symptom was written around, and it is larger than most of the budget
this client is arguing about elsewhere: PP40's whole glass-to-glass floor is built out of terms
smaller than one held frame. Frame generation would buy smoothness with a third of a tenth of a
second of latency, in a client whose entire quality argument is latency.

On this card the question does not arise, because there is nothing to buy. **There is no trade to
refuse and no trade to take** — which is a better outcome for the roadmap than either, since it
settles the line without a taste judgement.

## Two instruments, and both were wrong first

**`OUTPUT_RATE_NORMAL` is not the doubling.** The first version of this spike set the content
description to 30 fps in and 60 fps out and left the output rate at `NORMAL`, on the reading that
normal meant "the rate you asked for". It does not — it means one output frame per input frame —
and asking it for output frame 1 returns `E_INVALIDARG`. The doubling is only reachable through the
driver's own enumerated custom rate, which is why the rate list above is read rather than assumed.

**A control that could not fire.** The first check of whether the past and future surfaces reach
the driver processed the same frame twice with different neighbours, as interlaced, on the
reasoning that an adaptive deinterlacer consumes them. It reported zero difference — but the
processor had been created from a *progressive* content description, so nothing was deinterlacing
anything and the control was measuring its own assumption. It was replaced rather than believed.

## The binding lies about one field, and `--prove-arrays` is how that was settled

`D3D11_VIDEO_PROCESSOR_STREAM::ppPastSurfaces` is an **array** of input-view pointers, sized by
`PastFrames`. Vortice types it as a single `ID3D11VideoProcessorInputView` and marshals it by
writing that object's own native pointer into the slot — which hands the driver a COM object where
it will dereference an array. That is wrong for every count, one included.

So this spike builds the array by hand, pins it, and passes it inside a view wrapper whose native
pointer *is* the array's address: a lie about the C# type and the truth about the native field.

An accepted blt does not prove that landed, because a runtime that ignores the pointer accepts
everything. What proves it is nulling the array's elements while the counts still say there are
that many frames:

```
frame-generation.exe --prove-arrays
```

**It dies at `0xC0000005` inside `VideoProcessorBlt`.** The runtime dereferenced the address this
harness supplied and read a null view out of it, which is exactly what it would do with a real
array and cannot do with anything else. The crash is the result, which is why it is behind a flag —
the same shape as video-upscale proving its drain by removing it.

## The frame is synthetic, and that is a real limit

No decoded console frame feeds this. `Frames.cs` builds a deterministic NV12 pattern translating
32 px per frame: hard vertical edges at an irregular pitch, a soft-rimmed disc that crosses block
boundaries, and a fine diagonal grating near the sampling limit.

A rigid horizontal pan is the easiest motion a block-matcher will ever see, which cuts the right
way here: it is the case least likely to defeat an interpolator, and nothing interpolated anyway.
Had the caps said otherwise, this pattern would have been enough for the **cost** and the **hold**
and not for the **quality** — whether a real 30 fps title survives interpolation without artefacts
is not a question a pan can answer.

## Committed run

[`release-4060-no-frc.json`](release-4060-no-frc.json) — the run every number above is read from.
`result.json` is gitignored: a committed result is one taken deliberately, not whatever the last
invocation left behind.
