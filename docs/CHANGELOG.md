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

## Block I — NVIDIA path

