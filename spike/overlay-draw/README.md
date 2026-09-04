# overlay-draw

PP641's first option, priced.

## The question

PP319 chose a compositor tree: a ten-bit swapchain below, an eight-bit premultiplied
surface above, composed in that order. PP322 read it and confirmed the two compose.
What draws PP10's XAML HUD into the upper surface is unbuilt, and PP641 names three
shapes without pricing any:

1. render the visual tree to a bitmap per frame and upload it, "paying a full-screen
   copy at HUD update rate";
2. keep the HUD in WPF and accept SDR while it is up;
3. rebuild the HUD against the compositor, costing PP10 and PP12 a second time.

Only the first is a number a machine can produce. This produces it, and in doing so
tests the premise inside it.

## The premise it tests

A composition visual carries its own size and offset. Nothing requires the overlay
surface to be the size of the video plane, and PP10's HUD is not a plane - it is a
corner of text. If that holds, the first option's cost is a small bitmap at a low rate,
not a full-screen one at a high rate, and the three options are not the three PP641
describes.

So both are timed: the HUD's own bounds, and the same tree stretched across a full 1080p
and 4K surface.

## Run it

```
dotnet run -c Release -- result.json
```

WPF, STA, no window shown. `RenderTargetBitmap` is allocated fresh per iteration, which
is the pessimistic case - the real path would reuse one - and one warm-up pass is
discarded so the glyph path and font cache are not charged to the steady state. Sixty
iterations, median and p90 by nearest rank.

## What it found here

`release-wpf-hud.json`, on Windows 11, .NET 10, 2026-09-04.

| shape | pixels | render median | render p90 | copy median | bytes |
| --- | --- | --- | --- | --- | --- |
| the HUD's own bounds | 156x138 | 128 us | 156 us | 2 us | 86,112 |
| a full 1080p plane | 1920x1080 | 4,757 us | 4,997 us | 352 us | 8,294,400 |
| a full 4K plane | 3840x2160 | 18,390 us | 19,345 us | 2,565 us | 33,177,600 |

The HUD asks for **156x138**. That is the whole finding.

At 60 fps a frame is 16,667 us. Rendering the tree across a 4K plane takes 18,390 us,
which is more than an entire frame - so option 1 **as PP641 describes it** is not merely
expensive, it cannot be done at all. At the HUD's own bounds the same work is 128 us,
which is 0.77% of one frame, and the HUD updates at stats rate rather than frame rate.

The premise was the expensive part of the option, and it was not a property of the
option.

## What it does not measure

The upload from the managed buffer into a D3D11 or composition surface. That is a
different call on a different device. PP650 measured a full-frame system-memory copy at
2,253 us, which is the order the 4K row's copy column agrees with, so the two readings
are consistent about what a plane-sized transfer costs.

## Two things worth knowing before trusting the numbers

**The size is measured, not assumed.** The HUD's 156x138 is what the visual tree asks
for through `Measure`, with five stat lines in 16pt Consolas and a 12px margin. A HUD
with more lines is larger, and linearly so - but three orders of magnitude of headroom
do not disappear into a few more rows of text.

**Fresh bitmap per iteration.** Reusing a `RenderTargetBitmap` is faster and is what the
port would do. Timing the allocation keeps the number reachable rather than aspirational.
