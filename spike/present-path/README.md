# PP43 — present path spike

A throwaway measurement, kept only so the numbers below can be re-run rather than believed.
**This is not the .NET host.** PP1 builds that; this project is deliberately in no solution.

The renderer task (PP9) chooses between an airspace child window and a shared texture, and its
rationale argues for them on structure. This measures what they cost in milliseconds on this
machine, so that choice is taken against numbers.

## Result

Two runs per path, Release, 600 measured frames each after 60 warmup frames discarded.

| | **A — HwndHost child window** | **B — D3DImage shared surface** |
|---|---|---|
| present, p50 | 239 – 329 µs | **42 – 47 µs** |
| present, p99 | **961 – 1095 µs** | 4998 – 5008 µs |
| present, max | 1421 – 3062 µs | 9128 – 21297 µs |
| frame cadence, p50 | 31 357 – 31 468 µs (**≈31.8 fps**) | **16 685 – 16 727 µs (≈59.9 fps)** |
| frame cadence, p99 | 43 676 – 46 045 µs | 18 462 – 19 569 µs |

**The headline is the cadence, not the present cost, and it goes against the structural
argument.** The shared-surface path holds a steady 60 fps. The child-window path cannot: it
settles at half that, ~32 fps, with a p99 frame interval of 44–46 ms. The path usually assumed
faster because it bypasses the compositor is the one that failed to keep up here.

The present *call* is ~6x cheaper at the median on the shared surface, but its tail is ~5x worse:
p99 of 5 ms against 1 ms. So the two paths fail differently — A is consistently mediocre, B is
usually very fast and occasionally very slow. For a 60 fps stream judged on its worst frame of a
minute, that difference matters more than either median.

## Conditions

NVIDIA GeForce RTX 4060, driver `nvldumdx.dll` 32.0.16.1074. WPF render tier 2 (GPU
composition). Windows 11 Pro 10.0.26200. .NET 10, `net10.0-windows`, x64, Release.
1920×1080 X8R8G8B8 source surface, `PresentInterval.Immediate`, DWM composition on, window
960×540 DIP. Full JSON per run in `release-*.json`.

## What was verified, and what was not

`composed-d3dimage.png` is the WPF composition at the last frame: solid RGB (12, 188, 27), which
is exactly `Device.FrameColour(660)`. The surface really carries per-frame content, so the
timings are not measuring an empty present.

`composed-hwnd-airspace.png` is the same capture for path A and is **black** — the child HWND is
not part of WPF's composition. That is the airspace cost, demonstrated instead of asserted.

Three things are **not** established:

1. **On-screen composition was never confirmed for either path.** Removing `AddDirtyRect`
   entirely still produced a green capture, because `RenderTargetBitmap` reads the live surface
   rather than the composited output. The capture proves the drawing, not the presenting. A
   screen-level capture was attempted and copied the desktop behind the window instead, so
   nothing here has been seen on the glass.
2. **The ~32 fps cadence of path A is measured but not attributed.** Whether `PresentEx` blocks,
   or the WPF render loop is stalled behind it, was not isolated. The number is real; its cause
   is a guess until someone separates them.
3. **Neither path was fed a decoded frame.** The source is filled on the GPU with `ColorFill`,
   which is representative of a hardware-decoded frame (that one does not cross the CPU boundary
   either) and not of a software decode. Feeding a real frame needs the interop boundary of PP4,
   which has not shipped.

One asymmetry is inherent rather than a flaw: path A downscales 1080p to the child window inside
`PresentEx`, while path B renders 1080p and lets the compositor scale. Each scales where that
path naturally would, but they do not scale in the same place.

`AddDirtyRect` is a real cost, not a formality: removing it dropped path B's median present from
47 µs to 10 µs.

## Re-running

```
dotnet build -c Release
bin\Release\net10.0-windows\present-path.exe --path hwnd     --frames 600 --warmup 60
bin\Release\net10.0-windows\present-path.exe --path d3dimage --frames 600 --warmup 60
```

One path per process, so neither warms the other. `--out FILE` names the JSON; the composition
capture is written beside it. Screen captures are gitignored: `BitBlt` copies whatever is on the
desktop at the window's rect, which is not always only this application.

`Stats.cs` keeps every sample and reports exact percentiles — the opposite trade from
`chiaki_session_baseline_stat_p99_us`, which folds an evening into fixed memory. It also reports
`p99_bucketed_us`, computed with the same eight-buckets-to-the-octave scheme as the C record, so
a number from here and a number from `chiaki_baseline.jsonl` can be compared without the
difference in method being read as a difference in the thing measured.
