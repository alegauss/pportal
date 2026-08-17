# Deferred (set aside, not retired)

## Block A — Core

## Block B — Native interop

## Block C — Video and input path

## Block D — Screens

## Block E — Windows-only build

## Block F — Managed core

## Block G — Test discipline

## Block H — Performance and telemetry

- ⏸ **PP55** (deps: PP40 ✅) **the in-process latency estimate has nothing to check it against, so an honest sum and a wrong one read alike** — set aside (no Reflex-capable display on this machine): A floor built from three client-side terms can be self-consistent and still miss the delay a user feels. → §PP55

## Block I — NVIDIA path

- ⏸ **PP47** (deps: PP43 ✅) **DLSS needs motion vectors and a depth buffer, and a decoded video stream carries neither** — set aside (needs a driver-panel toggle): The feature that applies to video is RTX Video Super Resolution, not DLSS, and the two are confused often enough that the wrong one gets scheduled. → §PP47
- ⏸ **PP50** (deps: PP40 ✅, PP47 ✅) **frame generation would smooth a 30fps stream and cost a frame of latency to do it** — set aside (needs a live session to measure the trade): Interpolation needs the frame after the one being shown, so it buys smoothness with exactly the quantity remote play is judged on. → §PP50
- ⏸ **PP76** (deps: —) **the decoder preference is measured on synthetic frames, and drops under network jitter are what a stream is judged by** — set aside (needs a console): Decode cost follows resolution and bitrate, which a generator carries, but drops follow the network, which no encoder here produces. → §PP76
- ⏸ **PP72** (deps: —) **the auto decoder order prefers cuda over d3d11va on an OpenGL renderer, and the paced numbers now say the opposite** — set aside (needs real sessions): PP71 measured cuda slowest of the three at the rate a console sends, so the fallback it governs is picked against its own evidence. → §PP72
