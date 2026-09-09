# PP650 — Media Foundation against FFmpeg, priced · PP818 — and timed

```
dotnet run -c Release                       enumerate, print, write result.json
dotnet run -c Release -- release-x.json     write somewhere named instead

dotnet run -c Release -- --time             decode, paced at 60 fps, write timing.json
dotnet run -c Release -- --time --unpaced   feed as fast as the decoder accepts
dotnet run -c Release -- --time --frames=N  stop after N access units
dotnet run -c Release -- --time --out=x.json
```

Exit `0` at least one decoder was found, or a decode was timed · `1` neither, and there is nothing
below to read. `--time` reads [spike/decode-path](../decode-path/README.md)'s `stream.h264` and does
not generate one: a second generator would be a second stream, and then the two halves would not be
measured on the same input.

## The question PP31 left open

PP31 settled that the decoder stays native and did not settle **which** native. Its rationale named
FFmpeg and Media Foundation and priced neither, which is the shape §PP163 was criticised for one
subsystem along. This asks the machine.

Three questions, and only the first two need Windows to answer:

1. what Media Foundation offers for H.264 and HEVC here,
2. whether those decode into a D3D11 texture,
3. what the port would give up by leaving FFmpeg — which is countable from the tree.

## What Media Foundation has on this machine

| codec | decoder | registration | D3D11 |
|---|---|---|---|
| H.264 | Microsoft H264 Video Decoder MFT | no hardware URL | **D3D11-aware** |
| HEVC | HEVCVideoExtension | no hardware URL | **D3D11-aware** |

One each, and **both answer yes to question 2**. `MF_SA_D3D11_AWARE` is what lets a decoder be
handed a D3D11 device manager and give its output back as a texture, which is the whole of what the
present path PP319 chose needs from a decoder.

**"No hardware URL" does not mean the CPU decodes it,** and reading it that way is the mistake the
column invites. `MFT_ENUM_HARDWARE_URL_Attribute` marks a vendor's own MFT — Intel ships one. The
Microsoft decoder does not carry it and still decodes on the GPU: being D3D11-aware is what lets it
hand the work to the driver's DXVA. So one transform covers both the hardware path and, given no
device manager, the software fallback.

## Two things this spike got wrong first, and both would have settled it backwards

**`MFT_ENUM_FLAG_TRANSCODE_ONLY` is a filter, not another kind to include.** Passed alongside the
three category flags it reads as widening the search and narrows it, and the first run reported one
software decoder per codec on a machine with a discrete GPU.

**`MF_SA_D3D11_AWARE` is the transform's attribute, not the activate's.** Read off the activate it
is absent from everything, and the first run therefore answered question 2 with a flat no. The
activate is a factory carrying registration data; the flag belongs to the object it makes, so the
object has to be made and shut down. Both mistakes produced a plausible answer rather than an
error, which is why they are recorded here rather than quietly fixed.

## What leaving FFmpeg would cost

Measured from this checkout rather than estimated:

| | |
|---|---|
| `avcodec-61.dll` | 84.3 MB |
| `avutil-59.dll` | 2.7 MB |
| `swresample-5.dll` | 0.6 MB |
| **total in the portable tree** | **87.6 MB across three files** |

And four decoder choices in [`decoderchoice.c`](../../lib/src/decoderchoice.c): `vulkan`, `cuda`,
`d3d11va`, `software`. Media Foundation covers the last two of the four. It covers neither `cuda`
nor `vulkan`, because neither is a Media Foundation concept at all.

## What the numbers already in the tree say about losing those two

[spike/decode-path](../decode-path/README.md) measured all four, paced at the rate a console
actually sends:

| paced total per frame | decode | readback | total |
|---|---|---|---|
| vulkan | ~400 µs | none | **~400 µs** |
| d3d11va | ~300 µs | 2 253 µs | **~2 550 µs** |
| cuda | ~2 100 µs | 793 µs | **~2 900 µs** |

So of the two paths Media Foundation cannot offer:

- **`cuda` costs nothing to lose.** PP71 measured it as the worst of the three when paced, which is
  the reverse of the ordering the Qt client prefers. Nothing wants it.
- **`vulkan` is the one that would hurt**, and only because of a copy. Its whole advantage is that
  the Qt client's snapshot path skips `av_hwframe_transfer_data` for Vulkan frames alone. A D3D11
  texture handed straight to a D3D11 renderer pays no such copy either — so whether that advantage
  survives depends on the renderer this port builds, not on the decoder.

## PP818 — the decode, timed

The line above said no decode was timed here and that the honest comparison needs one. This is it,
on the same `stream.h264` and paced the same way, so the row drops straight into the table:

| paced total per frame | decode | readback | total |
|---|---|---|---|
| **Media Foundation** | **~750 µs** | **~25 µs** (PP828) | **~775 µs** |
| vulkan | ~400 µs | none | ~400 µs |
| d3d11va | ~300 µs | 2 253 µs | ~2 550 µs |
| cuda | ~2 100 µs | 793 µs | ~2 900 µs |

Seven paced runs of 570 counted frames: means 630 to 863 µs, p50s 638–864, p99s 1 345–1 780.
Quoted as a range for the reason [spike/decode-path](../decode-path/README.md) gives — a single
paced run is not worth quoting.

The decode is slower than `d3d11va`'s through FFmpeg — ~750 against ~300 µs, which is the honest
cost — and the question is whether the copy it avoids is worth that. It does avoid `d3d11va`'s
2 253 µs, which is the Qt client pulling every hardware frame back through **system memory**.

