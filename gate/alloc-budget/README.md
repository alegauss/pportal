# PP44 — the allocation budget

**Bytes allocated per packet processed: 0.** Not a target, a measurement of what the code being
replaced already does.

A managed transport that allocates per packet turns thousands of small packets a second into a
collection under load, and the symptom is the worst frame of a minute rather than the average —
invisible to every check that watches a mean. So the budget is a test, and it fails when the
number rises.

## The two halves

| | where | what it does |
|---|---|---|
| the number | [`test/allocbudget.c`](../../test/allocbudget.c) | counts every `malloc`/`calloc`/`realloc` inside libchiaki while replaying the captured packet through parse → reassemble → flush |
| the gate | this project | replays the same bytes through a managed reference parse and fails above the budget |

Both replay the **same** captured video packet, so the two numbers are comparable. The C side's
copy is the source of truth and `Capture.cs` is generated from it.

### Why the number is 0

After the first frame the C receive-and-reassemble path allocates nothing per packet: `unit_slots`
and `frame_buf` are sized once from a field inside the frame's own payload and then reused. Only
FEC reconstruction allocates, and only on frames that lost a unit. Measured over 200 frames ×
7 source units = 1400 packets: **0 bytes, 0 allocator calls.**

That makes the budget strict and defensible at the same time. The bar is not "allocate little", it
is "allocate nothing", because that is what exists today.

## Running it

```
dotnet build -c Release
bin\Release\net10.0\alloc-budget.exe        # exit 0 = held, 1 = broken
```

The C half runs in the existing suite: `ctest` in `build/`, or
`build\test\chiaki-unit.exe /chiaki/alloc_budget`.

Note the C half needs `-fno-builtin-malloc/-calloc/-realloc` on **both** `chiaki-lib` and
`chiaki-unit` (see their `CMakeLists.txt`). Without it on the library, the compiler emits builtin
allocation calls the linker never sees as undefined references, `--wrap` redirects nothing, and the
gate passes while blind — see below.

## What is under test, and what replaces it

`TakionAvHeader` is a **reference** parse, deliberately partial: it reads the fields
`test/takion.c` already asserts for this capture, enough to prove the real bytes were read, and
stops. Reimplementing the whole AV header would be writing PP27's parser inside PP44, and PP44 is
filed before PP27 so the budget does not wait on the transport.

PP27 replaces `TakionAvHeader` and its caller. What PP27 must **not** do is relax the assertion in
`Program.cs`. A gate that only appears once the thing it gates exists is a gate written to pass.

## Both halves were proven by a red run

A budget that has only ever passed has proven nothing, so each half was made to fail on purpose:

- **C half.** A `malloc(64)` added to `chiaki_frame_processor_put_unit` → `alloc_bytes / packets
  <= 0` failed with `64 <= 0`. Exactly the injected size, so the counter reaches inside libchiaki.
- **Managed half.** A `payload.ToArray()` added to the per-packet work → exit 1 with `184 bytes per
  packet exceeds the budget of 0`. That is the 153-byte payload plus array overhead.

The first attempt at the C half **passed while blind**, and that is worth recording: with
`--wrap` applied but `-fno-builtin` missing on the library, the injected `malloc(64)` was not
counted and the test reported 0 bytes per packet. The `counter_sees_allocations` case exists
because of it — it asserts the instrument works before the instrument is trusted, and it is the
reason the blindness was found instead of shipped.
