# PP48 — the NVIDIA decoder the client already prefers, measured

```
run.cmd                 build if needed, generate the stream if absent, measure
run.cmd rebuild         rebuild the harness first
run.cmd restream        regenerate stream.h264 first
```

Exit `0` at least one path decoded · `1` none did, and there is nothing below to read.

## The choice is already made, and it was made without a number

`qmlbackend` picks the decoder before a frame arrives ([qmlbackend.cpp:940](../../gui/src/qmlbackend.cpp#L940)):
with an NVIDIA card and `cuda` among the available decoders, `prefer_cuda` is set, and the auto
path then takes `vulkan` if the renderer is Vulkan, else `cuda`, else `d3d11va`. Nothing behind
that ordering is a measurement — it is a card detection and three `if`s. This spike supplies the
numbers, and the short version is that **the ordering is right and the reason usually given for
it is wrong.**

## What was measured

| | |
|---|---|
| adapter | NVIDIA GeForce RTX 4060, driver 32.0.16.1074 |
| ffmpeg | MSYS2 MinGW64, libavcodec 61.19.101 — the one `compile.cmd` links the client to |
| stream | 1920x1080 H.264 High, 60 fps, ~29 Mbps, no B-frames, 1s GOP, 600 frames |
| counted | 570 frames per path; the first 30 are warmup and are discarded |

Per frame, at a 60 fps interval of 16 667 µs:

| path | decode | send p50 | send p99 | readback | **what the client pays** |
|---|---|---|---|---|---|
| software | 5462 µs | 5320 µs | 8820 µs | — | **5462 µs — 33%** |
| cuda | 1485 µs | 1404 µs | 1649 µs | 792.8 µs | **2278 µs — 14%** |
| d3d11va | 1314 µs | 103 µs | 26990 µs | 2253.1 µs | **3567 µs — 21%** |
| vulkan | 1483 µs | 1415 µs | 2697 µs | *1879.9 µs* | **1483 µs — 9%** |

`decode` is a pass with no readback in the clock at all, so it is decode and nothing else.
`readback` is `av_hwframe_transfer_data` on the frame that just came out, timed in a second pass.
The two are separated deliberately: with the transfer inside the decode loop there is no way to
say which call a hardware decoder synchronised in, and a number nobody can attribute is the
mistake [spike/video-upscale](../video-upscale/README.md) already had to measure its way out of.

## The decode is not where the paths differ

**All three hardware paths decode this stream between 673 and 761 fps — 11 to 13 times real
time.** They sit within 13% of each other, and cuda and vulkan are within 0.1%: 1423.5 µs against
1422.4 µs mean per send. That is not a coincidence and it is the finding. Vulkan Video and NVDEC
are the same silicon on this card, so the API in front of the decode engine does not change how
long the decode takes.

So **"which decoder is faster" is the wrong question.** Every one of them finishes a 1080p60 frame
in under 9% of its interval. Choosing between them on decode speed alone would be choosing on
noise.

Software is the exception worth stating: 5462 µs is 33% of a frame interval, on one core, for
every frame — three times real time, so it holds 60 fps and takes a third of the budget to do it.

## Where they differ is a copy the client makes, and only for some of them

[`make_fallback_snapshot_frame`](../../gui/src/qmlmainwindow.cpp#L2285) copies every hardware frame
out of device memory with `av_hwframe_transfer_data`, and returns early — no copy — for exactly
one format:

```cpp
if (!frame->hw_frames_ctx || frame->format == AV_PIX_FMT_VULKAN)
    return nullptr;
```

It is reached from [`snapshotLastFrame`](../../gui/src/qmlmainwindow.cpp#L3355), which runs **on
every queued frame**. So this is a per-frame GPU→system copy on cuda and on d3d11va, and never on
vulkan. The `readback` column is what that copy costs, and it is the whole difference between the
three paths:

- **cuda** pays **792.8 µs** a frame — 4.8% of the interval, 6.5% at p99.
- **d3d11va** pays **2253.1 µs** a frame — 13.5% of the interval, 15.9% at p99. Nearly **three
  times** cuda's, and it is a larger cost than d3d11va's entire decode.
- **vulkan** pays nothing. The italicised 1879.9 µs above is what it *would* cost if the exemption
  were removed; it is measured here so the exemption's worth is a number rather than a claim.

That answers §PP48's question about a copy back through system memory: **the copy exists, it is
per frame, and it is what the vendor preference is actually buying.** Preferring cuda over
d3d11va saves 1460 µs a frame — 8.8% of a 60 fps interval — and none of that saving is decode.

And it puts the auto ordering on evidence rather than on habit. Vulkan first is right by 35% over
cuda; cuda ahead of d3d11va is right by 36%; and the OpenGL fallback that drops from vulkan to
cuda rather than to d3d11va ([qmlbackend.cpp:946](../../gui/src/qmlbackend.cpp#L946)) is right for
the same reason, since an OpenGL renderer cannot hold the vulkan frame and pays a copy either way.

> **Superseded in part by PP71, below.** Every number in this section was taken feeding the
> decoder as fast as it would accept, which is about twelve times the rate a console sends. At 60
> fps the decode costs change enough to reverse the cuda-over-d3d11va half of that paragraph. The
> readback measurements are unaffected — a copy costs what it costs — and vulkan first is still
> right. Read this section for the copy and the section below for the ordering.

## One number here is not fine — and PP65 found out why

**d3d11va's send is bimodal: a 103 µs median against a 26 990 µs p99.** A submission that usually
takes a tenth of a millisecond and sometimes takes 27 — 1.6 whole frame intervals — is a stall,
not a distribution, and its mean of 1258.8 µs is an average of two behaviours rather than a
description of either. That was filed as PP65 and has now been answered.

```
run.cmd sweep            the pool, hold and pacing sweep behind the table below
```

**It is an artifact of this harness, not of the driver.** Everything above feeds the decoder the
next packet the instant the last one returned — about twelve times faster than a console sends
them. Fed at 60 fps instead, the stall disappears:

| d3d11va | mean | p50 | p99 |
|---|---|---|---|
| as fast as it will take it | 1310.7 µs | 164 µs | **25 124 µs** |
| paced at 60 fps | 300.6 µs | 285 µs | **548 µs** |

Three paced runs gave p99s of 548, 838 and 1310 µs. The stall is gone, and what is left is a
median and a p99 within a factor of four of each other — a distribution rather than two
behaviours. At 60 fps that p99 is 3% of a frame interval.

The pool was ruled out first, because §PP65 named it as the likely cause. `extra_hw_frames` of 0,
4 and 16, crossed with holding 1 and 8 decoded frames out of the pool, moves nothing: every
combination lands between 12 790 µs and 25 124 µs at p99, with total decode wall clock within 3%
of itself. A decoder starved of surfaces would show both moving together, and neither does.

## PP71 — and the ranking above is wrong for cuda

The same sweep showed paced **cuda** behaving worse than the two paths it is preferred over, which
is the reverse of what the table at the top of this file says. That was filed as PP71 and has now
been narrowed.

Each configuration run **alone in a fresh process**, several times, paced at 60 fps:

| paced, alone | runs | p50 range | p99 range |
|---|---|---|---|
| d3d11va | 3 | 225 – 491 µs | **424 – 4 120 µs** |
| vulkan | 3 | 340 – 461 µs | **2 615 – 10 867 µs** |
| cuda | 7 | 1 807 – 2 900 µs | **11 558 – 25 892 µs** |

cuda's median is four to eight times the other two and its p99 is consistently the worst. The
tails are noisy for every path — a single paced run is not worth quoting, which is why these are
ranges over repeats rather than one committed number.

**Contamination is ruled out.** The first measurement had the paced runs last, after seven other
configurations had built and torn down decoders in the same process. Run alone in a fresh process
the numbers are the same or worse, so the order is not what produces them.

**Idleness is implicated, and not proven.** Pacing leaves the decode engine idle for about 90% of
each interval, and `nvidia-smi` sampled during a paced run shows the clocks falling with it —
video 1 995 → 1 350 MHz, SM 2 460 → 330 MHz, at 1–9% utilisation. That is the condition a remote
play client runs in and a benchmark never does. What stops this being a proof: the median moves
only 13% while the p99 moves six-fold, which looks more like the cost of waking back up than like
steady slow clocks. Pinning the clocks and re-measuring would settle it and needs privileges this
run did not have.

### What this changes

**"cuda ahead of d3d11va is right by 36%" — written above from the unpaced numbers — does not
survive.** Adding each path's decode to the readback the client pays for it, at the feed rate the
client actually uses:

| paced total per frame | decode | readback | total |
|---|---|---|---|
| vulkan | ~400 µs | none | **~400 µs** |
| d3d11va | ~300 µs | 2 253 µs | **~2 550 µs** |
| cuda | ~2 100 µs | 793 µs | **~2 900 µs** |

Vulkan first is still right, and by more than the unpaced numbers suggested. Preferring cuda over
d3d11va is not: the readback saving that justified it is smaller than the decode cost it takes on.
The client only reaches that choice with an OpenGL renderer ([qmlbackend.cpp:946](../../gui/src/qmlbackend.cpp#L946)),
so what is at stake is that one fallback rather than the common path.

## What this does not cover

- **The stream is synthetic.** No console is reachable from this machine, so `make-stream.sh`
  encodes 1080p60 at the bitrate the session baseline records rather than capturing a PS5's
  output. Decode cost follows resolution, profile and bitrate rather than content, so the cost
  transfers. **A dropped-frame count under real network jitter does not, and none is claimed** —
  §PP48 asks for one and this spike does not answer it.
- **`result.json` does not record the adapter**, only the ffmpeg version, so a run from another
  machine is identified by its filename and this README rather than by the file. That is a gap in
  the instrument, not in the numbers.
- **HEVC is not measured.** The console sends H.264 or HEVC and only H.264 is here.

## Committed run

[`release-4060.json`](release-4060.json) — the run every number above is read from. `result.json`,
`stream.h264` and `decode-path.exe` are gitignored: a committed result is one taken deliberately,
not whatever the last invocation left behind.