> **PP826 corrected this section's readback column, and the correction matters.** It first read
> **none**, argued from the decoder being handed an `IMFDXGIDeviceManager`: a decoded frame is an
> `ID3D11Texture2D`, so a D3D11 renderer samples it where it lies. The texture says otherwise.

**The decoded surface is `D3D11_BIND_DECODER` and nothing else** — bind flags `0x200`, with no
`D3D11_BIND_SHADER_RESOURCE`. It is a DXVA decode surface in an array of 8, NV12, 1920x**1088**:

```
surface   : 1920x1088 format 103, bind 0x200, array 8
sampleable: NO - a renderer must copy first
```

So **a renderer cannot sample the decoder's output**, and there is a copy after all. It is a
GPU→GPU `CopySubresourceRegion` rather than `d3d11va`'s GPU→system readback, so it should be far
cheaper than 2 253 µs — but "far cheaper" is the same kind of argument this correction just
overturned, so PP828 timed it.

### PP828 — and the copy, priced

**~25 µs**, three readings of a 64-copy batch: 25.3, 28.9, 25.1. About **89× cheaper** than
`d3d11va`'s 2 253 µs, which is what a GPU→GPU copy against a GPU→system readback should be — the
argument was right and is now a number.

It is timed in `shim/chiaki_render.c` (`chiaki_render_decode_copy_probe`) rather than here, and
that is deliberate: this spike declares its D3D11 interfaces by counting vtable slots, and
`CreateTexture2D`, `CreateQuery` and `CopySubresourceRegion` are three more of those. A slot
counted wrong reports a copy costing nothing — the exact answer this section already published
once. In the shim the headers count them.

Two things the number is not:

- **Not a Stopwatch.** `CopySubresourceRegion` is queued, not performed, so timing the call
  answers in tens of nanoseconds. This is a D3D11 timestamp pair around the batch, against a
  `TIMESTAMP_DISJOINT` query that says whether the clock ran steadily; a disjoint span is
  reported as a refusal rather than averaged into a number nobody can act on.
- **Not a decode.** The source is built to the description above — NV12, 1920x1088, array of 8,
  `D3D11_BIND_DECODER` — rather than decoded into. A copy's cost follows the two descriptions,
  the extent and the device, not which component last wrote the source. What it does not measure
  is a decoder still writing into the slice, which is a pipeline question and not this one.

### And the unpaced stall is not FFmpeg's

Fed as fast as it will take it, this decoder is **bimodal exactly as PP65 found `d3d11va` to be**:

| Media Foundation | mean | p50 | p99 |
|---|---|---|---|
| as fast as it will take it | 1 325 µs | **180 µs** | **14 500 µs** |
| paced at 60 fps | 745 µs | 750 µs | 1 607 µs |

A median of 180 µs against a p99 of 14 500 is the same two-behaviours shape, through a completely
different API on the same silicon. PP65 concluded it was an artifact of the harness rather than the
driver; this is that conclusion arriving from the other direction, and it is the reason every number
above is paced.

## What this does not settle

- **HEVC is not timed.** Only H.264 is, because `stream.h264` is what exists. The console sends
  either, so the HEVC row is still owed — and it is the one whose decoder is a Store extension.
- **HEVC's decoder is a Store extension.** `HEVCVideoExtension` is installed here and is not on
  every Windows machine. A port depending on it would depend on something a user can uninstall,
  which FFmpeg's own HEVC decoder does not.
- **The copy is timed, and it is a copy and not a pipeline.** PP828 priced the
  `CopySubresourceRegion` at ~25 µs on the GPU's own clock, which closes the readback column. What
  it does not answer is what the copy costs while a decoder is writing the next frame into the same
  array — that is contention rather than bandwidth, and it needs the real present path PP319 chose
  rather than two textures on an idle device.
- **One machine.** Windows 11 26200, one discrete NVIDIA card. An Intel or AMD machine would show
  the vendor MFT this one does not have, and the H.264 row could read differently there.

## Committed runs

[`release-mf-win11.json`](release-mf-win11.json) — the enumeration the first tables are read from.
[`release-mf-decode-4060.json`](release-mf-decode-4060.json) — the paced decode, and
[`release-mf-decode-4060-unpaced.json`](release-mf-decode-4060-unpaced.json) the stall beside it.
`result.json`, `timing.json` and the build output are gitignored: a committed result is one taken
deliberately, not whatever the last invocation left behind.

## One thing this spike got wrong a fourth time

PP818's readback column said **none**, and it was reasoning rather than a reading — the fourth time
in this file that a plausible answer arrived without an error to mark it. PP650 had two; PP818 added
the ComImport one below; and this is the one that would have decided the FFmpeg question wrongly,
because "no readback" was the whole of Media Foundation's margin. The fix was four lines: ask the
decoded sample for its texture and print `BindFlags`.

## One thing this spike got wrong a third time

PP650 recorded two mistakes that each produced a plausible answer rather than an error. PP818 added
a third, and it belongs beside them: **`[ComImport]` interface inheritance does not compose a COM
vtable.** `IMFActivate : IMFAttributes` puts `ActivateObject` at slot 3 instead of slot 33, and the
first call into it returned `MF_E_ATTRIBUTENOTFOUND` — a Media Foundation error code, from a method
that was never reached, on a machine where the decoder enumerates perfectly. Every interface in
[`MediaFoundation.cs`](MediaFoundation.cs) is therefore declared flat, repeating all thirty of
`IMFAttributes`' methods, which is how PP650's half was already written.
