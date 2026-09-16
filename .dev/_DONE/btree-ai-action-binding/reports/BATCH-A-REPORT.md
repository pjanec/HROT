# BATCH-A Report

## Implementation Summary

### Fix 1 — DelegateShape guard for picker + Promote affordance

Added `CurrentDelegateShape` (`BTreeActionDelegateShape?`) property to `BTreeFacetFqnContext`. The mapper now writes this field alongside `CurrentActionFqn` and `CurrentNodeVisualId` in `BuildActionFacet` and `BuildConditionFacet`, and clears it to `null` in the non-action/condition branch.

`BlackboardFieldPickerDrawer.HasNoCompatibleVariables` now returns `false` immediately when `CurrentDelegateShape == FourParamFull`, so the Promote affordance is never shown for whole-blackboard actions.

`DrawInput` adds an early return for `FourParamFull` that renders `"Operates on the full blackboard — no per-DTO binding."` instead of the picker or Promote button.

### Fix 2 — Tooltip on "Promote to new variable"

In `BlackboardFieldPickerDrawer.DrawInput`, added a `IsItemHovered` / `SetTooltip` call immediately after the `SmallButton` render. The tooltip explains what Promote does and why it's useful (creates a per-type blackboard variable, binds the node, enables zero-copy sharing).

No new test needed (pure ImGui rendering, not headless-testable).

### Fix 3 — Suppress false-positive collision error strip

Added `GetBindingAmbiguities(IActionSchemaExporter)` to `SubElementCollisionDetector`. It always returns `Array.Empty<ActionCollision>()` because BTree/HSM binding is always by full FQN — short-name collisions across distinct FQNs are harmless.

`InspectorWindow.DrawCollisionDiagnosticStrip` now calls `GetBindingAmbiguities` instead of `GetCollisions`, so the red "SUB-ELEMENT COLLISIONS DETECTED" strip is never shown.

`GetCollisions` is unchanged; all 6 existing collision-detection tests still pass.

## Design Decisions

- **`GetBindingAmbiguities` vs. removing `GetCollisions`**: kept `GetCollisions` intact so the underlying detection stays available for future tooling. The UI just stops surfacing it as an error. This is the minimal-risk, non-breaking approach.
- **Null for `CurrentDelegateShape` when no action selected**: matches the existing null-for-no-action pattern used by `CurrentActionFqn`. Nullability communicates "no node selected / not applicable" cleanly.
- **Tooltip placement**: `IsItemHovered` is called after the button, which is the ImGui convention; it only fires when the button itself is hovered.

## Deviations

None. All changes match the spec exactly.

## Test Results

**Hrot.BTree.Editor.Tests** (`--no-dependencies` build):
- Before: 568 passed
- After: **575 passed** (+7 new `DelegateShapeGuardTests`)
  - `HasNoCompatibleVariables_True_WhenThreeParamReusable_AndNoMatchingVars` — PASSED
  - `HasNoCompatibleVariables_False_WhenThreeParamReusable_AndMatchingVarExists` — PASSED
  - `HasNoCompatibleVariables_False_WhenFourParamFull_EvenWithNoMatchingVars` — PASSED
  - `HasNoCompatibleVariables_False_WhenFourParamFull_EvenWithZeroVarsInAsset` — PASSED
  - `Mapper_SetsCurrentDelegateShape_ForFourParamFullAction` — PASSED
  - `Mapper_SetsCurrentDelegateShape_ForThreeParamReusableAction` — PASSED
  - `Mapper_ClearsCurrentDelegateShape_ForNonActionNode` — PASSED

**Hrot.Editor.AiShared.Tests** (`--no-dependencies` build, collision tests only):
- Before: 6 passed
- After: **8 passed** (+2 new `GetBindingAmbiguities` tests)
  - `GetBindingAmbiguities_AlwaysEmpty_EvenWhenShortNamesCollide` — PASSED
  - `GetBindingAmbiguities_AlwaysEmpty_EvenWhenNoCollisions` — PASSED

Note: Both test projects depend on `Hrot.AI.Behaviors`, which has a pre-existing codegen error (`T11_Aliasing.Registrar.g.cs` CS8669 nullable context + `EmitParseParamsLocal` CS0103 in `BTreeBridgeEmitCore.cs`). These are from uncommitted edits already in the working tree before this batch. Both test projects were built successfully with `--no-dependencies` to avoid rebuilding the broken downstream.

## Developer Insights

- The `DrawInput` guard for `FourParamFull` comes before `GetItems()` is called, so there's no unnecessary exporter query for whole-blackboard actions. This is the correct performance order.
- `HasNoCompatibleVariables` is the single chokepoint — guarding it centrally ensures that any future code path that checks the property (not just `DrawInput`) also gets the correct result.
- The `BTREE0002` warning about `T10_MultiAction.btree.json` referencing `BrainBlackboard` is a residue of an earlier auto-variable created with the wrong type — it was already present before this batch.

## Known Issues

- The `DelegateShape` is not yet surfaced in the BTree facet struct (`BTreeActionFacet.DelegateShape` field does not exist). Condition and Action facets today only carry `MethodFqn` and `ExpressionTargetField`. If the facet is needed for read-only display in the inspector, it should be added to the facet and `BuildActionFacet`/`BuildConditionFacet` should populate it. Currently the shape is only read from the asset node via context, which is correct for the drawer but means the facet itself doesn't carry the value.
- Fix 2 tooltip is not testable headlessly. If the team wants to validate tooltip text in tests, a separate `TooltipText` constant on the drawer class could be added.

## Suggested Commit Message

`fix(btree-editor): guard picker+Promote for FourParamFull, add tooltip, suppress false-positive collision strip`
