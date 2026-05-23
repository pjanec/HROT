# ECS 512-Component Expansion — Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| D001 | BATCH-01-REVIEW | `GlobalComponentIds_NoToolkitBlockDuplicates` uses `f.FieldType == typeof(byte)` — matches 0 fields after byte->int widening; duplicate detection silently disabled. Fix: change to `typeof(int)` and update cast/dictionary accordingly. | P1 | BATCH-02 | OPEN |
| D002 | BATCH-01-REVIEW | `BitMask512` missing `Pack=64` in `StructLayout`. `BitMask256` has `Pack=32`; omitting it from `BitMask512` means AVX2 32-byte aligned loads in the hot path are not guaranteed. Add `Pack=64` before Phase 3 wires BitMask512 into entity storage. | P2 | BATCH-02 | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)
