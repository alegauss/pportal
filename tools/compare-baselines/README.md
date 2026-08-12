# PP45 — the comparison, as one command

```
compare-baselines <before.jsonl> <after.jsonl>
compare-baselines --self-test
```

Reads two `chiaki_baseline.jsonl` files — the sink PP39–PP42 write — and prints the difference per
stage. The last record in each file is used, so pointing it at two log directories compares the
most recent session each build ran.

Exit `0` compared, conditions matched and were verifiable · `2` compared, but the conditions differ
or one record predates them · `1` could not compare.

**The two records need not be the same schema** (PP60). See [Reading an older
record](#reading-an-older-record).

## What it prints

p50, p99 and maximum for each of the six stages of the frame path, side by side, with the delta:

```
  stage         p50 before -> after   delta        p99 before -> after   delta          max before -> after   delta
  receive           43 ->      43       .        60 ->      60       .        60 ->      60       .
  reorder         1100 ->    1151     +51      1100 ->    3071   +1971      1100 ->    4000   +2900
  reassemble      3000 ->    3000       .      3000 ->    3000       .      3000 ->    3000       .
  correct          250 ->     250       .       250 ->     250       .       250 ->     250       .
  decode          4607 ->    4607       .      9000 ->    9000       .      9000 ->    9000       .
  present         1279 ->    2559   +1280      1500 ->    7500   +6000      1500 ->    9000   +7500
```

Unchanged stages are dots, so the two that moved are the two you read. **No verdict is printed.**
A single number saying faster or slower is what people argue about; six distributions are what they
fix. The mean is deliberately absent from the table — it is the number that hides a tail.

`present` is `handoff_us` in the record. It sits outside `stages_us` because it shipped first, and
it is shown last here because that is where it belongs in the frame path.

## Conditions are compared before the numbers

Before and after taken on different resolutions, decoders or bitrates produce a delta that measures
the settings and reads exactly like one that measures the build. So a mismatch is a banner, not a
footnote, and it sets exit code 2:

```
!! CONDITIONS DIFFER - this delta is not only about the build:
     decoder cuda -> d3d11va
   Re-run both builds against the same settings before drawing a conclusion.
```

`app_version` is excluded from that check on purpose: it is the one condition that is *supposed* to
differ between two builds.

## p50 came with this task

The record carried `min/avg/p99/max` per stage through schema 3. §PP45 asks for the median, and the
histogram could already produce it, so `chiaki_session_baseline_stat_p99_us` was generalised into
`chiaki_session_baseline_stat_percentile_us` and every stat now emits `p50` beside `p99` —
**schema 4**. Both are upper bounds read off bucket edges and clamped to the observed maximum, so
neither ever under-reports.

## Reading an older record

The sink appends and never rewrites, so one `chiaki_baseline.jsonl` holds every shape the
application has ever written, in the order the user upgraded. This tool used to refuse everything
but the newest, which made the history unreadable to the one tool built to read it — and the
history is the point. Measured: the only baseline file this machine has ever produced holds a
single schema-1 record, and the tool answered `cannot compare: record is schema 1, this tool reads
4` with exit 1.

**The shape is now detected from the fields present, not from the schema number** — because the
number does not discriminate. `49661e9d` and `34b10cbf` both write `"schema":1`, and the second
added the whole `latency` object without bumping it. A reader keyed on the integer would have to
guess which of the two it was holding.

| shape | `stages_us` | `settings` | `latency` | `p99` | `p50` |
|---|---|---|---|---|---|
| schema 1, `49661e9d` | — | — | — | — | — |
| schema 1, `34b10cbf` | — | — | ✓ | — | — |
| schema 2 | ✓ | — | ✓ | ✓ | — |
| schema 3 | ✓ | ✓ | ✓ | ✓ | — |
| schema 4 | ✓ | ✓ | ✓ | ✓ | ✓ |

What is compared is the intersection, and what fell out of it is printed above the table — a
dropped comparison the reader cannot see is worse than one that never ran:

```
!! PARTIAL COMPARISON - these records are not the same shape:
     stages carried by one record only: receive, reorder, reassemble, correct, decode
     p50 - the median arrived in schema 4, so an older record has none
     p99 - the per-stage percentiles arrived in schema 2
     latency floor - not recorded by one of these records
     conditions one record did not carry, so a match cannot be claimed: decoder, requested bitrate, packet_loss_max
```

Two rules the intersection obeys. A missing percentile prints `n/a`, never `0` — zero is a stage
that took no time, and the two must not read alike. And a condition one record did not carry is
**unverifiable**, not equal: it stays out of `CONDITIONS DIFFER` and still sets exit 2, because
claiming a match nobody measured is the failure §PP45 exists to stop.

`present` (`handoff_us`) is the one stage every shape has ever carried, which is what makes an
oldest-against-newest comparison possible at all.

The table above is history, not a forecast: since PP64 the writer is gated on the same contract.
`test_baseline_field_set_belongs_to_its_schema` pins the emitted key set per schema number, so a
sixth shape cannot arrive under an existing number — which is what produced the two schema 1 rows.

## Assertions

`--self-test` is the assertion this tool ships with. It runs against literal fixtures — two
schema-4 records differing in exactly one stage and one condition, plus the two schema-1 shapes
written out longhand. It checks that the stage which moved is the one reported, that unchanged
stages read as unchanged, that a settings change is named even when every number is identical, that
an older record is read rather than refused, that what could not be compared is named, and that a
record missing a field *every* shape carries is still refused as broken.

It is a `--self-test` flag rather than a test project because there is no managed test runner in the
tree yet (PP36). Every fault below was injected and produced a red run:

- **A wrong format specifier.** `{d,+7}` is *alignment* in C#, not a sign — deltas printed without
  their `+`, so a regression and an improvement looked alike. The self-test caught this while
  writing the tool, which is why the sign is now `"+#;-#;0"` with a comment saying so.
- **The conditions guard disabled** (`Mismatches` returning empty) → 3 checks failed, exit 1.
- **A missing percentile read as `0`** (`Stat.Optional` returning `0` instead of `null`) → `FAIL a
  percentile that was never written must be null, not zero`, exit 1.
- **Stages zipped by index** instead of matched by name → `FAIL the present row must pair against
  the new record's present stage, not against its first one`, exit 1.

The last one is worth recording, because the first attempt at that check **passed while the bug was
live**. It asserted the report did not contain the string `receive`, and an index zip prints the
*before* record's name for the row — so the table read `present` while comparing present against
receive, word for word identical to a correct run. The assertion now reads the pairing rather than
the rendering. A check written against the spelling of a symptom is a check that proves nothing.

## Not verified here

**No real session *pair* has been compared.** No console is reachable from this machine, so no two
sessions from two builds exist to put side by side. The arithmetic, the stage ordering, the
conditions guard and the cross-shape intersection are all exercised against fixtures.

What is no longer untested is reading a file libchiaki actually wrote: PP60 ran this tool against
`%APPDATA%\Chiaki\Chiaki\log\chiaki_baseline.jsonl`, a real schema-1 record from a 97-second
session, and it parsed and compared where it previously exited 1. That file is one record, so it
was compared against itself — which exercises the reader and the intersection, and proves nothing
about a delta.
