# BATCH-15 Review — Phase 4: Hot Reload (HR-001, HR-002, HR-003)

**Status:** CHANGES REQUIRED

---

## Summary

Phase 4 implementation is structurally correct: `AiHotReloadCoordinator` is implemented per the design (all 4 patches respected), `BlueprintTestFixture` is updated to use the coordinator for ALC lifecycle management, and the hot reload test suite is written. However, **6 tests fail** due to an ALC GC-reclaim issue in the fixture methods and one structural test bug. The code must NOT be committed until these are fixed.

---

## Test Results

| Filter | Pass | Fail | Skip |
|--------|------|------|------|
| All tests | 341 | 6 | 5 |
| HotReload filter | ? | 6 | 0 |

**Failing tests:**
1. `HotReload.PdbLoadTests.CompileWithPdb_AiPrimitive_AssemblyLoadsSuccessfully` — "1 ALC(s) not GC-reclaimed after 10 retries"
2. `HotReload.FailureRollbackTests.Reload_Failure_DoesNotMutateCurrentAlc` — "1 ALC(s) not GC-reclaimed after 10 retries"
3. `HotReload.QuickReloadTests.QuickReload_UpdatesCurrentAlc` — "2 ALC(s) not GC-reclaimed after 10 retries"
4. `HotReload.SoftReloadTests.SoftReload_InstanceBlueprint_SlotPayloadPreserved` — "1 ALC(s) not GC-reclaimed after 10 retries"
5. `HotReload.AiPrimitiveReloadTests.AiPrimitive_AfterReload_CompilesAndTicksWithoutError` — "2 ALC(s) not GC-reclaimed after 10 retries"
6. `HotReload.AlcLifecycleTests.FailedReload_DoesNotLeakNewAlc` — "Expected 1, Actual 2" (live ALC count assertion inside body)

---

## Root Cause Analysis

### Issue 1: Fixture methods not marked `[NoInlining]` (DEBT-011 pattern)

`CompileAndLoadMany`, `SimulateReload`, `SimulateQuickReload`, and `SimulateReloadWithThrowingRegistrar` are NOT marked `[MethodImpl(MethodImplOptions.NoInlining)]`. 

Per DEBT-009 and DEBT-011: in Debug JIT, if these fixture methods are inlined by the JIT into a test body, their local variables (including `alc`, `assembly`, `roslynCompiler`) become locals of the inlining frame and are kept alive for the duration of that frame. This prevents GC from collecting the ALCs even after they are explicitly `Unload()`'d.

The test body methods ARE correctly marked `[NoInlining]` (they all use the `[MethodImpl(MethodImplOptions.NoInlining)]` attribute + `out WeakReference<...>[]` pattern from DEBT-009). But this is defeated if the fixture methods they call are inlined.

**Fix:** Add `[MethodImpl(MethodImplOptions.NoInlining)]` to:
- `BlueprintTestFixture.CompileAndLoadMany`
- `BlueprintTestFixture.SimulateReload`
- `BlueprintTestFixture.SimulateQuickReload`
- `BlueprintTestFixture.SimulateReloadWithThrowingRegistrar`
- `BlueprintTestFixture.SimulateReloadFromAlc`

### Issue 2: `FailedReload_DoesNotLeakNewAlc` asserts inside body while exception is alive

In `AlcLifecycleTests.FailedReload_DoesNotLeakNewAlc`, the test body calls `Record.Exception(() => fixture.SimulateReloadWithThrowingRegistrar())` and stores the result in `var ex`. Then — while `ex` is still a live local — it calls `fixture.ForceGcReclaim()` and asserts `liveAlcs == 1`.

The exception `ex` is a `TargetInvocationException` whose `InnerException.TargetSite` is `ThrowingRegistrar.Register` — a `MethodBase` from the failed ALC (ALC #2). In Debug JIT, `ex` keeps this `MethodBase` alive, which keeps ALC #2 alive, causing the GC check inside the body to see 2 live ALCs instead of 1.

**Fix:** Isolate the `Record.Exception` call and the `Assert.NotNull(ex)` into a `[NoInlining]` helper method so that `ex` goes out of scope before `ForceGcReclaim` is called. Pattern:
```csharp
[MethodImpl(MethodImplOptions.NoInlining)]
private static void ThrowingRegistrarMustThrow(BlueprintTestFixture fixture)
{
    var ex = Record.Exception(() => fixture.SimulateReloadWithThrowingRegistrar());
    Assert.NotNull(ex);
    // ex goes out of scope when this method returns
}
```
Then in the body:
```csharp
ThrowingRegistrarMustThrow(fixture);
// ex is now out of scope; ALC #2 only held by _alcWeakRefs (weak ref)
fixture.ForceGcReclaim();
var liveAlcs = ...;
Assert.Equal(1, liveAlcs);
```

---

## Coordinator Implementation Assessment

The `AiHotReloadCoordinator` implementation is correct:
- **Patch 1** (`_currentAlc` main-thread-only, no `OldAlc` in `PendingReload`): ✓
- **Patch 2** (`HsmActionDispatcher` is static, no constructor param, throws if injected): ✓
- **Patch 3** (`ApplyQuickReload` owns ALC lifecycle, hands off directly without queuing): ✓
- **Patch 4** (`BlueprintRegistry` injection throws with "RCU contract" message): ✓

`ScanForRegistrars` correctly uses ordinal ordering and only scans for `[BlueprintRegistrar]`.

`ResolveRegistrarArgument` correctly handles all three injection cases.

---

## Test Quality Assessment (once failing tests fixed)

The test structure is sound:
- All GC-reclaim tests use the `[NoInlining]` body + Fact GC loop pattern (DEBT-009) ✓
- `AlcLifecycleTests` tests chained reload sequences (success → failure → success) ✓
- `RegistrarInjectionTests` tests Patch 2 and Patch 4 forbidden types with real compiled assemblies ✓
- `FailureRollbackTests` verifies `_currentAlc` is NOT mutated on failure ✓
- `SoftReloadTests` and `HardReloadTests` test hash-driven behavior (though `GetBlueprintState` is stubbed) ✓

No fake tests or overly simplified assertions. Tests compile real assemblies via Roslyn and verify actual coordinator behavior.

---

## New Tech Debt Items

| ID | Source | Description | Priority |
|----|--------|-------------|----------|
| DEBT-016 | BATCH-15 review | `CompileAndLoadMany`, `SimulateReload`, `SimulateQuickReload`, `SimulateReloadWithThrowingRegistrar`, `SimulateReloadFromAlc` in `BlueprintTestFixture` must be `[NoInlining]` to prevent Debug-JIT ALC pinning. Extends DEBT-011. | P1 → CT0 in BATCH-16 |
| DEBT-017 | BATCH-15 review | `FailedReload_DoesNotLeakNewAlc` body asserts live ALC count while exception `ex` is alive; `ex.InnerException.TargetSite` pins the failed ALC. Fix: isolate `Record.Exception` + assertion into a `[NoInlining]` helper. | P1 → CT0 in BATCH-16 |

---

## Suggested Action

Do NOT commit BATCH-15 code in its current state. Create BATCH-16 with:
- CT0-A: Fix `[NoInlining]` on fixture methods (DEBT-016)
- CT0-B: Fix `FailedReload_DoesNotLeakNewAlc` body (DEBT-017)
- Then verify ALL 347 + HotReload tests pass before committing.
