# BATCH-02 Review

**Batch:** BATCH-02
**Reviewer:** Development Lead
**Date:** 2026-04-24
**Status:** APPROVED

---

## Issues Found

**Pre-existing test failure (not caused by BATCH-02):** `EntityRenderLayerTests.EntityRenderLayer_HitTest_FindsClosest` was already failing before BATCH-02 changes (confirmed by reverting and re-running). Not tracked here.

**NETSDK1004 on `dotnet build FDP/FDP.sln`:** Pre-existing; caused by missing `restore` assets for ExtDeps/Examples sub-projects. `Fdp.Presentation.csproj` itself builds cleanly. Not caused by CE06.

No issues attributable to BATCH-02 work.

---

## Code Quality

**CE06 (`Fdp.Presentation.csproj`):** Project references added in the correct `<ItemGroup>` alongside `Fdp.Toolkits`. Relative paths match the established style (`..\..\...`).

**CE04 (`PickerAttributes.cs`):** Both attribute classes are `public sealed`, `AttributeUsage` targets `Field | Property` on both, `params string[]` constructor is idiomatic. No unnecessary code.

**CE05 (`IComponentPickerContext.cs`):** Pure interface — no default implementations, no state. All five method signatures use `string jsonPath` (correct — NOT `int nodeId`). The XML comment on the interface explains the JsonPath key stability rationale.

---

## Test Quality

- `T-CE04a` and `T-CE04b` use `field.GetCustomAttribute<>()` on a concrete `TestComponent` struct — actual reflection, not mocked.
- `T-CE04c` also checks `AttributeUsageAttribute.ValidOn` includes `AttributeTargets.Field` — behavioral, not just "not null".
- `T-CE05a` calls all five methods on the NOP implementation — verifies the interface contract is complete and implementable.
- `T-CE05b` uses `"$.Targets[0]"` as the jsonPath key — validates the JsonPath key format is accepted and consumed correctly.

---

## Verdict

**APPROVED.** 5 new tests, all pass. 216 pre-existing tests unchanged. `IComponentPickerContext` matches spec exactly. No spec deviations.

---

## Commit Message

```
feat(comp-edit-1): Phase 2 picker infrastructure (BATCH-02)

CE04 - PickerAttributes.cs: MapPickableEntityAttribute (params string[] filterPresets)
  and MapPickableWorldLocationAttribute, both [AttributeUsage(Field|Property)].
  Domain authors annotate ECS component fields to opt into picker UI in the editor.

CE05 - IComponentPickerContext.cs: 5-method interface for async map/entity picks.
  All methods keyed on string jsonPath (not transient nodeId) for stability across
  RebuildDocument calls.

CE06 - Fdp.Presentation.csproj: Added StructEdit.Core and StructEdit.Reflection
  project references, enabling Phase 3 to use StructEdit types directly.
```

---

**Next Batch:** BATCH-03 (Phase 3 main — ComponentEditDrawer + ComponentEditWindow)
