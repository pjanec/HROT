# Persistence Unification (BTree/HSM to JSON) — Technical Debt Tracker

**Reference:** [`TASK-TRACKER.md`](./TASK-TRACKER.md) · [`TASK-DETAIL.md`](./TASK-DETAIL.md) · [`BTree_HSM_JSON_Persistence_Detailed_Design.md`](./BTree_HSM_JSON_Persistence_Detailed_Design.md)

> Debt discovered during a batch goes here (P2/P3). P1 never enters the tracker — it becomes Corrective Task 0 of the next batch. Do not delete RESOLVED rows.

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| PU-D01 | BATCH-01 | HSM `FromDto` lives in net8 `Hrot.Hsm.Editor` (uses `HsmAsset`'s internal ctor). The Phase-2 Roslyn generator (netstandard2.0) needs a public factory seam or an ns2.0 HSM builder to construct from the DTO. | P2 | PU-202 | OPEN |
| PU-D02 | BATCH-01 | HSM DTO persists `EventName`, not `EventId` (ids reassigned sequentially on `FromDto`). The JSON load path must match events by name, not id. | P2 | PU-301 | OPEN |
| PU-D03 | BATCH-01 | `HrotDocumentTypes.BTree`/`.Hsm` constants added but NOT registered with the migration system (no `RegisterDocType` passthrough) — intentional for zero-behavior-change; wire when the load/migration path lands. | P3 | PU-301 | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)

---

**Pre-existing baseline (NOT this thread's debt — do not "fix" as regressions):** DEBT-006 (10 Blueprints golden/snapshot), DEBT-008, SpatialHashSystem AV in EditorPreview, ClusterOpE2e DDS crash, flaky sub-80 ns perf (DEBT-014), ~26 pre-existing warnings (DEBT-BCP-004).
