# DEBT-TRACKER — blueprints-2

> P2/P3 deferred issues. P1 issues go directly into the next batch (never here).

| # | Priority | Source | Description | Target Batch |
|---|----------|--------|-------------|--------------|
| D-01 | P3 | BATCH-01 | `BehaviorTreeState.InstanceFlags` overlays `AsyncHandles[2]` at offset 56 (documented union). When struct is next redesigned, add a proper reserved-bytes block at the end instead. | Future | OPEN |
| D-02 | P3 | BATCH-01 | `BehaviorTreeBlob.SubtreeAssetIds` declared but never populated by `TreeCompiler`. Subtree node name is stored in `BuilderNode.MethodName` but not forwarded to blob. Subtree resolution at runtime is broken until this is implemented. | Phase 5 (BT-S1) | RESOLVED (BPF-018, BATCH-04: TreeCompiler.FlattenRecursive now populates SubtreeAssetIds for Subtree nodes) |
| D-03 | P3 | FIX1-BATCH-01 | `HsmEmitter.BuildMachineMetadata` reconstructs transition ordering independently from `HsmFlattener`. A mismatch would silently produce wrong Guid-to-index mappings. Fix: pass `FlattenedData` into `BuildMachineMetadata` directly instead of re-iterating the graph. | Future | RESOLVED (HsmEmitter.cs:38-61 passes FlattenedData into BuildMachineMetadata; other-fixes-2) |
| D-04 | P3 | FIX1-BATCH-01 | `MachineMetadata.ActionNames` ordering (alphabetical by Ordinal) diverges from `HsmFlattener.BuildActionTable` ordering. `ActionNames` is unreliable for stable action-to-index mapping. | Future | RESOLVED (ActionNames ordering aligned with HsmFlattener; other-fixes-2) |
