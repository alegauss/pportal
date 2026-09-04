# audio-effects

PP52's prior question, asked of the machine.

## The question PP52 did not ask first

`§PP52` proposes NVIDIA's audio effects SDK for echo cancellation and noise removal and
calls it "the first card in this port's audio". Two shipped findings bind that:
`§PP647`'s hardware contract requires a vendor path whose absence is quiet, and `§PP648`
established that a call which succeeds is not a feature that ran.

Both bind a path that exists. Whether it exists is the prior question, and it had not
been asked.

The audio effects SDK is **not part of the display driver**. It arrives with NVIDIA
Broadcast, or as a Maxine redistributable an application ships itself. So a machine can
have the card, a current driver and the vendor's own app and still have nothing to call.

And the comparison is not against nothing. Windows carries a Voice Capture DSP in the
box - `CLSID_CWMAudioAEC`, the transform that has done acoustic echo cancellation and
noise suppression for communications audio since Vista - and whether it is registered is
one registry read.

```
dotnet run -c Release -- result.json
```

## What it found here

`release-audio-effects-win11.json`, on Windows 11, .NET 10, 2026-09-04. Adapters: Intel
UHD Graphics 770 and **NVIDIA GeForce RTX 4060**, driver 32.0.16.1074, NVIDIA App
installed.

| path | vendor | reachable | ships |
| --- | --- | --- | --- |
| NVIDIA audio effects (Maxine) | yes | **no** | the SDK's DLLs and its model files, per effect |
| Windows Voice Capture DSP | no | **yes** | nothing |

The vendor path is **not reachable on a machine with the card**. `NVAFX_SDK_DIR` and
`NVAFX_MODELS_DIR` are unset, `C:\Program Files\NVIDIA Broadcast` does not exist, and a
recursive sweep of both `NVIDIA Corporation` trees finds no audio-effects runtime at all.
What is there is FrameView, NvContainer, the NVIDIA App, telemetry, ShadowPlay and PhysX.

The in-box path is registered in both hives, served by `mfwmaaec.dll` in `System32` and
`SysWOW64`, and both files are present.

## What this settles and what it does not

It settles the shape of the choice. The vendor path is not a feature this port can detect
and use - it is a **redistributable this port would have to ship**, on a card that has
one of the best consumer GPUs available and still does not carry it. That runs against
the direction of every other open dependency line here, which are all about taking
vendored binaries *out* of the package.

It does not settle quality. Nobody has compared the two on recorded audio, and this spike
does not run either one. What it removes is the premise that one of them is free because
the hardware is present.

## What it does not measure

Whether the Voice Capture DSP improves anything on a given microphone, and what it costs
per frame. Both are questions about a path that is integrated; this one is about which
path is available to integrate.
