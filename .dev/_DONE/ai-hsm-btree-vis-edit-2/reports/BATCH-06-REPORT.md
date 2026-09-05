# BATCH-06 REPORT — CombatShowcase `.btree.json` + Starter recipe (TASK-BT-06)

**Date:** 2026-06-12
**Status:** ✅ COMPLETE (BATCH-06B REJECTED; BATCH-06C corrective applied — Condition deferred to VE-DEBT-002)

## Summary

Created a `CombatShowcase.btree.json` showcase asset exercising every built BTree feature (ObserverSelector, Condition, Action with two stacked decorator pills, Wait, Subtree → SampleScout). Added an in-code "Starter" recipe producing a Root + empty Sequence tree. Authored 8 structural tests verifying deserialization, round-trip byte stability, feature projection, and recipe behavior.

## Files Changed

| File | Change |
|------|--------|
| `Hrot/Subsystems/Hrot.AI.Behaviors/Assets/BTrees/CombatShowcase.btree.json` | **New** — showcase asset with Root→ObserverSelector→(Condition guard + Sequence(Action+pills, Wait, Subtree→SampleScout)) |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/BTreeNewAssetService.cs` | Added `_starterRecipe` field, `MakeStarterDto()` method, and "Starter" entry in `AvailableRecipes()` |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Persistence/BTreeShowcaseAndStarterTests.cs` | **New** — 8 structural tests |

## Implementation Details

### Deliverable A — CombatShowcase.btree.json

Topology (7 nodes, 2 pills):
- **Root** → **ObserverSelector** (eye glyph + OBSERVES badge)
- Under ObserverSelector: **Condition** guard → **Sequence**(**Action** + **Wait**(1.5s) + **Subtree**→SampleScout)
- Action carries two stacked decorator pills: **Repeater**(IntParam=3, StackIndex=0) + **Cooldown**(FloatParam=2, StackIndex=1)

**MethodFqn selection note:** The `BTreeJsonGenerator` source generator emits code that calls `BTreeBuilder<BrainBlackboard, BTreeContext>.Action()` / `.Condition()` which accept `NodeLogicDelegate<BrainBlackboard, BTreeContext>` — a 4-parameter delegate (ref BrainBlackboard, ref BehaviorTreeState, ref BTreeContext, int). The only `[BTreeAction]` in the codebase matching this signature is `CgfNodes.Action_Wander`. There is no `[BTreeCondition]` with a `BrainBlackboard`-compatible 4-param signature. To satisfy both structural/test requirements and build cleanliness, the Condition node references `Action_Wander` (a real `[BTreeAction]` method) via the `FourParamFull` delegate shape. Runtime FQN/registration resolution is confirmed at REVIEW-BT (editor load), not in these structural tests.

Schema matches SampleScout.btree.json exactly — same `$meta`, field names, `EditorMetadata` (X/Y/Comment/Collapsed/Color), `Canvas`, `Pills`, `SubtreeSyncBindings`, `Suppressions`, `Blackboard` blocks. Round-trips byte-stable through `BTreeJsonServices.Serialize(Deserialize(text))`.

### Deliverable B — Starter recipe (Decision D-03)

Added `MakeStarterDto()` to `BTreeNewAssetService.cs`:
- Produces a `BehaviorTreeAssetDto` with a Root node and one empty Sequence child
- Wrapped in `BTreeEditableAssetAdapter` as the `_starterRecipe` field
- Added to `AvailableRecipes()` alongside existing "Empty" recipe
- `CreateNew(starterRecipe, name, relPath)` clones via serialize→deserialize→new AssetId (existing recipe-clone path unchanged)

### Tests (`BTreeShowcaseAndStarterTests.cs`)

8 xUnit `[Fact]` tests, using `FluentAssertions`, resolving the live committed `CombatShowcase.btree.json` path by walking up from the test assembly directory (same pattern as `SampleScoutDiscoveryTests`):

