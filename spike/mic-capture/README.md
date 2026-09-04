# mic-capture

PP652's first question, asked of the machine.

## The question

`MicrophoneSurface` established that four subsystems assume a microphone and nothing
opens a device, and it named five candidate APIs without choosing between them.
`streamconnection.c:1345` announces the microphone to the console as **one channel,
sixteen bits, 48000 Hz, 480 frames per unit**.

So the question is not whether Windows can capture audio. It is whether a capture device
here hands back that format, because the answer decides two things at once: whether a
conversion stage is owed, and whether a new dependency is.

## How it asks

WASAPI through its own COM interfaces, no package. That is the shape PP650 used for
Media Foundation, and it is deliberate: reaching for NAudio to answer the question would
prejudge the dependency half of it.

Every active capture endpoint is enumerated. For each, the shared-mode mix format is
read, and `IsFormatSupported` is asked whether the announced format is accepted in
shared mode and in exclusive mode. `S_FALSE` counts as a no - it means "not that, but
here is the nearest", which is a no to the question asked.

```
dotnet run -c Release -- result.json
```

## What it found here

`release-wasapi-win11.json`, on Windows 11, .NET 10, 2026-09-04.

| device | mix format | shared | exclusive | autoconvert |
| --- | --- | --- | --- | --- |
| HyperX QuadCast | 2ch 32-bit float 48000 | no | no | **yes** |
| HD Pro Webcam C920 | 2ch 32-bit float 48000 | no | no | **yes** |
| Steam Streaming Microphone | 1ch 32-bit float 44100 | no | yes | **yes** |
| Lenovo thinkplus XT80 (default) | 1ch 32-bit float 16000 | no | no | **yes** |

**No device takes the announced format in shared mode**, and the reason is structural
rather than local: WASAPI shared mode hands back the mix format, and a mix format is
32-bit float. Sixteen-bit PCM is never what a shared capture gives. The default
communications endpoint - the one a microphone path opens - runs at 16000 Hz, so a
resample is implied too, and upward.

**But the port owes neither.** `IsFormatSupported` answers a narrower question than the
one that matters: whether the engine takes the format as it is. `AUTOCONVERTPCM` asks
for a converter to be put in front of it instead, and the flag is invisible to
`IsFormatSupported` - the only way to ask is to `Initialize` and see.

Initialising with `AUTOCONVERTPCM | SRC_DEFAULT_QUALITY` succeeds on **all four
devices**, including the 16000 Hz mono headset and the two stereo microphones. So a
capture client hands back exactly one channel of 16-bit PCM at 48000 Hz, and every
downmix and resample is Windows's.

That inverts the conclusion the first two columns support, which is why the spike
initialises rather than asking.

Exclusive mode does take the format on one device, and it takes the whole device with
it. With autoconvert available there is no reason to pay that.

## The bug this spike had, and what it produced

The first run reported `shared yes, exclusive yes` on one device and failed to read the
other three with `0x88890008`. Both were wrong, and both came from one omission: the COM
interface methods were declared as returning `int` **without `[PreserveSig]`**.

Without it the CLR treats the declared `int` as an `[out, retval]` and the real return as
an HRESULT it converts into exceptions. So every `hr == 0` test was reading an
uninitialised local, and every genuine failure arrived as a thrown exception instead of a
code. The three "unreadable" devices were readable; the one "yes" was a coin toss.

With `[PreserveSig]` on all sixteen methods, all four devices read and the answer
inverts. Recorded because a wrong answer that looks like an answer is the thing a spike
is most likely to produce, and PP650 caught two of the same kind.

## What it does not measure

Latency, glitch rate, or what the conversion costs. Those are questions about a capture
path that exists; this one is about whether the path needs a converter at all. It does.
