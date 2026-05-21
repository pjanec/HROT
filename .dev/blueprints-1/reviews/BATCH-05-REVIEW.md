# BATCH-05 Review

**Batch:** BATCH-05 -- TH-010 + CT0 AlcUnloadTests SC3 fix
**Reviewer:** Dev Lead
**Verdict:** APPROVED

---

## Summary

BATCH-05 is complete. 90 tests pass, 5 skipped, 0 failures, 0 build errors.
The P2 AlcUnloadTests SC3 defect is correctly fixed. TASK-TH-010 is fully implemented:
`BehaviorRegistry` wiring, `InvokeBTree/Hsm` stubs, and `MockDispatcherSystem<TChannel>`
with all three concrete dispatchers and a 3-test suite. Phase 1 (Test Harness) is now
complete.

---

## Scope Check

- **Corrective Task 0 (CT0):** COMPLETE. `Fixture_AfterMultipleLoads_OldAlcsReclaimedNewestStillLive`
  now correctly loads 3 ALCs, manually unloads the first two, runs `ForceGcReclaim()`, and
  asserts the first two are dead and the third is alive. Two `[NoInlining]` helpers used:
  `LoadThreeGenerations` (isolates Assembly temporaries) and `UnloadFirstTwoAlcs` (calls
  `fixture.UnloadAndReleaseAlc()` to remove from `_activeAlcs` before GC). SC3 is now
  fully covered.

- **TASK-TH-010:** COMPLETE.
  - `BehaviorRegistry BehaviorRegistry { get; }` added to fixture.
  - `HsmActionDispatcher.ClearAll()` called in `Dispose()` before ALC unload.
  - `ResolveRegistrarParam` updated to resolve `BehaviorRegistry`.
  - `InvokeBTreeAction`, `InvokeHsmAction`, `InvokeHsmGuard` stubs added.
  - `MockDispatcherSystem<TChannel>`, `MockLocomotionDispatcher`, `MockWeaponDispatcher`,
    `MockInteractionDispatcher` all created. All three channel types found in engine.
  - `MockDispatcherSystemTests` with 3 tests covering SC1, SC3, SC4.

---

## Design Alignment

### Acceptable Deviations

**`HsmActionDispatcher` is a static class (not an instance type)**
The design anticipated `HsmActionDispatcher` would be a singleton-instance type with
`HsmDispatcher { get; }` as a property on the fixture. The real engine type is a `static class`.
C# does not permit static classes as property types. The correct workaround was applied:
`HsmActionDispatcher.ClearAll()` is called directly in `Dispose()`. No instance property.
The SC1 test verifies `BehaviorRegistry != null` only; the HsmDispatcher part is not testable
via a property assertion due to the language constraint.
Track as P3 debt: update TASK-TH-010 design note about HsmActionDispatcher.

**`IEntityQuery` does not exist -- concrete `EntityQuery` used**
The design referenced `IEntityQuery` as the field type in `MockDispatcherSystem<TChannel>`.
The engine uses the concrete `EntityQuery` class. The fix (`EntityQuery?`) is correct and
idiomatic.

**Aux systems in `TickFrame` now receive `_repo` instead of `View`**
`MockDispatcherSystem<TChannel>.Execute` casts `ISimulationView` to `EntityRepository` for
`GetComponentRW<T>` write access. `MockSimulationView` is not an `EntityRepository`. The fix
passes `_repo` (which is both a `EntityRepository` and an `ISimulationView`) to aux systems.
This is correct and matches the design constraint "MockDispatcherSystem casts ISimulationView
to EntityRepository for writable ref access." The `CountingSystem` test is unaffected.

**`LoadThreeGenerations` + `UnloadFirstTwoAlcs` helpers in AlcUnloadTests**
Not specified in batch instructions, but required by the Debug-JIT pinning issue (extends
DEBT-009: Assembly return values also pin ALCs). Correct application of the established pattern.
New insight: any `LoadTestAssemblyFromBytes` call must also be in a `[NoInlining]` helper
to avoid Debug-JIT keeping the discarded `Assembly` reference alive.

---

## Test Quality Assessment

### MockDispatcherSystemTests (3 tests)

GOOD.
- `Fixture_HasBehaviorRegistry` (SC1): direct null check on the property -- appropriate.
- `MockLocomotionDispatcher_WhenEntityHasActiveAction_IncreasesInvokeCount` (SC3): creates
  entity with `ActiveAction = 1`, calls `TickFrame`, asserts `InvokeCount == 1`. Correct.
  The `RegisterComponent<LocomotionChannel>()` call is present and necessary.
- `MockLocomotionDispatcher_NextStatusLambda_WritesStatusToChannel` (SC4): sets
  `NextStatus = _ => NodeStatus.Running`, calls `TickFrame`, reads channel via
  `GetComponentRO<LocomotionChannel>().Status`. Verifies the lambda's return value is
  written to the channel component. Correct.

Tests 2 and 3 verify actual state values, not just invocation counts. Good.

### AlcUnloadTests CT0 Fix

CORRECT and well-structured. The three-assertion pattern (dead/dead/live) precisely tests SC3.
The two `[NoInlining]` helpers follow the established GC isolation pattern. The design insight
(Assembly temporaries also pin ALCs) is a valuable extension of DEBT-009.

---

## Developer Insights Extraction (for DEBT-TRACKER)

- **DEBT-011 (new):** Assembly objects returned by `LoadTestAssemblyFromBytes` (even when
  the return value is discarded with `_ =`) can be kept alive by the Debug JIT as implicit
  stack locals for the entire calling method's scope. This prevents ALC GC collection, just
  like holding an explicit ALC local variable. Fix: move ALL `LoadTestAssemblyFromBytes` calls
  into a `[NoInlining]` helper. Extends DEBT-009 from "ALC locals" to "Assembly locals and
  discarded return values."

- **`HsmActionDispatcher` is static (not singleton instance):** The design assumed a singleton
  instance pattern. Actual engine code is a `static class`. Future design docs should note this.
  Track as P3 design inconsistency.

---

## Test Execution Results

```
Passed!  - Failed: 0, Passed: 90, Skipped: 5, Total: 95, Duration: ~460 ms
```

---

## Suggested Git Commit Message

```
feat(blueprints): BATCH-05 -- TH-010 complete, Phase 1 Test Harness done

- Fix: AlcUnloadTests SC3 now properly verifies old ALC reclaim after Unload()
- Add: UnloadAndReleaseAlc() to BlueprintTestFixture for controlled ALC release
- Add: BehaviorRegistry property on BlueprintTestFixture
- Add: HsmActionDispatcher.ClearAll() called in Dispose() before ALC unload
- Add: InvokeBTreeAction / InvokeHsmAction / InvokeHsmGuard stubs (Phase 3)
- Add: MockDispatcherSystem<TChannel> abstract base
- Add: MockLocomotionDispatcher, MockWeaponDispatcher, MockInteractionDispatcher
- Add: MockDispatcherSystemTests -- 3 tests (SC1/SC3/SC4)
- Tests: 95 total (90 pass, 5 skip)
- Phase 1 (Test Harness) -- all tasks complete
```

---

## TASK-TRACKER Updates

- [x] TASK-TH-010 -- COMPLETE
