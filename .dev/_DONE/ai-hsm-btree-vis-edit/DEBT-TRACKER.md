# Blackboard Authoring — Technical Debt Tracker

> Deferred issues discovered during implementation. P1 issues never enter this tracker — they become Corrective Task 0 of the next batch. Track P2/P3 here; do not delete resolved rows.
> **Companions:** [`TASK-TRACKER.md`](./TASK-TRACKER.md) · [`TASK-DETAIL.md`](./TASK-DETAIL.md) · [`Blackboard_Authoring_Detailed_Design.md`](./Blackboard_Authoring_Detailed_Design.md)

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| DEBT-01 | BATCH-02 review | HSM `[HsmAction]`/`[HsmGuard]` use `void*` parameters; schema exporter cannot extract DtoType for real HSM actions (only `[SharedAiAction]` companion methods work). HSM-only action types invisible in picker. | P2 | BATCH-03 | RESOLVED (BATCH-03): added `DtoType` property to `HsmActionAttribute` + `HsmGuardAttribute`; exporter falls back to attribute DtoType when `ExtractFirstRefParamType` returns null |
| DEBT-02 | BATCH-04 review | Window `BuildViewModel` uses `Type.Name` (CLR name, e.g., "Single" for float) instead of C# alias ("float"). Design BB §4.1 shows alias names. Fix: expose a shared alias helper and use it in `VariableViewModel.TypeName`. | P3 | BATCH-05 | RESOLVED (BATCH-05): `BlackboardTypeHelper.GetDisplayName` added; window updated |

| DEBT-03 | BATCH-08 review | `AliasMutableAsset` test stub alias bodies duplicated in 4 AiShared test stubs; extract shared test base or helper to cut ~40 lines of redundancy. | P3 | BATCH-09+ | OPEN |
| DEBT-04 | BATCH-08 review | `DrawClientArea` alias badge uses `TableSetColumnIndex(0)` to jump back to name column; Dear ImGui tables are forward-only in some configs — validate after any ImGui upgrade. | P3 | BATCH-09+ | OPEN |
| DEBT-05 | BATCH-09 report | `CountNodesReferencingVariable` returns 0 on all concrete assets (stub-like). Unused-variable detection is only as accurate as real graph traversal. Needs real implementation when node-graph wiring is complete. | P2 | BATCH-10+ | RESOLVED: BTreeAsset already traverses _nodes via ExpressionTargetField; was not a stub |
| DEBT-06 | BATCH-09 report | Removing a variable from asset A does not notify assets B/C that hold alias bindings pointing to A's variable. Cascade invalidation needed when cross-asset alias tracking is added. | P2 | BATCH-10+ | PARTIALLY-ADDRESSED (BATCH-10): `PruneStaleAliasBindings(GetKnownSubAssetIds())` added to both BehaviorTreeAsset and HsmAsset; window calls it each frame before BuildViewModel. Full catalog-wide cascade (notifying B from A's change) deferred to future batch when asset-catalog eventing is wired. |
| DEBT-07 | BATCH-09 report | `Unsafe.As<T, T>` in generated orchestrator is a same-type no-op cast; if a layout change silently misaligns structs no compile error is raised. Consider a layout assertion in the generated file or a build-time check. | P3 | BATCH-10+ | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)
