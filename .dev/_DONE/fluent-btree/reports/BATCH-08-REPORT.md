# BATCH-08 Report: Phase 4 — IEntityAwareImGuiRenderer, ComponentReflector, BehaviorDefinition, BrainBlackboardRenderer, BTreeVisualizerRenderer

**Batch Number:** BATCH-08
**Tasks:** FBT-030, FBT-031, FBT-032, FBT-033, FBT-034, FBT-035, FBT-036, FBT-037
**Status:** COMPLETE
**Date:** 2026-04-30

---

## Test Results

| Suite | Before | After | Delta |
|---|---|---|---|
| `Fbt.Tests` | 149 | 149 | +0 |
| `Fdp.Presentation.Tests` | 249 | 251 | +2 |
| `Fdp.Toolkits.Tests` | 761 passing (13 pre-existing failures, unrelated to batch) | 761 passing | +0 |
| `Hrot.Presentation.Tests` | 31 | 41 | +10 |

All new tests pass. No regressions introduced.

The 13 failures in `Fdp.Toolkits.Tests` are pre-existing and unrelated to this batch (they affect `SimTransformBridgeSystemTests`, `IdAllocationTests`, `PhysicsQueryActionNodeTests`, and `CombatComponentTests`).

---

## Files Created

| File | Task |
|---|---|
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/EntityAwareRendererTests.cs` | FBT-035 |
| `Hrot/Engine/Hrot.Presentation/Renderers/BrainBlackboardRenderer.cs` | FBT-033 |
| `Hrot/Engine/Hrot.Presentation/Renderers/BTreeVisualizerRenderer.cs` | FBT-034 |
| `Hrot/Engine/Hrot.Presentation.Tests/ImGuiTestFixture.cs` | FBT-036 prerequisite |
| `Hrot/Engine/Hrot.Presentation.Tests/Behavior/BrainBlackboardRendererTests.cs` | FBT-036 |
| `Hrot/Engine/Hrot.Presentation.Tests/Behavior/BTreeVisualizerRendererTests.cs` | FBT-037 |

## Files Modified

| File | Task | Change Summary |
|---|---|---|
| `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs` | FBT-034 prereq | Added `public BehaviorTreeBlob Blob => _blob;` property |
| `FDP/Engine/Fdp.Presentation/ImGui/Renderers/IImGuiRenderer.cs` | FBT-030 | Added `IEntityAwareImGuiRenderer` interface; added `using Fdp.Core; using Fdp.Presentation.Abstractions;` |
| `FDP/Engine/Fdp.Presentation/ImGui/Utils/ComponentReflector.cs` | FBT-031 | Updated dispatch to use `IEntityAwareImGuiRenderer.RenderValue(session, e, data)` when available |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs` | FBT-032 | Added `public Type? ParamsDtoType { get; init; }` to `BehaviorDefinition` |

---

## Implementation Notes / Deviations

### CS0213 fix in BrainBlackboardRenderer
The batch instructions used `fixed (byte* ptr = bb.Memory)` for accessing the fixed buffer, which causes CS0213 ("cannot use the fixed statement to take the address of an already fixed expression"). Since `BrainBlackboard bb` is a value-type local parameter (on the stack), the fixed buffer can be accessed directly in an unsafe block without `fixed`:
- `RenderTypedDto`: `Marshal.PtrToStructure((IntPtr)bb.Memory, dtoType)`
- `RenderRawBytes`: `byte* ptr = bb.Memory;`

### ImGuiTreeNodeFlags.NoTreePushOnLeaf
The flag `NoTreePushOnLeaf` does not exist in the ImGui.NET version used by this project. Replaced with the correct `NoTreePushOnOpen` (consistent with usage elsewhere in `ImGuiPropertyTree.cs` and `ClusterScenarioPanel.cs`).

### ComponentId(299) → ComponentId(200)
The `SampleComponent` test helper in `EntityAwareRendererTests` was annotated with `[ComponentId(299)]`, but `ComponentIdAttribute` accepts `byte` (0-255). Changed to `200` (reserved examples range per `GlobalComponentIds`).

### BrainBlackboardRenderer — Collection tag
`BrainBlackboardRendererTests` is marked `[Collection("ImGui Sequential")]` without `IClassFixture<ImGuiTestFixture>` because the tests only exercise early-exit paths (return before any ImGui API calls). The `ImGuiTestFixture.cs` was copied into `Hrot.Presentation.Tests` (with namespace `Hrot.Presentation.Tests`) in case future tests in the collection need it.

### ImGui.NET transitive availability
`Hrot.Presentation.Tests` references `Hrot.Presentation` which has `rlImgui-cs` → `ImGui.NET` as a package dependency. The transitive package reference was sufficient; no explicit `ImGui.NET` package was needed in the test project.

---

## Success Criteria Verification

- [x] `IEntityAwareImGuiRenderer` interface exists and inherits `IImGuiRenderer`
- [x] `ComponentReflector` uses entity-aware path when renderer implements the new interface
- [x] `BehaviorDefinition.ParamsDtoType` property added with `null` default
- [x] `Interpreter<T>.Blob` property added
- [x] `BrainBlackboardRenderer` compiles + registered via `[ImGuiRenderer]` attribute
- [x] `BTreeVisualizerRenderer` compiles + `GetNodeColorCode` internal helper exists
- [x] All 149 `Fbt.Tests` pass
- [x] All 251 `Fdp.Presentation.Tests` pass (249 + 2 new)
- [x] `Hrot.Presentation.Tests` at 41 (31 + 10 new tests)
