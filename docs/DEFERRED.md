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
