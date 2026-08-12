# Shipped Ledger

## Block A — Core

## Block B — Native interop

## Block C — Video and input path

## Block D — Screens

## Block E — Windows-only build

## Block F — Managed core

## Block G — Test discipline

- ✅ **PP54** **the vendored munit does not compile on gcc 16, so the only test target in the tree cannot be built at all** — munit is a pinned submodule, so this repo's CMake now builds that one file as the C11 it was written for, and chiaki-unit compiles on gcc 16 with 113 of 113 passing.

## Block H — Performance and telemetry

- ✅ **PP39** **the numbers the stream already computes are drawn on screen and never recorded, so the Qt build leaves no baseline** — The counters the stream already computes are now written as one JSON line per session to chiaki_baseline.jsonl in the log dir, timestamped and held by 8 munit assertions.
- ✅ **PP40** **glass to glass latency is not among the numbers measured, and it is the one users judge on** — The record now carries a latency floor: input queueing plus the console's reported round trip, read as milliseconds against ICMP, plus the decode-to-present handoff, held by 4 munit assertions.
- ✅ **PP41** **only one stage of the frame path is timed, so a slower build says which build and never which stage** — Receive, reorder dwell, reassemble, FEC correct and the decoder's send-to-pull are each timed now, as min/max/mean plus a p99 from a fixed log histogram, held by 6 munit assertions.
- ✅ **PP42** **telemetry has no sink: the counters live in the window and end with it** — The row now names the configuration that produced it - decoder, requested bitrate, both loss knobs - and its field set is closed against an address ever entering it, held by 11 munit assertions.

## Block I — NVIDIA path

