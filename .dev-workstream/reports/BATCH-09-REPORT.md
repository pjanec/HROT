# BATCH-09 Report

**Batch:** BATCH-09 — Geographic P1 Fix + Physics P2 Fixes + Phase 5 Combat Start (BCS-P5-T1, T2)  
**Status:** ✅ Complete  
**Build:** 0 errors, 0 new warnings  

---

## ✅ Success Criteria Checklist

- [x] **DEBT-025** — `RotationToPitchRollDeg` static method added; `UpdateEntity` calls it; 6 new tests pass (including integration test)
- [x] **DEBT-021** — `Math.Min` cap applied in `RaycastSolverSystem`
- [x] **DEBT-026** — `PhysicsConstants.MaxBroadphaseCandidates = 64` constant added; used in `RaycastSolverSystem`
- [x] **DEBT-027** — Comment added in `HitResolutionSystem` documenting the raw-index gap
- [x] **DEBT-028** — `Intersection2DTests` Test 4 uses distinct geometry; entry t ≈ 0.30 exit t ≈ 0.70 (spread = 0.40 > 0.3)
- [x] **DEBT-023** — `HitEvent` moved to `FDP.Toolkit.Combat`; Physics no longer defines it
- [x] **P3** — `QueryExpansionMeters` → `QueryExpansionRadius: float`; stale test comment removed
- [x] **BCS-P5-T1** — `WeaponState`, `Health`, `BallisticProjectile`, `FireRequestEvent`, `HitEvent` (migrated); 6 component tests pass
- [x] **BCS-P5-T2** — `AimAndFireExecutor`; 5 tests pass including multi-tick cooldown gating
- [x] **`FDP.Toolkit.Combat` + `FDP.Toolkit.Combat.Tests` added to `FDP.sln`**
- [x] **Full solution build:** 0 errors
- [x] **All tests green** (20 Geographic, 16 Physics, 11 Combat = 47 in affected assemblies; full solution clean)
- [x] **Report submitted**

---

## 📊 Test Results

```
dotnet test FDP.sln  (selected relevant assemblies shown)

Passed! - Failed: 0, Passed:  20, Total:  20 — Fdp.Toolkit.Geographic.Tests
Passed! - Failed: 0, Passed:  16, Total:  16 — FDP.Toolkit.Physics.Tests
Passed! - Failed: 0, Passed:  11, Total:  11 — FDP.Toolkit.Combat.Tests
Passed! - Failed: 0, Passed:  25, Total:  25 — FDP.Toolkit.Behavior.Tests
Passed! - Failed: 0, Passed:  17, Total:  17 — FDP.Toolkit.Navigation.Tests
Passed! - Failed: 0, Passed:  18, Total:  18 — FDP.Toolkit.Perception.Tests
Passed! - Failed: 0, Passed: 675, Total: 677 — Fdp.Tests (Kernel)
... all other assemblies: 0 failures
```

New tests added this batch: **18** (6 pitch/roll + 1 geometry correction reused + 6 component + 5 executor).  
All pre-existing tests remained green.

---

## Q1 — `RotationToPitchRollDeg` Sign Convention

**Body-frame right axis: `-UnitY`** (body-left axis = `+UnitY`).

**How determined:**  
The codebase uses the UnitX-forward convention throughout (confirmed in `CarKinematicsSystem`, `RotationToHeadingDeg`, and the test suite). In this ENU frame (X=East, Y=North, Z=Up) with body-forward = `+UnitX`, the body-left axis is `+UnitY` (cross-product: UnitX × UnitZ = −UnitY, so to get left from forward we need the convention-specific body frame).

