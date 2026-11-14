# BATCH-HS-07 REPORT — Showcase `.hsm.json` + Starter recipe

**Date:** 2026-06-13  
**Branch:** `blueprint-integ-1`  
**Task:** TASK-HS-07

## Summary

✅ **All deliverables complete. 0 build errors, 0 test failures (454 passed).**

Created `HsmShowcase.hsm.json` (rich showcase HSM machine), added in-code Starter recipe to `HsmNewAssetService`, authored 22 new tests, and fixed a pre-existing generator bug in `HsmBridgeEmitCore.cs` that blocked JSON-owned HSMs from referencing action FQNs.

## Files changed

| File | Action | Notes |
|------|--------|-------|
| `Hrot/Subsystems/Hrot.AI.Behaviors/Assets/HSMs/HsmShowcase.hsm.json` | **NEW** | Showcase machine |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/HsmNewAssetService.cs` | **MODIFIED** | Added `_starterRecipe` + `MakeStarterDto()` |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmShowcaseTests.cs` | **NEW** | 18 tests (showcase + recipe) |
| `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/HsmBridgeEmitCore.cs` | **FIXED** | Generator bug: invalid lambda→fn-ptr cast replaced with valid static local function + `&` |

## Showcase topology

### States (11 user states + synthetic root)
| State | Type | Parent | Notes |
|-------|------|--------|-------|
| `__Root` | Composite | (top-level) | Children: GuardComposite, ParallelWork, EndState |
| `GuardComposite` | Composite | __Root | **IsInitial=true** — avoid CompositeWithoutInitialChild |
| `Idle` | Simple | GuardComposite | **IsInitial=true**; OnEntryAction + ActivityAction = `StubIdle` |
| `HistoryPseudo` | History | GuardComposite | Inside composite with 4 children → avoids HistoryOutsideComposite |
| `Scanning` | Simple | GuardComposite | |
| `AlertState` | Simple | GuardComposite | |
| `ParallelWork` | **Parallel** | __Root | Children in 3 distinct region indices |
| `WorkA` | Simple | ParallelWork | **IsInitial=true**, RegionIndex=0 |
| `WorkB` | Simple | ParallelWork | **IsInitial=false**, RegionIndex=1 (initial via Region's InitialChildStableId) |
| `WorkC` | Simple | ParallelWork | **IsInitial=false**, RegionIndex=2 (initial via Region's InitialChildStableId) |
| `EndState` | **Final** | __Root | No children, no outgoing transitions |

### Regions (4)
- **TopRegion** (RegionIndex=0): wraps __Root
- **RegionA** (RegionIndex=0): parallel region, InitialChildStableId=WorkA
- **RegionB** (RegionIndex=1): parallel region, InitialChildStableId=WorkB
- **RegionC** (RegionIndex=2): parallel region, InitialChildStableId=WorkC

### Events (3)
- `Alert` (EventId=1), `Resolved` (EventId=2), `Emergency` (EventId=3)

### Transitions (3)
- Idle → Scanning on `Alert`, ActionFunction=`StubIdle`, GuardFunction=**null**
- Scanning → AlertState on `Alert`, ActionFunction=null, GuardFunction=**null**
- AlertState → HistoryPseudo on `Resolved`, ActionFunction=null, GuardFunction=**null**

### Global transitions (1)
- → EndState on `Emergency`, GuardFunction=**null**, ActionFunction=null

### StubIdle bindings
- `Idle.OnEntryAction` = `Hrot.AI.Behaviors.CgfHsmNodes.StubIdle`
- `Idle.ActivityAction` = `Hrot.AI.Behaviors.CgfHsmNodes.StubIdle`
- T1 (`Idle→Scanning`).`ActionFunction` = `Hrot.AI.Behaviors.CgfHsmNodes.StubIdle`

## VE-DEBT-004: Guards

✅ **Every transition `GuardFunction` is `null`** (3 regular + 1 global). No guard bindings, no fake guards, no `[HsmGuard]` registrations. Conforms to VE-DEBT-004.

## Validator result

- **0 Error-severity diagnostics**
- **0 Warnings** (OutputLaneConflict did not fire because ParallelWork's children produce no OutputLaneMask — no actions defined in the parallel regions)

Key validations passed:
- ✅ CompositeWithoutInitialChild: NOT fired (GuardComposite has Idle as IsInitial; __Root has GuardComposite as IsInitial)
- ✅ MultipleInitialChildrenInSameParent: NOT fired (ParallelWork has only WorkA as IsInitial; regions handle the rest)
- ✅ HistoryOutsideComposite: NOT fired (HistoryPseudo parent is GuardComposite, which has 4 children)
- ✅ FinalStateWithChildren: NOT fired (EndState has 0 children)
- ✅ FinalStateWithOutgoingTransition: NOT fired (EndState has 0 outgoing transitions)
- ✅ StateDepthExceeded: NOT fired (max depth = 2)
- ✅ EventReferenceDangling: N/A for JSON-loaded assets (EventId=0 in model, check skipped)

## Round-trip

```
Serialize(Deserialize(original_json)) == Serialize(Deserialize(Serialize(Deserialize(original_json))))
```
✅ Byte-stable: two successive round-trips produce identical output (tested in `Showcase_RoundTrip_Is_ByteStable` and `Showcase_TripleRoundTrip_Stable`).

## Starter recipe

`HsmNewAssetService.MakeStarterDto()` produces:
- 1 `__Root` composite + 1 `InitState` simple (IsInitial=true)
- 1 `Region0` (RegionIndex=0, InitialChildStableId=__Root)
- Empty events, transitions, blackboard

Validates with **0 Errors**. Available as a recipe in `AvailableRecipes()` alongside the existing "Empty" recipe.

## Generator fix (`HsmBridgeEmitCore.cs`)

**Problem:** The original generator emitted invalid C# for action thunks:
```csharp
// INVALID: cannot cast lambda to delegate* directly
unsafe { HsmActionDispatcher.RegisterAction(100,
    (System.IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)
    static (...) => { }); }
```

This was a latent bug — no JSON-owned HSM before this batch referenced any action FQN, so it never triggered.

**Fix:** Replaced with a valid static local function + function pointer via `&`:
```csharp
unsafe
{
    static void __hsActionStub(void* inst, void* ctx, HsmCommandWriter* w) { }
    HsmActionDispatcher.RegisterAction(100,
        (System.IntPtr)(delegate*<void*, void*, HsmCommandWriter*, void>)&__hsActionStub);
}
```

Same fix applied to guard generation (guards not exercised in this batch since all GuardFunctions are null).

## Test coverage (22 new tests in `HsmShowcaseTests.cs`)

### Showcase (13 tests)
1. `Showcase_Deserializes_To_NonNull_Dto`
2. `Showcase_Deserializes_To_Valid_Model`
3. `Showcase_RoundTrip_Is_ByteStable`
4. `Showcase_TripleRoundTrip_Stable`
5. `Showcase_Validates_With_Zero_Errors`
6. `Showcase_Has_ParallelState_With_AtLeast_Two_Regions`
7. `Showcase_Has_HistoryPseudoState_Inside_Composite`
8. `Showcase_Has_FinalState_With_No_Children_And_No_Outgoing_Transitions`
9. `Showcase_Has_AtLeast_Two_Events`
10. `Showcase_Has_AtLeast_One_GlobalTransition`
11. `Showcase_Has_Composite_With_Initial_Child`
12. `Showcase_All_Transitions_Have_Null_GuardFunction`
13. `Showcase_Has_AtLeast_One_Transition_With_StubIdle_Action`
14. `Showcase_Has_AtLeast_One_State_Bound_To_StubIdle`
15. `Showcase_Transitions_EventNames_Reference_Defined_Events`

### Starter recipe (7 tests)
16. `StarterRecipe_Is_In_AvailableRecipes`
17. `StarterRecipe_EmptyRecipe_Still_In_AvailableRecipes`
18. `StarterRecipe_Deserializes_To_Valid_Dto`
19. `StarterRecipe_Has_Exactly_One_Initial_State`
20. `StarterRecipe_RoundTrips`
21. `StarterRecipe_Validates_With_Zero_Errors`
22. `StarterRecipe_CanBeCloned_Via_Service`

## Before/after counts

| Metric | Before | After |
|--------|--------|-------|
| Build errors | 0 | 0 |
| Tests passed | 432 | 454 |
| Tests failed | 0 | 0 |
| New tests | — | 22 |

## Not done / deferred

- Visual polish (REVIEW-HS) — reasonable Canvas layout authored but not pixel-art reviewed; deferred to REVIEW-HS.
- Generator fix applied to both action and guard generation; guard path was NOT exercised (no HSM with guards exists) but the same bug was present and fixed proactively.

## Commit

Not committed (per batch instructions).
