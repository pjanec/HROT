# Blueprint Integration Finalization — Technical Debt Tracker

> Deferred issues discovered during implementation. **P1 never enters this tracker** — it becomes Corrective
> Task 0 of the next batch. Track P2/P3 here; **do not delete resolved rows** (mark ✅/RESOLVED in place).
> **Companions:** [`TASK-TRACKER.md`](./TASK-TRACKER.md) · [`TASK-DETAIL.md`](./TASK-DETAIL.md) ·
> [`DESIGN-DEBT.md`](./DESIGN-DEBT.md) · [`Blackboard_Authoring_Addendum_v3_ActionParamAuthoring.md`](../../docs/blueprints/Blackboard_Authoring_Addendum_v3_ActionParamAuthoring.md)

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| DEBT-BF-01 | TASK-TRACKER Phase 8 | AN1 vector/Quaternion inline-default literal materialization is skipped; enum defaults assume int-backed underlying type. | P3 | on demand | OPEN |
| DEBT-BF-02 | TASK-TRACKER Phase 8 | DESIGN-DEBT.md DD-1..DD-4: ChannelCommand→per-action generalization (partly done AN4/AN7), rare pin-collapse watch, StructEdit param grid (folds into BB1). | P2 | BB1 | PARTIALLY-ADDRESSED (BB1C): DD-4 StructEdit param grid delivered as the B-3 DefaultValueAuthoring panel. DD-1/DD-2/DD-3 still open. |
| DEBT-BF-03 | TASK-TRACKER L34 | "Open saved `\"Pins\": []` blueprint → compile" load-time pin rehydration: RESOLVED by BP-2 Stage0_Rehydrate per the tracker; re-verify if the load path regresses. | P3 | on demand | OPEN (verify) |
| DEBT-BF-04 | BATCH-BB1A review | HSM `ExpressionTargetField` (type-filtered picker) added to transitions/global-transitions only, NOT states. REVIEW-BB1's surface is "HSM state". A state has 4 action slots (Entry/Exit/Activity/Timer) → the "one DTO → one variable" model needs a per-slot extension; **needs a design call** (architect) before implementing — not a clean autonomous guess. | P2 | needs design decision | OPEN |

Legend:
- **P1** = Critical (never enters tracker; always becomes Corrective Task 0 in next batch).
- **P2** = Should fix (tracked here, assigned a target batch).
- **P3** = Nice to have (tracked here, best-effort).
- Status: OPEN / RESOLVED (do not delete resolved rows).