Empirical confirmation via Test 4 (`RotationToPitchRollDeg_RightWingDown45_ReturnsRollPositive45`):  
`Quaternion.CreateFromAxisAngle(Vector3.UnitX, π/4)` rotates +UnitY (body-left) toward +UnitZ, meaning the left side goes up — by definition the *right* wing goes down. The roll formula `atan2(left.Z, up.Z)` where `left = Transform(UnitY, rotation)` yields:
- `left.Z = sin(π/4) ≈ 0.707`  
- `up.Z = cos(π/4) ≈ 0.707`  
- `atan2(0.707, 0.707) = +45°`  

Result: **+45° = right wing down**, which matches the required convention (`+PitchDeg = nose up, +RollDeg = right wing down`). **No negation was required.**

The confirming test:
```csharp
// Test 4
var q = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 4f);
SimTransformBridgeSystem.RotationToPitchRollDeg(q, out _, out float roll);
Assert.InRange(roll, 43f, 47f);   // +45° → right wing down ✓
```

---

## Q2 — `HitEvent` Migration to Combat

**Changes to `FDP.Toolkit.Physics.csproj`:**  
Added a `<ProjectReference>` to `FDP.Toolkit.Combat`. Physics now depends on Combat (one-way).

**Changes to `HitResolutionSystem.cs`:**  
Updated the `using` directive from `FDP.Toolkit.Physics.Events` to `FDP.Toolkit.Combat.Events`. The system's logic was unchanged.

**Changes to `PhysicsEvents.cs`:**  
File body replaced with a single migration comment; the `HitEvent` type is no longer defined there.

**Circular reference analysis:**  
No circular references were introduced. The dependency graph is:
```
Fdp.Kernel
  └─ FDP.Toolkit.Behavior     (depends on Kernel)
       └─ FDP.Toolkit.Combat  (depends on Kernel + Behavior)
            └─ FDP.Toolkit.Physics (depends on Kernel + Combat)
```
Combat→Physics would create a cycle, but that direction was never needed: `HitEvent` is *published by* Physics and *consumed by* Combat, so Physics→Combat is the correct dependency direction.

---

## Q3 — `WeaponChannel`

**Yes, `FDP.Toolkit.Behavior` already defines `WeaponChannel`** in `Toolkits/FDP.Toolkit.Behavior/Components/ChannelComponents.cs`. No creation was necessary.

**Fields:**
```csharp
public unsafe struct WeaponChannel
{
    public byte  ActiveAction;
    public byte  DoctrineInstanceId;
    public byte  ActionInstanceId;
    public byte  DispatchedInstanceId;
    public NodeStatus Status;
    public fixed byte Params[32];   // AimAndFireParams written here by dispatcher
    public fixed byte State[32];    // Target entity stored here by OnEnter
}
```
`AimAndFireExecutor` writes `AimAndFireParams` into `Params` via unsafe pointer on dispatch, and caches the target `Entity` in `State` during `OnEnter` to avoid re-reading params on every tick.

---

## Q4 — DEBT-027 Severity Assessment

**DEBT-027:** `HitResolutionSystem` stores the LOS target in `TargetVisibleEvent` as a raw entity index (int), not a versioned `Entity` handle. If the entity at that index is destroyed and a *new* entity is created at the same index between the time the LOS event is submitted and the time it is consumed, the consumer will update the threat memory of the *wrong* entity.

**In practice, with the full combat flow now clear:**

After implementing `AimAndFireExecutor`, the flow is:
1. `AimAndFireExecutor` fires → `FireRequestEvent` published.
2. `FireProcessingSystem` (future) spawns bullet entity.
3. `RaycastSolverSystem` processes bullet raycasts → resolves hits.
4. `HitResolutionSystem` publishes `HitEvent` and `TargetVisibleEvent` (with raw index).
5. Threat-memory / perception systems consume `TargetVisibleEvent`.

The window of vulnerability is steps 4→5 (within the same frame, or across a frame boundary if the event bus doesn't swap within the same tick). In a scenario with high entity churn (e.g., a unit is killed in the same tick that a LOS result is resolved for that unit's former slot index, and a new entity immediately reuses that slot), the perception system would incorrectly credit a LOS sighting to the newly-spawned entity.

