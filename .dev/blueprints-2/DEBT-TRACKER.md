# DEBT-TRACKER — blueprints-2

> P2/P3 deferred issues. P1 issues go directly into the next batch (never here).

| # | Priority | Source | Description | Target Batch |
|---|----------|--------|-------------|--------------|
| D-01 | P3 | BATCH-01 | `BehaviorTreeState.InstanceFlags` overlays `AsyncHandles[2]` at offset 56 (documented union). When struct is next redesigned, add a proper reserved-bytes block at the end instead. | Future |
| D-02 | P3 | BATCH-01 | `BehaviorTreeBlob.SubtreeAssetIds` declared but never populated by `TreeCompiler`. Subtree node name is stored in `BuilderNode.MethodName` but not forwarded to blob. Subtree resolution at runtime is broken until this is implemented. | Phase 5 (BT-S1) |
