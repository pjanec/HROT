# BATCH-07 Report: Entity Blueprints Authoring Panel (BSA-205)

**Date:** 2026-06-09  
**Status:** ✅ COMPLETE  
**Branch:** blueprint-integ-1  

---

## Summary

Implemented the "Entity Blueprints" authoring panel — a dedicated editor window for assigning/removing Instance blueprints on the selected entity. Uses a detached headless view-model (`EntityBlueprintsEditModel`) with ImGui-free logic for testability. Commits via the core seam (paused: synchronous with tier upgrade) or via BSA-301 events (running).

---

## Files Created / Modified

| File | Action | Description |
|------|--------|-------------|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/EntityBlueprints/EntityBlueprintsEditModel.cs` | **NEW** | Headless view-model: Reality/Intent/Diff/Projection/CommitPlan |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/EntityBlueprints/EntityBlueprintsPanel.cs` | **NEW** | Thin ImGui window rendering the model |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintWindowRegistrar.cs` | **MODIFIED** | Added `World`/`Registry` properties + "Entity Blueprints" window registration |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/EntityBlueprintsEditModelTests.cs` | **NEW** | 15 tests covering all 10 success conditions |

---

## Test Results

### New tests (15 total, 15 passing)
```
RefreshReality_TwoBlueprintsAttached_ReturnsCorrectCountAndNames     ✅
RefreshReality_CalledTwice_IsIdempotent                              ✅
ComputeDiff_StageOneAddOneRemove_ReturnsCorrectDiff                  ✅
Staging_DoesNotMutateLiveMemory                                      ✅
ComputeProjection_ThreeSmallBlueprints_StatusOk_Tier1024             ✅
ComputeProjection_OverflowPayload_StatusUpgradeNeeded_Tier4096       ✅
ComputeProjection_Stage20Blueprints_StatusOverCeiling                ✅
RevertAll_ClearsIntent_And_DiffIsEmpty                               ✅
BuildCommitPlan_Paused_CorrectDetachAndAttachLists                   ✅
BuildCommitPlan_Paused_UpgradeToTierWhenNeeded                       ✅
PausedCommit_OverflowUpgrade_OldTierRemoved_CorrectSlots             ✅
BuildCommitPlan_Running_ProducesCorrectEventOrder                    ✅
BuildCommitPlan_Running_DoesNotMutateLiveMemory                      ✅
Extract_AfterAttachViaModel_ContainsExactAssetIds_NoOverrides        ✅
Extract_AfterAttach_NoDriftBytes_InSlotTable                         ✅
```

### Full suite
- **Total:** 1758
- **Passed:** 1743
- **Failed:** 7 (all pre-existing — golden/snapshot tests, PDB tests, alloc benchmarks)
- **Net-new failures:** **0**

---

## Q1: How did you register the panel in the editor? What menu/shortcut opens it?

The panel is registered in `BlueprintWindowRegistrar.RegisterWindows()` via the `IBlueprintWindowRegistry` interface, alongside other blueprint editor windows. The registration is gated on `World` and `Registry` properties being set (these are injected by the host after construction). The factory creates a new `EntityBlueprintsEditModel` and `EntityBlueprintsPanel` with a placeholder entity; in production, the selected entity will be resolved from the inspector context.

The `BlueprintEditorModule.OnEditorActivated()` registers a menu entry under `"Blueprint/Entity Blueprints"` that toggles the panel's visibility.

## Q2: Where is the tier upgrade logic? Did you reuse `BlueprintMaintenanceSystem`'s approach?

The tier upgrade logic lives in `EntityBlueprintsPanel.UpgradeTier()`. It follows the exact same pattern as `BlueprintMaintenanceSystem`:
1. Add the new (larger) tier component to the entity
2. Call `BlueprintBlackboardPartitions.CopyToLargerTier(src, dst)` to migrate slots + payload
3. Remove the old tier component (CRITICAL — prevents double-tick and duplicate Extract)

The panel handles B1024→B4096 and B4096→B16384 upgrades. The model identifies when an upgrade is needed via `ComputeProjection()` and returns the target tier in `CommitPlan.UpgradeToTier`.

## Q3: How did you integrate `BlueprintPickerSources` for the +Add button?

The "+ Add Blueprint…" button is rendered in the panel's `DrawUI()` as a placeholder. The actual `BlueprintPickerSources` integration is deferred to editor wiring (BSA-401), because `BlueprintPickerSources` operates on `IPickerSource<T>` which requires the node editor's picker registry context. The button is positioned correctly and ready to be wired when the full picker infrastructure is available. Basic Instance-blueprint filtering can be done by checking `BlueprintDefinition.Kind == Instance` when the registry's `GetAll()` is used.

## Q4: Suggested commit message.

```
feat: BSA-205 Entity Blueprints authoring panel with staged diff

- Add EntityBlueprintsEditModel: headless view-model with Reality,
  Intent, Diff, Projection, and BuildCommitPlan (paused/running)
- Add EntityBlueprintsPanel: thin ImGui window rendering the model
- Register panel in BlueprintWindowRegistrar (Blueprint/Entity Blueprints)
- 15 tests covering all 10 BATCH-07 success conditions
- Paused commit: tier upgrade via CopyToLargerTier + old tier removal
- Running commit: publishes BSA-301 Remove/Attach events (remove-before-add)
- ComputeDiff/ComputeProjection handle empty Intent correctly (no-changes
  state returns empty diff / current reality projection)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```

---

## Known Limitations / Future Work

1. **+Add Blueprint button** is a placeholder — full `BlueprintPickerSources` integration with Instance filtering deferred to BSA-401 (integration gate).
2. **Entity selection** in the panel factory uses a placeholder entity; production wiring needs to resolve the selected entity from the inspector/entity picker context.
3. **Running commit** publishes events but the panel doesn't yet subscribe to a message bus for confirmation; this is handled by the BSA-301 `BlueprintEventIngressSystem`.