**Probability in practice:** Low-to-medium. Entity slot reuse within a single frame requires: (a) a destruction event, (b) the entity allocator immediately reusing that slot, and (c) a concurrent LOS event for the same slot. These conditions coincide most often in high-churn spawn/despawn scenarios (e.g., burst events, cluster munitions). However, the error is **silent** (no assertion fires, no crash) and can corrupt threat memory in a determinism-sensitive simulation. The fix (use versioned `Entity` handles throughout) is low-cost and should be prioritised.

---

## Files Modified

| File | Change |
|------|--------|
| `Toolkits/Fdp.Toolkit.Geographic/Systems/SimTransformBridgeSystem.cs` | Added `RotationToPitchRollDeg`; `UpdateEntity` calls it |
| `Toolkits/Fdp.Toolkit.Geographic.Tests/SimTransformBridgeSystemTests.cs` | +6 pitch/roll tests; added `using ModuleHost.Core.Abstractions` |
| `Toolkits/FDP.Toolkit.Physics/PhysicsConstants.cs` | Added `MaxBroadphaseCandidates`; renamed `QueryExpansionMeters`→`QueryExpansionRadius` |
| `Toolkits/FDP.Toolkit.Physics/Systems/RaycastSolverSystem.cs` | `Math.Min` cap; named constant for 64; `QueryExpansionRadius` rename |
| `Toolkits/FDP.Toolkit.Physics/Systems/HitResolutionSystem.cs` | DEBT-027 comment; using → Combat.Events |
| `Toolkits/FDP.Toolkit.Physics/Events/PhysicsEvents.cs` | Replaced with migration comment (DEBT-023) |
| `Toolkits/FDP.Toolkit.Physics/FDP.Toolkit.Physics.csproj` | Added Combat ProjectReference |
| `Toolkits/FDP.Toolkit.Physics.Tests/FDP.Toolkit.Physics.Tests.csproj` | Added Combat ProjectReference |
| `Toolkits/FDP.Toolkit.Physics.Tests/PhysicsTestWorldFactory.cs` | using → Combat.Events |
| `Toolkits/FDP.Toolkit.Physics.Tests/HitResolutionSystemTests.cs` | using → Combat.Events |
| `Toolkits/FDP.Toolkit.Physics.Tests/Intersection2DTests.cs` | Test 4 new geometry |
| `Toolkits/FDP.Toolkit.Physics.Tests/RaycastSolverSystemTests.cs` | Stale comment removed |
| `FDP.sln` | Added Combat + Combat.Tests projects |

## Files Created

| File | Purpose |
|------|---------|
| `Toolkits/FDP.Toolkit.Combat/FDP.Toolkit.Combat.csproj` | New toolkit project |
| `Toolkits/FDP.Toolkit.Combat/CombatConstants.cs` | HitEventId=5001, FireRequestEventId=5002 |
| `Toolkits/FDP.Toolkit.Combat/Components/CombatComponents.cs` | WeaponState, Health, BallisticProjectile |
| `Toolkits/FDP.Toolkit.Combat/Events/CombatEvents.cs` | FireRequestEvent, HitEvent (migrated) |
| `Toolkits/FDP.Toolkit.Combat/Executors/AimAndFireParams.cs` | Channel params struct |
| `Toolkits/FDP.Toolkit.Combat/Executors/AimAndFireExecutor.cs` | IActionExecutor<WeaponChannel> impl |
| `Toolkits/FDP.Toolkit.Combat.Tests/FDP.Toolkit.Combat.Tests.csproj` | Test project |
| `Toolkits/FDP.Toolkit.Combat.Tests/CombatComponentTests.cs` | 6 component/event tests |
| `Toolkits/FDP.Toolkit.Combat.Tests/AimAndFireExecutorTests.cs` | 5 executor tests |