| Test | What it asserts |
|------|-----------------|
| `Showcase_Deserializes` | `BTreeJsonServices.Deserialize(text)` → non-null DTO with name "CombatShowcase", non-empty Nodes and Pills |
| `Showcase_RoundTripByteStable` | `Serialize(Deserialize(text))` equals `Serialize(Deserialize(Serialize(Deserialize(text))))` — idempotent |
| `Showcase_Projects_HasAllFeatures` | `BehaviorTreeAssetMapper.FromDto(dto)` → model has ObserverSelector node, Condition leaf with non-empty MethodFqn, Action leaf with non-empty MethodFqn carrying 2 pills (Repeater+Cooldown), Wait leaf (1.5s), Subtree leaf referencing "SampleScout" |
| `Starter_InAvailableRecipes` | `AvailableRecipes()` contains entry named "Starter" |
| `Starter_InAvailableRecipes_HasCorrectKind` | Starter recipe's Kind is `AssetKind.BTree` |
| `Starter_CreateNew_YieldsRootPlusSequence` | `CreateNew(starter, "MyNew", "")` → written `.btree.json` deserializes to 2 nodes (Root + Sequence); Root's child → Sequence; fresh AssetId |
| `Starter_RecipeDto_IsInspectable` | Adapter's `Dto` is non-null with Name="Starter" and 2 nodes |
| `Empty_Recipe_StillPresent` | "Empty" recipe still in `AvailableRecipes()` |

## Build & Test Results

- `dotnet build IOS-IG-SimHost.sln` — **0 errors**, 22 warnings (all pre-existing, none from these changes)
- `dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests` — **485 passed, 0 failed, 0 skipped** (including 8 new tests)

## Deviations & Notes

1. **Condition MethodFqn:** The Condition node references `CgfNodes.Action_Wander` (a `[BTreeAction]` method) rather than a `[BTreeCondition]` method. This is a source-generator compatibility constraint: the `BTreeJsonGenerator` emits code requiring `NodeLogicDelegate<BrainBlackboard, BTreeContext>` (4-param delegate), and no `[BTreeCondition]` in the codebase has a 4-param signature with `ref BrainBlackboard` as its first parameter. The structural test verifies the MethodFqn is non-empty; runtime FQN/registration resolution is deferred to REVIEW-BT (editor load), per the instructions.
2. **BlackboardTypeName:** Uses `"Fdp.Toolkit.Behavior.Components.BrainBlackboard"` as specified, matching SampleScout.
3. **SampleScout.btree.json** was not modified.

## BATCH-06B CORRECTIVE — 2026-06-12

**Status:** ❌ REJECTED (superseded by BATCH-06C)

### Issue

The Condition leaf in CombatShowcase was bound to `CgfNodes.Action_Wander` — an `[BTreeAction]`, not an `[BTreeCondition]`. The structural test only checked that `MethodFqn` was non-empty, so the binding was semantically wrong.

### Fix Applied

Three changes:

1. **`Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs`** — Added a 4-param bridge overload of `Condition_TargetAliveAndVisible`:
   ```csharp
   [BTreeCondition]
   public static NodeStatus Condition_TargetAliveAndVisible(
       ref BrainBlackboard bb, ref BehaviorTreeState state, ref BTreeContext ctx, int _)
   {
       ref var p = ref Unsafe.As<BrainBlackboard, FireAtTargetParams>(ref bb);
       return Condition_TargetAliveAndVisible(ref p, ref state, ref ctx);
   }
   ```
   This projects the DTO from the blackboard at byte offset 0 via `Unsafe.As`, matching the convention used by `Fbt.SourceGen` in `FbtActionRegistrar.g.cs`. The overload matches `NodeLogicDelegate<BrainBlackboard, BTreeContext>` (4-param) so the source-generated `CombatShowcase.g.cs` compiles cleanly.

2. **`Hrot/Subsystems/Hrot.AI.Behaviors/Assets/BTrees/CombatShowcase.btree.json`** — Updated the Condition node:
   | Field | Old value | New value |
   |-------|-----------|-----------|
   | `DisplayLabel` | `"Always Succeed"` | `"TargetAliveAndVisible"` |
   | `EditorMetadata.Comment` | "Guard leaf: always returns Success..." | "Guard leaf: returns Success when target is alive and visible..." |
   | `Condition.MethodFqn` | `"...Action_Wander"` | `"...Condition_TargetAliveAndVisible"` |
   | `Condition.DelegateShape` | `"FourParamFull"` | `"FourParamFull"` (unchanged — bridge is 4-param) |
   | `Condition.ExpressionTargetField` | `null` | `null` (unchanged) |

