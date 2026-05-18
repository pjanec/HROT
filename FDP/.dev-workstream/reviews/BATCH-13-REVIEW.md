# BATCH-13 Review

**Batch:** BATCH-13  
**Reviewer:** Development Lead  
**Date:** 2026-02-25  
**Status:** ⚠️ NEEDS FIX — one P2 issue

---

## Issues Found

### Issue 1: `BehaviorIngressSystem` — `catch` does not correctly fail-safe after partial behavior transition (P2)

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BehaviorIngressSystem.cs` (lines 46–82)

**Problem:** The comment says *"fail safe — leave BehaviorState unchanged"*, but this is incorrect. By the time `ParseParams` throws, the following writes have **already occurred** (before the try block):

1. `behavior.ActiveBehaviorHash = behaviorId;` (line 48)
2. `unchecked { behavior.InstanceId++; }` (line 50)
3. `behavior.BrainTier = def.BrainTier;` (line 51)
4. `btState.State = default;` (line 57)

The `continue` at line 81 skips only the ParseParams result-store — it does **not** roll back the four writes above. The entity is now on the **new behavior** with a **default (zero) blackboard** instead of its old settings or correct new settings.

This can cause subtle AI bugs: the HSM/BTree definition is pointed at (hash set), the InstanceId bumped (preemption triggered), but the blackboard parameters are all zeros. On the next tick the brain runs with garbage inputs.

**Fix:** Move all BehaviorState/BrainBTreeState writes **inside** (or after) the `try/catch`, so that a ParseParams failure leaves the entity entirely on the previous state:

```csharp
// Pre-resolve before any writes:
if (!_registry.TryGetId(evt.BehaviorName, out int behaviorId)) continue;
if (!_registry.TryGetDefinition(behaviorId, out var def)) continue;

// Only write after ParseParams succeeds (or when there are no params to parse).
bool paramsOk = true;
byte[] parsedBlackboard = null; // temp, or use a stackalloc approach

if (def.ParseParams != null && World.HasComponent<BrainBlackboard>(evt.Entity))
{
    ref var blackboard = ref World.GetComponentRW<BrainBlackboard>(evt.Entity);
    var bbPtr = (BrainBlackboard*)Unsafe.AsPointer(ref blackboard);
    try
    {
        def.ParseParams(evt.JsonParams, bbPtr->Memory);
    }
    catch (Exception ex)
    {
        _ = ex; // log when logger available
        paramsOk = false;
    }
}

if (!paramsOk) continue;  // ParseParams failed — entity stays on old behavior entirely

// All good: apply behavior transition.
ref var behavior = ref World.GetComponentRW<BehaviorState>(evt.Entity);
behavior.ActiveBehaviorHash = behaviorId;
unchecked { behavior.InstanceId++; }
behavior.BrainTier = def.BrainTier;
if (World.HasComponent<BrainBTreeState>(evt.Entity))
{
    ref var btState = ref World.GetComponentRW<BrainBTreeState>(evt.Entity);
    btState.State = default;
}
```

However — there is a practical complication: `ParseParams` writes directly into the blackboard memory (`bbPtr->Memory`), so it cannot be "previewed" before committing. The cleanest truly atomic approach would be to shadow-copy the blackboard, attempt parse on the copy, and only if it succeeds write the copy back + update BehaviorState. Given the blackboard is 128 bytes, a `stackalloc` shadow is allocation-free.

An acceptable simpler fix (matching the batch scope): **at minimum**, move the `BehaviorState` and `BrainBTreeState` writes to AFTER the try/catch, so a failure leaves those unwritten. The blackboard may have been partially written, but the brain won't use the new blackboard unless BehaviorState points to it — so if BehaviorState is not updated, the old behavior stays active and the brain ignores the partial parse. This is the practical P2 fix:

```csharp
// Try ParseParams first; only commit behavior switch if it succeeds.
// (Blackboard memory may be partially written on failure, which is acceptable
//  because the old BehaviorState.ActiveBehaviorHash still points to the old definition.)
```

**New required test (replace/extend the DEBT-008 test in `BehaviorIngressSystemTests.cs`):**
```csharp
[Fact] void BehaviorIngress_BehaviorStateUnchanged_WhenParseParamsFails()
// Entity: BehaviorState(ActiveBehaviorHash=OldId, InstanceId=0).
// Register new behavior with failing ParseParams.
// Publish AssignBehaviorEvent for new behavior.
// Run system.
// Assert: ActiveBehaviorHash == OldId (unchanged).
// Assert: InstanceId == 0 (not bumped).
```

---

### Minor P3 finding: `BehaviorIngressSystem.cs` duplicate `using System;`

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BehaviorIngressSystem.cs` (lines 1–2)

Both `using System;` directives are identical. Remove one. No logic impact.

---

## Test Quality Assessment

**`BehaviorRegistryTests` (3 tests):** Clean. Test 2 creates two separate `BehaviorRegistry` instances and registers the same id=42 in both — proves the int key is not derived from instance state or hash randomisation. ✅

**`HsmTickSystemTests.FdpHsmContext_ExposesWorldAccess`:** Verifies `FdpHsmContext.World` is non-null inside a running tick. The `HsmKernelBridge` bridge pattern (Q1) is the correct solution given the `unmanaged` constraint from `Fhsm.Kernel`. ✅

**`LocomotionDispatcherTests.Dispatcher_CallsOnExit_WhenEntityDestroyedMidAction`:** The self-destroying executor pattern is the right approach — Execute() destroys the entity, then the dispatcher's `IsAlive` guard fires and calls OnExit. No crash, OnExit recorded. ✅

**`MissionDirectorSystemTests` HealthCritical tests (2):** Test uses `HealthData{Current=5f, Max=100f}` with threshold=0.1f → Fraction=0.05 ≤ 0.10 → fires. Clean. ✅

**`Intersection2DTests.RaycastCircle_ReturnsZero_WhenRayStartsOnCircleEdge` (DEBT-022):** Addresses the degenerate t=0 case. ✅

**DEBT-031 (`HitEvent` → `Combat.Contracts`):** Dependency graph confirmed clean (Q3 diagram). No circular references. ✅

---

## Verdict

**NEEDS FIX** — Issue 1 (P2) only. The DEBT-008 catch block does not achieve its stated goal; the BehaviorState is partially mutated before the try. Fix ordering, add one test. All other debt items are cleanly resolved.

---

**Required Actions:**
1. Reorder `BehaviorIngressSystem.OnUpdate()`: attempt `ParseParams` first (inside try), then write `BehaviorState` and `BrainBTreeState` only on success.
2. Add test: `BehaviorIngress_BehaviorStateUnchanged_WhenParseParamsFails`.
3. Remove duplicate `using System;` from lines 1–2 (pick one).
4. No re-review of any other file.

---

**Next Batch:** BATCH-14 — Pre-Demo Corrective + Phase 7 Start (BCS-P7-T1, T2, T3)
