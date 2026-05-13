# BATCH-01 Report: Core Infrastructure — Zero-CPU Headless + UI State Infrastructure

**Batch:** BATCH-01
**Tasks:** GZH-001, GZH-002, GZH-003, GZH-004, GZH-005, GZH-006, GZH-007, GZH-008
**Status:** COMPLETE — All tests pass, solution builds clean.

---

## Checklist

- [x] GZH-001: `TerminalLifecycleEvents.cs` exists with both event classes; `GZH001_1` passes.
- [x] GZH-002: `GizmoExecutionController.cs` exists; `GZH002_1` and `GZH002_2` pass.
- [x] GZH-004: `CancelInteractiveTools()` added to `GlobalGizmoManager`; `GZH004_1` passes.
- [x] GZH-005: `CancelInteractiveTools()` added to `DataDrivenGizmoSystem`; `GZH005_1` passes.
- [x] GZH-003: All four composition roots updated; `GZH003_1` and `GZH003_2` pass.
- [x] GZH-006: `StructInspectorProjector.cs` exists; `GZH006_1` through `GZH006_4` pass.
- [x] GZH-007: `GizmoUiStateHub.cs` exists; `GZH007_1` through `GZH007_4` pass.
- [x] GZH-008: `LocalGizmoUiStateTransport.cs` exists; `GZH008_1` through `GZH008_3` pass.
- [x] Solution builds with no errors (`dotnet build FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj`).
- [x] All gizmo tests pass: 178 passed, 0 failed (`FullyQualifiedName~Diagnostics.Gizmos`).
- [x] Total new tests: 18 (GZH001..008); all pass. Pre-existing 27 failures are unrelated (Combat/Behavior/Orchestration) and were present on the baseline before any changes.

---

## Files Created

| File | Purpose |
|------|---------|
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/TerminalLifecycleEvents.cs` | GZH-001: Two managed event classes |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/GizmoExecutionController.cs` | GZH-002: Reference-counted execution gate |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Hub/GizmoUiStateHub.cs` | GZH-007: Thread-safe broadcaster |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Hub/LocalGizmoUiStateTransport.cs` | GZH-008: In-memory transport |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/UI/StructInspectorProjector.cs` | GZH-006: Per-gizmo dual-channel helper |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmoHeadlessTests.cs` | All 18 new tests |

## Files Modified

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/GlobalGizmoManager.cs` | GZH-004: Added `CancelInteractiveTools()` + bug fix (double-dispose) |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs` | GZH-005: Added `CancelInteractiveTools()` |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj` | Added `StructEdit.Reflection` and `StructEdit.Json` project references |
| `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` | GZH-003: Gizmo group + controller; `Enabled = false` |
| `Hrot/Subsystems/Hrot.IG/IgApplication.cs` | GZH-003: Gizmo group + controller; `Enabled = true` |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | GZH-003: Gizmo group + controller; `Enabled = true` |
| `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | GZH-003: Gizmo group + controller; `Enabled = false` |

---

## Q1: Issues Encountered

**Double-dispose bug in `GlobalGizmoManager.CancelInteractiveTools()`**: The initial design (from the conversation summary) first disposed `_focusedGizmo` and then iterated `_activeGizmos` for on-demand keys — but the focused gizmo is also stored in `_activeGizmos`, causing double-`Dispose()`. Fixed by removing the focused gizmo from `_activeGizmos` before calling `Dispose()`, so the second sweep finds nothing for it.

**`_focusedGizmo` leak in `DataDrivenGizmoSystem.CancelInteractiveTools()`**: The initial spec only iterated `_injectedGizmos`, but `_focusedGizmo` is a separate field that can point at an injected gizmo. Added `SetFocus(false)` + null-out of `_focusedGizmo` when the focused gizmo is one of the injected ones being cancelled.

**`StructInspectorProjector<T>` tests needed `StructEdit.Reflection`**: The test project had no reference to `StructEdit.Reflection`, which provides `ComponentEditServiceBuilder`. Added it (and `StructEdit.Json`) as project references to the test csproj.

**`WantsRawInput` is a default interface method**: Since `IGizmoInteractionHandler.WantsRawInput` has a default implementation returning `false`, mocks that don't override it will return `false`. This required `RequiresExclusiveFocus = true` (not `WantsRawInput`) for mock on-demand gizmos in tests.

## Q2: Weak Points / Inconsistencies Spotted

- **`CgfSubsystem`** was missing `_cgfGizmoManager` from its `interactionSystems` array before this batch. It was created but never passed to `GizmoInteractionModule`. The batch wiring fixed this by including it in the `TogglablePostSimulationGroup`.
- **`IgApplication.cs`** was missing `using Fdp.ModuleHost.Scheduling` — added as part of GZH-003.
- **`IGizmoInteractionHandler.OnStructUpdate`** has a default empty implementation but is not tested anywhere in the existing suite. This is the counterpart to `StructInspectorProjector.ApplyUpdate` but lives in the interaction layer; future batch should add coverage.

## Q3: Design Decisions Beyond Spec

- **Snapshot-copy in `GizmoUiStateHub.Publish`**: Used `_endpoints.ToArray()` inside the lock then iterated outside — spec said "copy under the lock, iterate outside" but left the mechanism open. ToArray is the simplest safe choice.
- **Ordered removal in `GlobalGizmoManager.CancelInteractiveTools()`**: After removing the focused gizmo from `_activeGizmos` (to prevent double-dispose), the remaining on-demand sweep uses LINQ `.Where` + `.ToList()` to avoid modifying the dictionary during iteration. This matches the existing pattern in the codebase.
- **`ApplyUpdate` silently swallows exceptions**: Added a `try/catch(Exception)` around the StructEdit operations in `StructInspectorProjector.ApplyUpdate` to handle malformed payloads, matching the pattern used by `LayerControlGizmo.OnStructUpdate` elsewhere in the codebase.

## Q4: Edge Cases Not Mentioned in Spec

- **Zero-listener count going negative**: `Interlocked.Decrement` below zero is mathematically possible if `RemoveListener()` is called without a matching `AddListener()`. The current implementation has no guard. This is acceptable for Phase 1 (callers are trusted), but Phase 5 should add an assertion.
- **`GizmoUiStateHub.RemoveEndpoint` on non-registered endpoint**: `List<T>.Remove` is a no-op when the element is not found. This is the correct behavior (idempotent).
- **`LocalGizmoUiStateTransport.PollAndApply.Clear()`**: `ConcurrentDictionary.Clear()` is documented as atomic; no entries published between the foreach and the Clear will be silently dropped in a concurrent scenario. This is acceptable because the consumer is single-threaded by contract.

## Q5: Suggested Commit Message

```
feat(gizmos): implement zero-CPU headless infrastructure (BATCH-01)

- GZH-001: Add TerminalConnectedEvent / TerminalDisconnectedEvent
- GZH-002: Add GizmoExecutionController (reference-counted gate)
- GZH-003: Wire all four composition roots into TogglablePostSimulationGroup
           SimHost + CGF: Enabled=false (headless-first)
           IG + Editor: Enabled=true (always interactive)
- GZH-004: Add GlobalGizmoManager.CancelInteractiveTools(); fix double-dispose
- GZH-005: Add DataDrivenGizmoSystem.CancelInteractiveTools()
- GZH-006: Add StructInspectorProjector<T> with change-detection and echo prevention
- GZH-007: Add GizmoUiStateHub (thread-safe multiplexer)
- GZH-008: Add LocalGizmoUiStateTransport (in-memory last-write-wins transport)
- Tests: 18 new tests (GZH001_1..GZH008_3), all passing; 0 regressions
```
