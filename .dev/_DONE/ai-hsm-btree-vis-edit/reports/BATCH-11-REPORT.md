# BATCH-11 Report

## Summary

BATCH-11 delivered three tasks (1e-03, 1e-04, 1e-05) covering layout persistence for
sync bindings, Approach B orchestrator code emission, and auto-allocated memory slices
for subtree DTOs. All production code was implemented across 11 modified/created files.
17 new tests were written in three new/extended test files, and an additional 9 integration
tests were added for the layout builder and projector path. The Hrot.BTree.Editor.Tests
suite grew from 239 to 265 passing tests (0 failures). The full solution builds with
0 errors, 0 warnings.

## Tasks Completed

| Task ID | Deliverable | Tests Written | Tests Passing |
|---------|-------------|---------------|---------------|
| 1e-03 | Layout persistence for sync bindings — `SyncBindings` on `BTreeEditorLayout`, `SubtreeSyncField` on `BTreeEditorLayoutBuilder`, `LoadSyncBindings` on `BehaviorTreeAsset`, `EmitLayout` emits `.SubtreeSyncField(...)` calls, `BehaviorTreeAssetProjector` wires layout to asset | 5 (BTreeSyncPersistenceTests) + 4 projector + 5 layout builder = 14 | 14 |
| 1e-04 | Approach B orchestrator emit — `ApproachBSyncGroup` record, `IBTreeSyncableAsset.GetApproachBSyncGroups`, `BTreeOrchestratorEmitter` emits ref-DTO + sync-in + tick + sync-out per group | 7 (BTreeOrchestratorSyncEmitterTests) | 7 |
| 1e-05 | Auto-allocated variables — `IBTreeSyncableAsset.GetAutoAllocatedVariables`, `BehaviorTreeAsset.GetAutoAllocatedVariables`, `BlackboardAuthoringWindow` SUB-TREE ALLOCATIONS section, `IBTreeSyncableAsset.RecordSubtreeNodeMeta` + helpers in `InspectorWindow` | 5 (BTreeAutoAllocationTests) | 5 |

## Files Modified / Created

