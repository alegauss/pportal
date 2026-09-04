# Decisions

## Block A — Core

## Block B — Native interop

## Block C — Video and input path

- ✅ **PP641** **PP10's HUD is XAML, and the compositor tree PP319 chose covers a WPF window's own drawing entirely** — The overlay surface is sized by what the HUD measures, never by the video plane: a composition visual carries its own size, and rendering the tree across a 4K plane costs more than a whole frame.

### §PP641 The overlay surface is the HUD's size

PP641 named three shapes for drawing PP10's XAML HUD into the overlay layer PP319 chose,
and priced the first as "a full-screen copy at HUD update rate". That sentence is what
made the option expensive, and it was not a property of the option.

A composition visual carries its own size and offset. Nothing requires the overlay
surface to match the video plane, and the HUD is a corner of text. `spike/overlay-draw`
measured the same visual tree at both sizes on this machine, sixty iterations after a
discarded warm-up.

| shape | pixels | render | copy | share of a 60fps frame |
| --- | --- | --- | --- | --- |
| the HUD's own bounds | 156x138 | 128 us | 2 us | 0.8% |
| a full 1080p plane | 1920x1080 | 4,757 us | 352 us | 30.7% |
| a full 4K plane | 3840x2160 | 18,390 us | 2,565 us | 125.7% |

The described option does not fit in a frame at all. The actual option costs under one
percent of one, at a rate far below frame rate, because stats update about once a
second.

The other two were priced without a machine. Accepting SDR has no time cost and PP319
already rejected it. Rebuilding against the compositor costs PP10 and PP12 again: their
four commits wrote 2,126 lines across 24 files, which is 4.3 times Block C's p90.

So the rule is the surface size, and `OverlayDraw.SurfaceSizeFor` is where it lives. Its
test measures a real visual tree rather than repeating the number, and the timings above
are read from the spike's committed file rather than typed.

## Block D — Screens

## Block E — Windows-only build

## Block F — Managed core

- ✅ **PP33** **HTTP and JSON in the core are curl and json-c, two vendored dependencies for what the runtime already does** — holepunch.c stays as unbuilt source, gui/'s answer after PP598: ~20 managed models are held against it, and deleting it turned 28 assertions red and would have silenced more quietly.

## Block G — Test discipline

## Block H — Performance and telemetry

## Block I — NVIDIA path

- ✅ **PP76** **the decoder preference is measured on synthetic frames, and drops under network jitter are what a stream is judged by** — PP48's copy ranking does not carry: this port downloads every vulkan frame to NV12 for D3D11, so vulkan's no-copy path is not the one a live session measures.

## Block J — Public documentation
