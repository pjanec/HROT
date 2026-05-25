# DEBT-TRACKER — eqs-2

> P2/P3 deferred issues. P1 issues go directly into the next batch (never here).

| # | Priority | Source | Description | Target Batch |
|---|---|---|---|---|
| D-01 | P3 | BATCH-01 review | `EqsCognitiveBuffer_GetSpanRW_NoDefensiveCopy` test uses struct copy semantics, not actual `[InlineArray]` readonly-receiver trap; add a test with `in` or `ref readonly` receiver to cover the real compiler bug | BATCH-03+ |
| D-02 | ✅ RESOLVED | BATCH-13 review → fixed BATCH-14 | `CheapLineOfSightTest` now sets `FlagsMeaningful \|= 1` on both exposed (rejected) and covered paths. | — |
| D-03 | P3 | BATCH-13 report | `Action_MaintainEqsSensor` initial-create and update paths are separate code blocks that must be kept in sync manually. A helper building `EqsSensor` from `EqsParams` would eliminate duplication risk. | future |
