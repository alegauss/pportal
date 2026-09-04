# PP650 — Media Foundation against FFmpeg, priced

```
dotnet run -c Release                       enumerate, print, write result.json
dotnet run -c Release -- release-x.json     write somewhere named instead
```

Exit `0` at least one decoder was found · `1` none was, and there is nothing below to read.

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

## What this does not settle

- **No decode was timed here.** This is an enumeration and an attribute read; the numbers in the
  table above are [spike/decode-path](../decode-path/README.md)'s, taken through FFmpeg. A Media
  Foundation decode has not been measured against them, and the honest comparison needs one.
- **HEVC's decoder is a Store extension.** `HEVCVideoExtension` is installed here and is not on
  every Windows machine. A port depending on it would depend on something a user can uninstall,
  which FFmpeg's own HEVC decoder does not.
- **One machine.** Windows 11 26200, one discrete NVIDIA card. An Intel or AMD machine would show
  the vendor MFT this one does not have, and the H.264 row could read differently there.

## Committed run

[`release-mf-win11.json`](release-mf-win11.json) — the enumeration every row above is read from.
`result.json` and the build output are gitignored: a committed result is one taken deliberately,
not whatever the last invocation left behind.