### New Files
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/ApproachBSyncGroup.cs`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeSyncPersistenceTests.cs`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeAutoAllocationTests.cs`

### Modified Files
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBTreeSyncableAsset.cs` — added `RecordSubtreeNodeMeta`, `GetApproachBSyncGroups`, `GetAutoAllocatedVariables`
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBlackboardManagedAsset.cs` — added default `Name` and `BlackboardTypeName` implementations
- `Hrot/Editor/Hrot.Editor.AiShared/Layout/BTreeEditorLayout.cs` — added `SyncBindings` property
- `Hrot/Editor/Hrot.Editor.AiShared/Layout/BTreeEditorLayoutBuilder.cs` — added `SubtreeSyncField` method, updated `Build()`
- `Hrot/Editor/Hrot.Editor.AiShared/Windows/InspectorWindow.cs` — added `RecordSubtreeNodeMeta` call + `ShortTypeName`, `NsOf`, `SanitizeIdentifier` helpers
- `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs` — added SUB-TREE ALLOCATIONS section
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs` — added `RecordSubtreeNodeMeta`, `GetApproachBSyncGroups`, `GetAllSyncBindings`, `LoadSyncBindings`, `GetAutoAllocatedVariables`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAssetProjector.cs` — added `LoadSyncBindings` call after layout block
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Emit/BTreeFluentEmitter.cs` — `EmitLayout` emits sorted `.SubtreeSyncField(...)` calls
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Emit/BTreeOrchestratorEmitter.cs` — complete Approach B emit
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeOrchestratorEmitterTests.cs` — appended `BTreeOrchestratorSyncEmitterTests` class
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BehaviorTreeAssetProjectionTests.cs` — appended projector sync + layout builder sync tests

## Developer Insights

### Issues Encountered

1. **Circular dependency** — `BlackboardAuthoringWindow` (AiShared) cannot directly reference
   `Hrot.BTree.Editor.Model.BehaviorTreeAsset`. The workaround was to add
   `GetAutoAllocatedVariables()` to the `IBTreeSyncableAsset` interface and use
   `(asset as IBTreeSyncableAsset)?` in the window. This is clean and does not introduce
   a hard dependency.

2. **`EmitLayout` visibility** — `BTreeFluentEmitter.EmitLayout` is `private static`.
   Persistence tests that needed to verify sync field emission had to go through the public
   `emitter.Emit(asset)` path and inspect the full generated text, not the layout helper
   alone.

3. **`BTreeSubtreePayload.SubtreeAssetId` type** — Is `Guid`, not `string`. Persistence
   test helpers initially used string literals and had to be corrected.

4. **`BlackboardVariableEntry` constructor** — The `comment:` named parameter requires
   a capital C (`Comment:` is the parameter name). Small but non-obvious gotcha.

5. **`LoadSyncBindings(null)` semantics** — The implementation always clears `_syncBindings`
   first before processing the parameter. When null is passed, the result is an empty dict.
   This is the desired behavior (reset path), but had to be verified before finalizing T5.

6. **Orchestrator `targetNs` duplicate** — When refactoring `BTreeOrchestratorEmitter.Emit`
   to add the Approach B block, a duplicate `targetNs` variable declaration was introduced.
   Fixed by restructuring the variable initialization to a single declaration site.

### Spec Ambiguities

- **Approach A preemption test (T7)**: The spec says the Approach A alias is for "var SharedFire
  to Shoot_BT". The alias matching in `GetAutoAllocatedVariables` uses
  `GetAliasesFor(v.Name).Any(a => a.RequiringElementId == group.NodeVisualId)`. In T7 the test
  registers a blackboard variable and calls `AddAlias` to confirm the preemption. The spec was
  clear enough on intent; the alias must name the same node visual ID.

- **`GetApproachBSyncGroups` "anyActive" rule**: The spec says include a node when at least one
  binding has `(SyncIn || SyncOut) && MasterVariableName != null`. A group with bindings only
  having `MasterVariableName=null` is excluded even if SyncIn=true — this was confirmed by T6.

### RecordSubtreeNodeMeta Timing

`RecordSubtreeNodeMeta` is called by `InspectorWindow.DrawSyncBindingsTable` — i.e., it runs
on every UI render when the user has a subtree node selected in the Inspector. The emitter
(`BTreeFluentEmitter`/`BTreeOrchestratorEmitter`) runs only when "Generate" is triggered
(typically a button press or on-save). Between `RecordSubtreeNodeMeta` and `Emit`, the canvas
must have been rendered at least once with the subtree node visible for the metadata to be
populated.

A scenario where the emitter runs before the Inspector has rendered the node: a fresh project
load where the user immediately invokes "Generate" without opening/inspecting any node. In
this case `_syncNodeMeta` is empty, `GetApproachBSyncGroups` returns empty, and the
orchestrator file for Approach B is simply not emitted — a safe degradation.
This could be mitigated by persisting `RecordSubtreeNodeMeta` data to the layout file
alongside sync bindings (deferred design concern).

### Auto-Allocation Size Deferral

`typeof(object)` is not blittable. `Marshal.SizeOf(typeof(object))` throws
`ArgumentException`. In practice, the auto-allocated variables are display-only until the
subtree DTO type is resolved from the runtime catalog. The real fix requires:
1. A catalog that maps `subDtoTypeName` strings to `System.Type` objects at edit time.
2. Passing the resolved `Type` to `BlackboardVariableEntry` so the bin-packer can compute
   a real byte size.
A `// TODO(1e-05): pass to bin-packer when type resolution is available` comment was left in
`BlackboardAuthoringWindow`.

### `IBlackboardManagedAsset` Additions

Default `Name` returns `GetType().Name` and `BlackboardTypeName` returns
`GetType().Name + "_Blackboard"`. All existing stubs in tests use anonymous/inline
implementations (`IBTreeSyncableAsset` stubs) that did not require explicit overrides, as
they only tested interface methods unrelated to `Name`/`BlackboardTypeName`. No stub needed
an explicit override.

## Test Counts

| Project | Before | After |
|---------|--------|-------|
| Hrot.BTree.Editor.Tests | 239 | 265 |
| Hrot.Editor.AiShared.Tests | unchanged | 372 |
| Full solution | compiles clean | compiles clean |
