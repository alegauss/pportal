# Strategy

What this port is for, and the standing argument behind the choices in
[ROADMAP.md](ROADMAP.md). Stated 2026-09-07 by the project's owner.

The backlog says what is being done and the ledger says what was done. This says why the
answers keep coming out the way they do, so a reader does not have to infer it from
eighteen task lines.

## Delegate to the platform

**Simplify to what Windows already ships, and do not reinvent it.**

This port began as a translation of a client that ran on Windows, Linux and macOS. Every
cross-platform abstraction in it was paid for by that reach — SDL for audio and input,
Qt for the interface, a threading and logging layer over pthreads and Win32. This port
builds for Windows and nothing else, so those abstractions are being maintained for
platforms it refuses to build.

The rule that follows is a non-goal: **no hand-rolled subsystem where Windows already
ships one**. Where the platform has the capability, the port calls it and deletes the
transcription.

What made this concrete was the audio render path. `AudioRing`, `AudioPlayback` and
`WasapiRender.Pump` are `streamsession.cpp`'s ring and drain policy, written for SDL.
They carry, by hand, the arithmetic WASAPI demands: how much room the engine has, how
much of a unit fits in it, what becomes of the rest. PP810 is one line of that
arithmetic going wrong, and it was audible — a click on every unit, growing with the
volume.

Both of PP810's own proposed fixes were measured on a live console and both made the
loss worse rather than smaller: 2374 and 2410 ring evictions against one. That is the
argument in its sharpest form. Repairing a hand-rolled subsystem cost more than deleting
it, and XAudio2 queues PCM buffers with no room to ask about — so the defect cannot be
expressed. PP816 shipped on 2026-09-07 and the click ended, which is the prediction paid
off rather than restated.

**The rule is not only subtraction.** It reads as deleting what Windows already ships,
and those deletions are real — the ring, the drain, the pump, the echo canceller, the
resampler. But delegating also acquires what the port never wrote. XAudio2 brought its
own engine thread and its own buffering along with the deletion; a decoder bound to the
presenter's D3D11 device brings back a texture instead of a copy. Where the platform
offers capability the port has no counterpart for, taking it is the same rule and not a
different one.

## Take the vendor's optimisations

**Use NVIDIA's acceleration where it earns its place, and measure before adopting it.**

The port already evaluates rather than assumes: PP47 ruled DLSS out because a decoded
stream does not carry the inputs it needs, PP48 weighed the CUDA decoder against d3d11va,
PP49 measured RTX Video HDR at 29.0µs a frame. Each is a number in a report, not an
opinion. That method stands — what changes is that a positive result is now expected to be
taken up rather than filed.

Two constraints bind this and are non-goals in their own right. A vendor feature is not
used for the network path. And a vendor path whose absence is visible to the user is
refused: the port runs on a machine without an NVIDIA card, so acceleration is a faster or
cleaner route to the same result, never the only route to it.

## Reproduce behaviour, not structure

The port is checked against the client it replaces, and that check is what makes it
trustworthy. So a subsystem is not improved *while* it is being translated — a screen that
changes shape in the same commit that changes framework cannot be judged against the one
it replaced.

This does not conflict with the first section, and the distinction is the whole of it.
Translating `chiaki_takion_send_message_data_ack` means reproducing what the console
receives, byte for byte, including the parts that read like mistakes. Deleting `AudioRing`
in favour of XAudio2 means removing a subsystem outright, in its own task, with the
behaviour it produced held by assertions either side of the change.

The C in `lib/` is vendored and is not patched locally. Where it is wrong, the port models
the wrongness and says so.

## Prove it against a console

A task about the stream is not finished because the suite is green. The suite has been
green through a session that decoded nothing, and through an audio path that dropped
half of every unit while every counter read healthy.

So the standing expectation is a live run against PS5-385, and a number out of it. Where
what changed is audible rather than countable, the reading is a person listening — which
is how PP815's attribution was found to be wrong after its assertions passed.

## Keep the frame on the GPU

**The copy is the cost, not the decode.** `spike/decode-path` measured every hardware
decoder on this machine finishing a 1080p60 frame within 13% of the others, each inside
9 to 21% of its interval. What separates them is not decode: it is whether the frame
comes back through system memory. That copy is 792.8µs a frame on cuda and 2253.1µs on
d3d11va — more than d3d11va's entire decode.

So the standing rule for the video path is that a decoded frame does not leave the GPU,
and the pieces Windows ships for holding it there are the ones to reach for: a decoder
bound to the presenter's own device through `IMFDXGIDeviceManager`, a transform
reporting `MF_SA_D3D11_AWARE`, `MF_LOW_LATENCY` where the stream carries no reordering,
and a present that hands the compositor a texture rather than a buffer.

This decides architecture, not which decoder wins. PP77's choice stands and the floor in
docs/HARDWARE-CONTRACT.md stands with it. What the rule refuses is a path that downloads
a frame the renderer could already have taken — which is what PP821 found the shim doing
for every hardware decoder, because its branch asks whether a frame is hardware rather
than what format it is in.

Read forward, the same rule is why PP818's measurement is taken on a shared device: an
MF decode timed while copying answers about a configuration nobody would ship.
