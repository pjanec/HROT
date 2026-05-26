# BATCH-19: Phase M11 — Asset Wiring & Final Integration Test

**Batch Number:** BATCH-19  
**Phase:** M11 — Corrective: Production wiring  
**Tasks:** WHEN-M11-T4, WHEN-M11-T5, WHEN-M11-T6  
**Estimated Effort:** 10-14 hours  
**Dependencies:** BATCH-18 (editor bootstrap wiring) committed and passing  

---

## Overview

This batch completes Phase M11 production wiring by:

1. **Moving recipes to production** and wiring them into the Asset Browser
2. **Consolidating duplicate `ReactiveGuardVocabulary`** definitions  
3. **Writing the final integration smoke test** that validates all M11 tasks work end-to-end

After this batch, the entire When-Node feature is production-wired: designers can create
nodes via the palette, configure them in drawers, see visual decorations, and instantiate
them from recipes.

---

## Task Breakdown

### WHEN-M11-T4 — Move recipes to production location + wire Asset Browser discovery

**Scope:**

1. **Move recipe files to production location:**
   - Source: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/`
   - Target: `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/`
   - Files: All five `.bp.json` recipe files (CoverAwarePatrol, SimpleWhen, etc.)

2. **Update project file for content bundling:**
   - Edit `Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj`
   - Add: `<Content Include="Blueprints/Recipes/*.bp.json" />`
   - Ensures recipes are bundled into the shipped package

3. **Wire Asset Browser discovery:**
   - Location: Blueprint editor's "New from Recipe" dialog / Asset Browser menu
   - Implementation: Enumerate `Hrot.AI.Behaviors/Blueprints/Recipes/` at editor startup
   - Parse each `.bp.json` and filter to assets with `EditorMetadata.Recipe != null`
   - Populate dropdown in New-from-Recipe dialog
   - Wire "Create" button to `NewFromRecipeService.CreateFromRecipe(...)`

4. **Update test asset references:**
   - `RecipeIntegrityTests.cs` currently references test-location recipes
   - Either point to production location (preferred) or sync tests via CI check

**Design Reference:**  
[When_Reactivity_Iteration_Design_v2_2.md § Recipe file location](../When_Reactivity_Iteration_Design_v2_2.md)  
[TASK-DETAIL.md § WHEN-M11-T4](../TASK-DETAIL.md#when-m11-t4--move-recipes-to-production-location--wire-asset-browser-discovery)

**Success Conditions (from TASK-DETAIL):**
1. Integration test: Boot editor; open Asset Browser; click "+ New" → "From Recipe…"  
   Assert dropdown contains all five recipe names with "(★ recommended for learning)" star on `CoverAwarePatrol`
2. Select a recipe, enter name, click "Create"  
   Assert new BlueprintAsset created with fresh AssetId and EditorMetadata.Recipe == null

**Testing hints:**
- Verify five recipe files appear in the dropdown
- Create a new blueprint from CoverAwarePatrol recipe
- Verify the created blueprint compiles and runs
- Verify RecipeIntegrityTests still pass

---

### WHEN-M11-T5 — Consolidate the two `ReactiveGuardVocabulary` declarations

**Scope:**

1. **Identify the two declarations:**
   - File 1: `Hrot/Editor/Hrot.Editor.AiShared/ReactiveGuardVocabulary.cs` (canonical location)
   - File 2: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/ReactiveGuardVocabulary.cs` (duplicate)

2. **Compare contents:**
   - Verify all constants are byte-identical
   - If drifted, use `Hrot.Editor.AiShared` copy as authoritative
   - Document any differences in batch report

3. **Consolidate:**
   - Keep `Hrot.Editor.AiShared/ReactiveGuardVocabulary.cs` (canonical)
   - Delete or convert `Hrot.Blueprints.Editor/NodeDrawers/ReactiveGuardVocabulary.cs`
   - Update all `using` directives to reference the canonical location
   - Option: Keep a type-forward in the deleted location for backward compatibility

4. **Verify references:**
   - Search for all usages of `ReactiveGuardVocabulary` in the codebase
   - Ensure all consumers can find the canonical type
   - Run affected tests to verify no regressions

