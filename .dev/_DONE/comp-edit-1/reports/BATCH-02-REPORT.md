# BATCH-02 Report

**Batch:** BATCH-02
**Tasks:** TASK-CE06, TASK-CE04, TASK-CE05
**Date:** 2026-04-24
**Status:** COMPLETE

---

## Implementation Summary

### TASK-CE06 — Add StructEdit Project References

Modified `FDP/Engine/Fdp.Presentation/Fdp.Presentation.csproj`. Added two `<ProjectReference>` entries to the existing `<ItemGroup>` that already contained the `Fdp.Toolkits` reference:

```xml
<ProjectReference Include="..\..\ExtDeps\StructEdit\src\StructEdit.Core\StructEdit.Core.csproj" />
<ProjectReference Include="..\..\ExtDeps\StructEdit\src\StructEdit.Reflection\StructEdit.Reflection.csproj" />
```

`dotnet build FDP/Engine/Fdp.Presentation/Fdp.Presentation.csproj --no-restore` succeeds with 0 errors.

(Note: `dotnet build FDP/FDP.sln --no-restore` has pre-existing NETSDK1004 and MSB3202 errors for various ExtDeps/Examples projects with un-restored NuGet assets — these are entirely unrelated to this batch and were present before any changes.)

### TASK-CE04 — Picker Attributes

Created `FDP/Engine/Fdp.Presentation/ImGui/Editing/PickerAttributes.cs` with two `public sealed` attribute classes in namespace `Fdp.Presentation.Editing`:

- `MapPickableEntityAttribute` — `[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]`, constructor `(params string[] filterPresets)`, property `string[] FilterPresets`.
- `MapPickableWorldLocationAttribute` — `[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]`, no parameters.

### TASK-CE05 — IComponentPickerContext

Created `FDP/Engine/Fdp.Presentation/ImGui/Editing/IComponentPickerContext.cs` with interface `IComponentPickerContext` in namespace `Fdp.Presentation.Editing`. All five methods as specified, using `Fdp.Core.Entity` and `System.Numerics.Vector3`, keyed on `string jsonPath`. Pure interface — no state, no default implementations.

### Tests

Created `FDP/Engine/Fdp.Presentation.Tests/ImGui/Editing/PickerTests.cs` with 5 new tests:

| Test | Task | Verifies |
|------|------|----------|
| `T_CE04a_MapPickableEntity_WithArgs_FilterPresetsArePreserved` | CE04 | `FilterPresets == ["tanks", "infantry"]` via field reflection |
| `T_CE04b_MapPickableEntity_NoArgs_FilterPresetsIsEmpty` | CE04 | `FilterPresets.Length == 0` when no args |
| `T_CE04c_MapPickableWorldLocation_AttributePresentOnField` | CE04 | Attribute present on field; `AttributeTargets.Field` is set |
| `T_CE05a_NopPickerContext_AllMethodsInvokableWithoutError` | CE05 | All five interface methods callable without exception |
| `T_CE05b_TryConsumeEntityPick_NoPendingPick_ReturnsFalseAndDefault` | CE05 | Returns `false` and `e == default(Entity)` |

---

## Test Results

**`Fdp.Presentation.Tests` — new tests (filter: `Fdp.Presentation.Tests.ImGui.Editing`):**

```
Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5
```

**`Fdp.Presentation.Tests` — full run:**

```
Failed!  - Failed: 1, Passed: 216, Skipped: 0, Total: 217
```

The 1 failure (`EntityRenderLayer_HitTest_FindsClosest`) is **pre-existing** — confirmed by running the same filter against the pre-BATCH-02 commit via `git stash`. It was already failing before any changes in this batch.

---

## Q1 — Issues Encountered

One minor issue: the first draft of the CE04 tests applied `[MapPickableEntity]` to local functions, which violates the attribute's `AttributeTargets.Field | AttributeTargets.Property` usage declaration and would not compile. This was caught immediately and corrected by using a private nested `TestComponent` struct with annotated fields instead.

No other issues. CE06 and CE05 were straightforward.

---

## Q2 — IComponentPickerContext Design Assessment

The design feels right for the async pick use-case. The key strengths:

- **Stable key:** Keying on `jsonPath` (e.g., `"$.Targets[2].Location"`) rather than the sequential `EditNodeId.Value` is the correct choice. If an array element is removed and `RebuildDocument` is called, the sequential IDs shift but the JSON path of surviving fields does not. Any pending pick that was keyed on the old ID would be silently orphaned; the path key survives the rebuild.
- **Poll-based, non-blocking:** `IsPickPendingFor` + `TryConsume*` fit naturally into a per-frame ImGui render loop. The editor window can display a `[Picking...]` label on one frame and apply the result on the next, without ever blocking the render thread.
- **No state in the interface:** Keeping state entirely in the implementation prevents StructEdit or `Fdp.Presentation` from accumulating pick-state responsibilities. Implementations in `Hrot.IG` / `Hrot.ExCon` can manage per-context queues however they like.

One potential concern: if a pick context is shared across multiple simultaneously open editor windows for different entities, the `jsonPath` key must be unique across all of them (e.g., `"$.Position"` could appear in multiple open windows). Whether this is a real issue depends on whether the application allows multiple editor windows open at once and whether the same JSON path can appear in both. If it can, the implementation will need a composite key (e.g., window ID + path). This is an implementation detail for the application layer and not a flaw in the interface itself.

---

## Q3 — Edge Cases and Design Questions

- **`params string[]` with `null`:** `new MapPickableEntityAttribute(null)` would set `FilterPresets` to `new string[] { null }` rather than an empty array. This is consistent with normal `params` semantics. The design document and task spec do not call out null-arg handling, so this is left as standard C# behavior. If the renderer needs to guard against null elements, it can do so independently.
- **`FilterPresets` mutability:** The array returned by `FilterPresets` is the same instance passed to the constructor (no defensive copy). Since attributes are effectively read-only once constructed, this is fine for the intended use.

---

## Q4 — Suggested Commit Message

```
feat(comp-edit-1): Phase 2 picker infrastructure + project references (BATCH-02)

CE06 - Fdp.Presentation.csproj: add ProjectReference to StructEdit.Core and
  StructEdit.Reflection so Phase 3 rendering code can use StructEdit types.

CE04 - PickerAttributes.cs (new): MapPickableEntityAttribute and
  MapPickableWorldLocationAttribute in Fdp.Presentation.Editing. Both are
  public sealed, target Field|Property. MapPickableEntityAttribute accepts
  params string[] filterPresets (empty by default).

CE05 - IComponentPickerContext.cs (new): five-method interface in
  Fdp.Presentation.Editing. Requests are keyed on EditNode.JsonPath (stable
  semantic path) rather than the transient sequential EditNodeId so pending
  picks survive a RebuildDocument call. No state, no default implementations.

Tests: 5 new tests in Fdp.Presentation.Tests/ImGui/Editing/PickerTests.cs
  covering T-CE04a/b/c and T-CE05a/b. All 5 pass; 1 pre-existing failure
  (EntityRenderLayer_HitTest_FindsClosest) unaffected.
```