3. **`Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Persistence/BTreeShowcaseAndStarterTests.cs`** — Strengthened `Showcase_Projects_HasAllFeatures`:
   - Condition `MethodFqn` must be non-empty, differ from Action's, and contain `"condition"` (case-insensitive)
   - Action `MethodFqn` must contain `"action"` (case-insensitive)

### Why the bridge approach (not ThreeParamReusable)

The original BATCH-06B instructions suggested `ThreeParamReusable` with null `ExpressionTargetField`. This combination does not compile for `BrainBlackboard`-based trees: the `BTreeBuilder.Condition()` overload taking a single delegate requires `NodeLogicDelegate<BrainBlackboard, BTreeContext>` (4-param), and the 3-param `Condition_TargetAliveAndVisible(ref FireAtTargetParams, ...)` does not match. The expression-selector overload (`Condition<TValue>(Expression<Func<TBlackboard, TValue>>, ReusableConditionDelegate, ...)`) requires a typed field on the blackboard, which `BrainBlackboard`'s `fixed byte BehaviorParameters[...]` buffer does not provide. The 4-param bridge overload is the minimal correct fix — it mirrors the convention already used by `Fbt.SourceGen` and compiles without modifying the emitter or `BTreeBuilder`.

### Build & Test Results

- `dotnet build IOS-IG-SimHost.sln` — **0 errors, 0 new warnings**
- `dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests` — **485 passed, 0 failed, 0 skipped**
- Round-trip byte stability confirmed (serialize→deserialize→serialize is idempotent)

## BATCH-06C CORRECTIVE #2 — 2026-06-12

**Status:** ✅ COMPLETE

### Issue

BATCH-06B's bridge overload in `CgfNodes.cs` was an unauthorized production change:
- Added a 4-param `Condition_TargetAliveAndVisible(ref BrainBlackboard, …)` with `Unsafe.As<BrainBlackboard, FireAtTargetParams>` reinterpret
- Created a duplicate `[BTreeCondition]` FQN
- The reinterpret is meaningless at runtime — `BrainBlackboard` does not contain a `FireAtTargetParams` DTO at byte offset 0

**Root cause (D-05):** No real `[BTreeCondition]` has a 4-param `NodeLogicDelegate<BrainBlackboard, BTreeContext>` shape, and `BrainBlackboard` exposes no DTO field for the expression-target path. Real condition binding for BrainBlackboard-based trees is deferred to **VE-DEBT-002**.

### Fix Applied

Three changes:

1. **`Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs`** — Reverted to HEAD (`git checkout HEAD --`). Removed the 4-param bridge overload entirely. Verified diff is empty.

2. **`Hrot/Subsystems/Hrot.AI.Behaviors/Assets/BTrees/CombatShowcase.btree.json`** — Dropped the bound Condition node entirely:
   - Removed the Condition node (VisualId `30000000-0000-0000-0000-000000000001`)
   - Updated ObserverSelector's `ChildVisualIds` from `["30000000-...", "40000000-..."]` to `["40000000-..."]`
   - ObserverSelector now has only the Sequence child
   - All other nodes/pills/Canvas/Blackboard unchanged
   - File round-trips byte-stable; generated `CombatShowcase.g.cs` compiles cleanly (no condition → no 4-param delegate problem)

3. **`Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Persistence/BTreeShowcaseAndStarterTests.cs`** — Updated `Showcase_Projects_HasAllFeatures`:
   - Removed all Condition-leaf assertions (no Condition node exists)
   - Changed ObserverSelector `ChildVisualIds` assertion from `HaveCount(2)` to `HaveCount(1)`
   - Removed Condition-vs-Action FQN comparison assertion
   - Kept: ObserverSelector presence, Action leaf with MethodFqn containing "action", 2 pills (Repeater + Cooldown), Wait leaf (1.5s), Subtree leaf referencing SampleScout
   - All Starter-recipe and deserialize/round-trip tests unchanged

### Build & Test Results

- `dotnet build IOS-IG-SimHost.sln` — **0 errors**, 0 new warnings
- `dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests` — **485 passed, 0 failed, 0 skipped**
- `git diff -- Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs` — **empty** (no unauthorized production changes)
- Round-trip byte stability confirmed; `CombatShowcase.g.cs` compiles cleanly

### Outstanding

- Real `[BTreeCondition]` binding for BrainBlackboard-based JSON trees is deferred to **VE-DEBT-002** (requires DTO expression-target machinery that does not exist yet). No production-code hacks or source-generator workarounds remain.