**Design Reference:**  
[When_Reactivity_Iteration_Design_v2_2.md § 14.4](../When_Reactivity_Iteration_Design_v2_2.md)  
[TASK-DETAIL.md § WHEN-M11-T5](../TASK-DETAIL.md#when-m11-t5--consolidate-the-two-reactiveguardvocabulary-declarations)

**Success Conditions (from TASK-DETAIL):**
1. Only one `class ReactiveGuardVocabulary` declaration remains in the solution
2. All previous consumers compile and resolve to the canonical `Hrot.Editor.AiShared` type

**Testing hints:**
- Build entire solution to verify no broken references
- Search `ReactiveGuardVocabulary` — should return only canonical declaration + usages
- Test BlueprInt drawer creation to ensure vocabulary constants are accessible

---

### WHEN-M11-T6 — End-to-end "wired" smoke test in the running editor

**Scope:**

Write a comprehensive integration test that validates all M11 tasks (T1–T5) work together
in a real editor instance. This is the regression guard preventing future bootstrap drift.

**Test file location:**  
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Integration/WhenNodeEditorSmokeTest.cs` (new)

**Test implementation:**

```csharp
[Fact]
public void EditorSmokeTest_BootEditor_ValidateAllM11Wiring()
{
    // Boot a real editor instance (headless)
    using var editor = new BlueprintEditorTestHarness();
    editor.Initialize();
    
    // Assert M11-T1: Drawer registry populated
    Assert.Contains(
        editor.GetDrawerRegistry().EnumerateAll(),
        d => d is WhenNodeDrawer
    );
    
    // Assert M11-T2: Palette entries present
    var paletteItems = editor.GetPaletteMenuItems();
    Assert.Contains(paletteItems, p => p.Text == "When");
    Assert.Contains(paletteItems, p => p.Text == "ReadEqsResult");
    Assert.Contains(paletteItems, p => p.Text == "SpawnEqsSensor");
    
    // Assert M11-T3: Canvas attachment providers registered
    Assert.Contains(
        editor.GetAttachmentProviders(),
        p => p is CrossAssetDependencyAttachmentProvider
    );
    
    // Assert M11-T4: Recipes available in Asset Browser
    var recipes = editor.GetAvailableRecipes();
    Assert.Contains(recipes, r => r.Name == "CoverAwarePatrol");
    Assert.Contains(recipes, r => r.Name == "SimpleWhen");
    
    // Assert M11-T5: ReactiveGuardVocabulary is single declaration
    var vocabularyType = typeof(ReactiveGuardVocabulary);
    Assert.Equal(
        "Hrot.Editor.AiShared.ReactiveGuardVocabulary",
        vocabularyType.FullName
    );
}
```

**Design Reference:**  
[When_Reactivity_Iteration_Design_v2_2.md § 16 M9](../When_Reactivity_Iteration_Design_v2_2.md)  
(mirrors EQS-2 `TASK-EQS-040` and UBP corrective patterns)  
[TASK-DETAIL.md § WHEN-M11-T6](../TASK-DETAIL.md#when-m11-t6--end-to-end-wired-smoke-test-in-the-running-editor)

**Success Conditions (from TASK-DETAIL):**
All five assertions pass:
1. DrawerRegistry has three new drawers registered (failure of M11-T1)
2. Palette host has three new entries (failure of M11-T2)
3. Canvas attachment-provider list contains three new providers (failure of M11-T3)
4. Asset Browser's recipe discovery returns 5 entries from production folder (failure of M11-T4)
5. ReactiveGuardVocabulary resolves to canonical type (failure of M11-T5)

**Constraint:**  
This test serves as the regression guard for M11. It must remain in the test suite even
after the phase closes — it prevents future bootstrap-code drift from silently un-wiring
the feature.

---

## Quality Checklist

Before submitting, verify:

- [ ] Five recipe files moved to production location (`Hrot.AI.Behaviors/Blueprints/Recipes/`)
- [ ] Project file updated with content bundling rule
- [ ] Asset Browser recipe dropdown works end-to-end (dropdown → create → new asset)
- [ ] RecipeIntegrityTests pass with updated paths
- [ ] Only one `ReactiveGuardVocabulary` declaration remains in solution
- [ ] All consumers of vocabulary updated to canonical location
- [ ] Solution compiles without errors or warnings
- [ ] New smoke test passes and validates all M11 wiring
- [ ] No changes to TASK-DETAIL.md or DESIGN documents

---

## Deliverables

Upon completion, submit:

1. **BATCH-19-REPORT.md** in `reports/` containing:
   - Summary of recipe migration (files moved, project changes)
   - Consolidation details (which files deleted, which consumers updated)
   - Copy of smoke test output (assertions passing)
   - Any blockers or unexpected findings

2. **Updated solution** with:
   - Recipes in production location
   - Single ReactiveGuardVocabulary declaration
   - Smoke test passing and integrated

---

## References

- [TASK-DETAIL.md § Phase M11](../TASK-DETAIL.md#phase-m11--corrective-production-wiring)
- [When_Reactivity_Iteration_Design_v2_2.md § 14, 16](../When_Reactivity_Iteration_Design_v2_2.md)
- BATCH-18 / BATCH-18-CORRECTIVE reports (context on M11-T1/T2/T3)
- EQS-2 and Universal-Breakpoints corrective phases (similar patterns)

---

## Next Steps After Approval

Once BATCH-19 is approved and committed:

1. Update TASK-TRACKER.md to mark WHEN-M11-T4/T5/T6 complete
2. Verify all M11 tasks (T1–T6) show [x] in TASK-TRACKER
3. Phase M11 is COMPLETE — entire When-Node feature is production-wired
4. Begin next iteration or close out the feature
