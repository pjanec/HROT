# DEBT-TRACKER — eqs-2

> P2/P3 deferred issues. P1 issues go directly into the next batch (never here).

| # | Priority | Source | Description | Target Batch |
|---|---|---|---|---|
| D-01 | P3 | BATCH-01 review | `EqsCognitiveBuffer_GetSpanRW_NoDefensiveCopy` test uses struct copy semantics, not actual `[InlineArray]` readonly-receiver trap; add a test with `in` or `ref readonly` receiver to cover the real compiler bug | BATCH-03+ |
|---|---|---|---|---|
