# BATCH-04 Report

**Batch:** BATCH-04 (Phase 4 Wiring)
**Workstream:** comp-edit-1
**Date:** 2025-07-14
**Status:** Complete

---

## Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| TASK-CE09 | Done | `ImGuiPropertyTree.Render` overload + four properties + `_editService` + `TryOpenEditWindow` on `ComponentReflector` |
| TASK-CE10 | Done | `public ComponentReflector Reflector` property on `EntityInspectorPanel` and `EntityWatchPanel` |

---

## Modified Files

| File | Change |
|------|--------|
| `FDP/Engine/Fdp.Presentation/ImGui/Utils/ImGuiPropertyTree.cs` | Added `Render(object?, Type?, out string? doubleClickedPath)` overload; existing overload delegates to it; `RenderRows` / `RenderCollectionRows` carry `string jsonPath, ref string? doubleClickedPath` and detect double-click per row |
| `FDP/Engine/Fdp.Presentation/ImGui/Utils/ComponentReflector.cs` | Changed from `internal` to `public class`; added four injectable properties (`EditWindowManager`, `EditSessionGetter`, `EditPickerContext`, `EditOwningPerspective`); added `_editService` field; default constructor + internal test constructor; `TryOpenEditWindow` internal method; `DrawComponents` updated to capture double-click |
| `FDP/Engine/Fdp.Presentation/ImGui/Panels/EntityInspectorPanel.cs` | Added `public ComponentReflector Reflector => _reflector;` |
| `FDP/Engine/Fdp.Presentation/ImGui/Panels/EntityWatchPanel.cs` | Added `public ComponentReflector Reflector => _reflector;` |

## New Test Files

| File | Tests |
|------|-------|
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/ComponentReflectorDoubleClickTests.cs` | 6 tests (T-CE09a through T-CE09f) |
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReflectorExposureTests.cs` | 5 tests (T-CE10a, T-CE10b, T-CE10c inspector, T-CE10c watch, T-CE10d smoke) |

---

## Testing Results

**`Fdp.Presentation.Tests`:** 248 passed / 249 total (1 pre-existing failure unchanged)  
**`IOS-IG-SimHost.sln` build:** Succeeded (no new errors)

**New tests added:** 11 (requirement was >= 10)

### CE09 tests (`ComponentReflectorDoubleClickTests`)

| ID | Name | Result |
|----|------|--------|
| T-CE09a | ReadOnly session -- TryOpenEditWindow is no-op | Pass |
| T-CE09b | Null EditWindowManager -- no exception | Pass |
| T-CE09c | Header double-click -- registers window | Pass |
| T-CE09d | Second call on same window ID -- focus not duplicate | Pass |
| T-CE09e | Window ID format is deterministic | Pass |
| T-CE09f | Field doubleClickedPath -- opens ForField scope | Pass |

### CE10 tests (`ReflectorExposureTests`)

| ID | Name | Result |
|----|------|--------|
| T-CE10a | `EntityInspectorPanel.Reflector` is non-null after construction | Pass |
| T-CE10b | `EntityWatchPanel.Reflector` is non-null after construction | Pass |
| T-CE10c | Inspector Reflector: `EditWindowManager` is settable | Pass |
| T-CE10c | Watch Reflector: `EditWindowManager` is settable | Pass |
| T-CE10d | Draw regression smoke: Draw still executes after Reflector added | Pass |

### Pre-existing failure (unchanged)

`Fdp.Toolkit.Vis2D.Tests.Layers.EntityRenderLayerTests.EntityRenderLayer_HitTest_FindsClosest` -- present before this batch; not related to BATCH-04 changes.

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Two compiler errors required deviations from the original spec (see Deviations below):

1. **CS0118 -- WindowManager namespace/class collision.** `Fdp.Presentation.WindowManager` is both a namespace and contains a class named `WindowManager`. Declaring `WindowManager? EditWindowManager` failed because the compiler resolved `WindowManager` as the namespace. Fixed with type alias `using WM = Fdp.Presentation.WindowManager.WindowManager;` in both `ComponentReflector.cs` and the test file.

2. **CS0053 -- Inconsistent accessibility.** The spec said `ComponentReflector` should remain `internal`, but `EntityInspectorPanel.Reflector` and `EntityWatchPanel.Reflector` are `public` properties of `public` classes. C# forbids a `public` property whose return type is `internal`. Fixed by changing `ComponentReflector` to `public class`.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

The `WindowManager` naming collision (class and namespace sharing the same identifier) is a maintenance hazard. Renaming the namespace to `Fdp.Presentation.WindowManagement` would eliminate the need for the alias everywhere.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

Extracted `TryOpenEditWindow` as a separate `internal` method rather than inlining all the window-open logic inside `DrawComponents`. This was not in the spec, but it is the only way to unit-test the open/focus/scope logic without a live ImGui frame (CE09c-f call it directly). The alternative -- only an integration test requiring mouse simulation -- would not be feasible in the headless test environment.

Added an internal constructor `ComponentReflector(IComponentEditService editService)` to allow fake service injection in tests. The public default constructor builds a real service, matching production behaviour.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- Double-click on the header (`headerDoubleClicked = true`) and a row double-click (`doubleClickedPath != null`) can theoretically happen simultaneously. The current logic gives priority to `doubleClickedPath` (field-level scope) when both are set, which is the most useful behaviour.
- `doubleClickedPath` starts with `$` (root JSON path token). `EditPath.Parse` must accept that prefix; no issue was found.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

Each `DrawComponents` call now allocates `winId` and `title` strings per component per frame even when no double-click occurs. These are short-lived and GC-collected, so no measurable impact at typical entity counts, but caching them per entity+type would eliminate the allocations if needed later.

---

## Deviations from Instructions

| Deviation | Reason | Impact |
|-----------|--------|--------|
| `ComponentReflector` changed from `internal` to `public class` | CS0053: C# requires the return type of a `public` property to be at least as accessible as the property itself. The spec required `public ComponentReflector Reflector`, making `internal` illegal. | Expands the public API surface slightly; no functional change to existing code. |
| Property type declared as `WM?` via alias | CS0118: `WindowManager` name is ambiguous between namespace and class. Alias `using WM = Fdp.Presentation.WindowManager.WindowManager;` is the standard resolution. | None -- identical compiled output. |
| `TryOpenEditWindow` extracted as `internal` method | Required for CE09c-f unit tests to call window-open logic directly without live mouse input. | Better testability; no change to observable behaviour. |
| Internal test constructor `ComponentReflector(IComponentEditService)` added | Required to inject `FakeEditService` in CE09 tests. | No production-path change; constructor is `internal`. |

---

## Outstanding Issues / Next Steps

- The pre-existing `EntityRenderLayer_HitTest_FindsClosest` failure is unrelated to this batch and should be tracked separately.
- Callers that held a `ComponentReflector` field and relied on it being `internal` are now exposed to any assembly. If the class should remain package-private, a wrapper / factory pattern would restore the encapsulation without breaking the `Reflector` property requirement.
