# PP45 — the comparison, as one command

```
compare-baselines <before.jsonl> <after.jsonl>
compare-baselines --self-test
```

Reads two `chiaki_baseline.jsonl` files — the sink PP39–PP42 write — and prints the difference per
stage. The last record in each file is used, so pointing it at two log directories compares the
most recent session each build ran.

Exit `0` compared and conditions matched · `2` compared but conditions differ · `1` could not compare.

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

## Assertions

`--self-test` is the assertion this tool ships with, run against two literal schema-4 fixtures that
differ in exactly one stage and one condition. It checks that the stage which moved is the one
reported, that unchanged stages read as unchanged, that a settings change is named even when every
number is identical, and that a record from another schema is refused rather than half-read.

It is a `--self-test` flag rather than a test project because there is no managed test runner in the
tree yet (PP36). Both faults below were injected and produced red runs:

- **A wrong format specifier.** `{d,+7}` is *alignment* in C#, not a sign — deltas printed without
  their `+`, so a regression and an improvement looked alike. The self-test caught this while
  writing the tool, which is why the sign is now `"+#;-#;0"` with a comment saying so.
- **The conditions guard disabled** (`Mismatches` returning empty) → 3 checks failed, exit 1.

## Not verified here

**No real session pair has been compared.** No console is reachable from this machine, so both the
end-to-end run and the self-test use hand-written schema-4 records. The arithmetic, the stage
ordering, the conditions guard and the schema refusal are all exercised; what is untested is reading
a file that libchiaki actually wrote. The fixtures are byte-identical in shape to
`test_baseline_format_line`'s expected output, which is the closest this can get without hardware.
