# BATCH-15 Report — GZ039: Undo/Redo Stack for Gizmo Interactions

**Date:** 2026-05-07  
**Agent:** Claude Sonnet 4.6

---

## Files Created

| File | Description |
|------|-------------|
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/UndoRedo/IGizmoUndoRecord.cs` | New interface: `Description`, `Undo`, `Redo` |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/UndoRedo/GizmoUndoStack.cs` | New class: bounded undo/redo stack with `Push`, `Undo`, `Redo`, `Clear` |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmoUndoStackTests.cs` | 8 tests SC-GZ039-1 through SC-GZ039-8 |

## Files Modified

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatefulGizmo.cs` | Added default interface method `CreateUndoRecord(commit) => null` with usings for Events and UndoRedo |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs` | Added `_undoStack` field, optional `undoStack` constructor param, step 5 in Execute to read commit events and push records |
| `Hrot/Subsystems/Hrot.IG/IgApplication.cs` | Added `_gizmoUndoStack` field, initialization, `HandleGizmoUndoInput()` method, call in Update, WorldResetEvent clearing |

---

## Build Result

```
Build succeeded.
    0 Error(s)
```

---

## Test Results

```
Passed: 8 / 8
```

| Test | Status |
|------|--------|
| SC-GZ039-1 Push_Then_Undo_CallsUndoAndMovesRecord | PASSED |
| SC-GZ039-2 Undo_Then_Redo_CallsRedoAndMovesBack | PASSED |
| SC-GZ039-3 Push_BeyondMaxDepth_DropsOldest | PASSED |
| SC-GZ039-4 Push_ClearsRedoStack | PASSED |
| SC-GZ039-5 Undo_WhenEmpty_NoOp | PASSED |
| SC-GZ039-6 Redo_WhenEmpty_NoOp | PASSED |
| SC-GZ039-7 DataDrivenGizmoSystem_PushesRecord_AfterCommit | PASSED |
| SC-GZ039-8 Null_CreateUndoRecord_DoesNotPush | PASSED |

SC-GZ039-7 and SC-GZ039-8 were implemented (not skipped). The integration was straightforward:
register `GizmoInteractionCommitEvent` in the test repo, publish construction then commit events,
assert stack state.

---

## Git Commits

| Repo | Commit Hash | Message |
|------|-------------|---------|
| FDP submodule | `5ea4ea5` | `GZ039: IGizmoUndoRecord, GizmoUndoStack, DataDrivenGizmoSystem integration` |
| Root | `8569711` | `GZ039: Gizmo undo/redo keyboard shortcuts in IgApplication` |

---

## Deviations from Instructions

1. **`using Fdp.Core;` vs `using Fdp.Interfaces;`**: The instruction template used `using Fdp.Core;`
   for `IGizmoUndoRecord`. The actual namespace for `IEntityCommandBuffer` is `Fdp.Interfaces`
   (same assembly, different namespace). Used `using Fdp.Interfaces;` to match the existing
   codebase pattern (`GizmoSettingsRegistry.cs`).

2. **`HandleGizmoUndoInput` uses fully-qualified cast**: Used
   `(Fdp.Core.EntityCommandBuffer)((Fdp.ModuleHost.Abstractions.ISimulationView)_world).GetCommandBuffer()`
   to match the existing pattern at line 2316, avoiding any ambiguity.

3. **`WorldResetEvent` namespace**: The type is in `Hrot.ScenarioEditor.Events` (not
   `Hrot.Presentation`). Added `using Hrot.ScenarioEditor.Events;` to `IgApplication.cs`.
   `Hrot.IG.csproj` already references `Hrot.Presentation.csproj` which contains this type.

4. **`DataDrivenGizmoSystem` commit event handling**: The system had no prior commit event
   handling. Added step 5 at the end of Execute to read `GizmoInteractionCommitEvent` events
   (returns empty span if not registered, no exception). Commit processing uses
   `PickToken.Target` to look up gizmo instances in `_activeGizmos`.

---

## WorldResetEvent Subscription Notes

`WorldResetEvent` is a managed class event published by `ScenarioFileService` via
`_bus.PublishManaged(new WorldResetEvent())`. In `IgApplication.Update()`, after
`_kernel.Update()`, we call `_world.Bus.ReadManaged<WorldResetEvent>()` which returns
an empty list when no reset occurred. This is the correct pattern as confirmed by the
integration test in `EditorFileIOIntegrationTests.cs`.

---

## Final TASK-TRACKER State

GZ039 marked as `[x]` complete in `.dev/gizmos-1/TASK-TRACKER.md`.
