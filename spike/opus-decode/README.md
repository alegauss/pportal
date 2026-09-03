# PP32 — managed Opus against the native decoder

```
dotnet build -c Release
bin\Release\net10.0-windows\opus-decode.exe [out.json]
```

## What this run measured

| | |
|---|---|
| profile | 48000 Hz, 2 channels, 480-sample (10 ms) frames — what `chiaki_audio_header` carries |
| corpus | 500 frames, encoded **natively** at 96000 bps, 121.2 bytes a frame |
| **native libopus** | **15.8 µs p50**, 16.1 µs mean, 21.8 µs p99 per frame |
| **managed Concentus** | **24.9 µs p50**, 51.2 µs mean, 139.0 µs p99 per frame |
| **the ratio** | **1.58× on the p50** |
| agreement | 386,460 of 480,000 samples differ, **max \|delta\| 15** of 32767 |

**Cost is not what decides this.** A frame is 10 ms. The managed decoder's p50 is 24.9 µs — 0.25% of
one — and even its p99 is 1.4%. Neither decoder is anywhere near a budget, which is the same shape
§PP49 found one subsystem along, and it means the choice turns on the dependency and the audio
rather than on the clock.

## The tail is the finding, not noise

`native libopus` p99/p50 is **1.38**. `managed Concentus` is **5.58** — and across the five runs
taken before this one it was 5.35, 5.37, 5.59, 5.64 and 4.75, against 1.16–1.50 native.

That reproducibility is what makes it a property rather than a contaminated afternoon. §PP645's
limit rejects a run whose p99 sits more than 1.5× its p50, and this run would fail it on the managed
side in every attempt — so `SpikeRunQuality` names it **excluded**, beside `spike/decode-path`, and
asserts that it *would* fail. An exclusion nobody checks is one somebody forgot to remove.

Each sample is a batch of 500 frames divided by its count, so a p99 of 139 µs is one batch in twenty
whose whole run was five times slower. That is a managed runtime doing managed-runtime things —
collection, most likely, since the decoder allocates per call. **For audio the jitter is the number
that matters more than the median**, because a late frame is a gap and an early one is nothing. It is
still two orders of magnitude inside the budget.

## They do not produce the same audio, and the difference is inaudible

386,460 of 480,000 samples differ, and the largest difference is **15 out of 32767** — about 0.05%,
roughly 66 dB down. Identical in all six runs.

Concentus claims bit-exactness against libopus on the **fixed-point** path; this build is evidently
not taking it. So the two are the same algorithm in the same order with a different arithmetic
backend, which is what a difference this small and this uniform looks like. It is reported rather
than asserted: a large difference would have been a fact about this spike, and a zero would have
been a fact worth knowing too.

## The corpus is encoded natively, on purpose

Encoding with the managed library would leave the obvious objection standing — that the packets are
ones only that library likes — and it costs nothing to avoid, because libopus is already in the
tree. `libopus-0.dll` is loaded from `build/chiaki-ng-Win`, the build's own copy, rather than from
whatever is on PATH: a spike that measured a different libopus from the one the port ships would be
answering about a machine.

`OPUS_APPLICATION_RESTRICTED_LOWDELAY` is the encoder mode, because that is what a remote play
stream is.

The signal is two tones an octave and a fifth apart at different levels per channel, plus a slow
sweep. **Not silence**, deliberately: silence compresses to almost nothing and decodes to almost
nothing, which would measure the call overhead and report it as a decoder.

## What this does not measure

**The dependency, which is the other half of the trade** — and when it was counted it changed the
answer. `libopus-0.dll` is 488 KB, linked by `chiaki-lib` alone behind `CHIAKI_LIB_ENABLE_OPUS`. It
has **two** consumers, not one: `opusdecoder.c` on playback and `opusencoder.c` on the microphone.

So porting the decoder removes nothing. The library stays linked for the encoder, the DLL stays in
the package, and what the port would have bought is a decoder that costs 1.58× more and jitters five
times as much *for no saving at all*. The two halves of the audio path move together or neither
moves — and the encoder's half cannot move yet, because §PP32's other criterion is that this host
captures no microphone and there is nothing to encode.

`audiosender.c` reads like a third consumer and is not one. It names its parameter `opus_sender`,
copies it into three buffers, and calls nothing in the library: it carries frames somebody else
encoded. A census taken by grepping for the word gets three. `OpusDependency` counts calls instead,
and `OpusDependencyTests` holds both halves — the two that call, and the one that only looks like it.

**So the measurement decided less than it looked like deciding**, which is the finding rather than a
disappointment: managed Opus is adequate by a factor of 400 on the median, and adopting it for the
decoder alone is a cost with no saving attached.

## Committed run

[`release-managed-vs-native.json`](release-managed-vs-native.json). `result.json` is gitignored: a
committed result is one taken deliberately, not whatever the last invocation left behind.
