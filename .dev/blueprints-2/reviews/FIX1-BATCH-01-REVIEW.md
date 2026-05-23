# FIX1-BATCH-01 Review

**Batch:** FIX1-BATCH-01 — Phase 0: Kernel Prerequisites  
**Tasks:** TASK-K-01, TASK-K-02/K-03, TASK-K-05, TASK-K-06  
**Status:** APPROVED

---

## Verification Summary

All four tasks implemented correctly and verified against source files.

### TASK-K-01 — [HsmAction] Lane Property
- `HsmActionAttribute.cs` has `public CommandLane Lane { get; set; } = CommandLane.None;` ✅
- `HsmActionGenerator.cs` extracts `laneArg` from named arguments and assigns to `MethodInfo.Lane` ✅
- `EmitSharedAiActionThunk` prepends `[HsmAction(Name=..., Lane=...)]` on emitted thunks ✅
- Test coverage: source generator unit test in analyzer tests passes ✅
- F0-01, F0-02: SATISFIED

### TASK-K-02 / TASK-K-03 — HSM Fluent Builder stableId / visualId Round-Trip
- `MachineMetadata` has `StateStableIds` and `TransitionVisualIds` dictionaries ✅
- `HsmDefinitionBlob.Metadata` property added ✅
- `HsmEmitter.BuildMachineMetadata` populates both dictionaries in correct flattener order ✅
- `StateMachineGraph.Compile()` attaches metadata sidecar ✅
- 6 new tests in `MetadataRoundTripTests.cs` all pass ✅
- F0-03, F0-04, F0-05: SATISFIED

### TASK-K-05 — BTree Paused Flag
- `BTreeTickSystem.Execute` guards `BehaviorInstanceFlags.Paused` before calling `Tick(...)` ✅
- Test `BTreeTick_DoesNotTick_WhenPausedFlagIsSet` verifies pause halts execution and clearing resumes ✅
- F0-08: SATISFIED

### TASK-K-06 — BTree visualId on Composite/Decorator Builders
- All 17 composite/decorator builder methods in `BTreeBuilder.cs` have `Guid visualId = default` ✅
- `BuildMeta` stamps `visualId` into `NodeDebugMetadata.VisualId` ✅
- 2 new tests in `NodeDebugMetadataTests.cs` verify explicit and auto-generated VisualId ✅
- F0-06: SATISFIED

## Test Results Verified
- FastHSM: 289/291 pass (2 pre-existing failures unrelated to this batch) ✅
- FastBTree MetadataRoundTrip: 6/6 pass ✅
- FastBTree NodeDebugMetadata: 8/8 pass ✅
- BTreeTickSystem Paused: 1/1 pass ✅

## Issues Noted for Debt Tracker

**D-03 (P3):** `HsmEmitter.BuildMachineMetadata` reconstructs transition ordering independently from `HsmFlattener`. A mismatch between these two would produce silent wrong Guid-to-index mappings. Future fix: pass the `FlattenedData` struct into `BuildMachineMetadata` directly.

**D-04 (P3):** `MachineMetadata.ActionNames` ordering (alphabetical) diverges from `HsmFlattener.BuildActionTable` ordering. `ActionNames` should not be relied on for stable action-to-index mapping.

## Suggested Git Commit Message

Already committed as: `fix(kernels): FIX1-BATCH-01 Phase 0 kernel prerequisites` (f53ed842)

---

## Conclusion

All acceptance criteria for Phase 0 (F0-01 through F0-06, F0-08) are satisfied.
TASK-K-04 (HSM Paused flag) was verified as pre-existing correct implementation per FIX1-TASK-DETAIL.md.
TASK-S1-03 is verified as pre-existing correct implementation per FIX1-TASK-EXTRA-DETAILS.md.
