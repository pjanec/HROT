# BATCH-11 Review

**Batch:** BATCH-11  
**Reviewer:** Development Lead  
**Date:** 2026-02-24  
**Status:** ✅ APPROVED — no issues

---

## Issues Found

None.

---

## Corrective Verification

**Issue 1 (BallisticsSystem group):** `[UpdateInGroup(typeof(PostSimulationSystemGroup))]` confirmed on line 41. `[UpdateAfter(typeof(LinearKinematicsSystem))]` confirmed live on line 42 — not commented out. ✅

**Issue 2 (DamageSystem group):** `[UpdateInGroup(typeof(SimulationSystemGroup))]` confirmed on line 36. No `UpdateAfter` attribute present. ✅

**Issue 3 (Capability stripping):** `caps.Capabilities &= ~(ActorCapabilities.CanMove | ActorCapabilities.CanShoot)` confirmed before `DestroyEntity` on line 81–83. Field names verified against actual `BehaviorComponents.cs` structure (`Capabilities`/`ActorCapabilities`). ✅

**Test for Issue 3:** The two-part (non-lethal baseline + lethal no-exception) approach is the correct pragmatic solution given `FDP_PARANOID_MODE` always being defined. Part A (non-lethal: capabilities NOT stripped) is the direct observable proof. Part B (lethal: no exception thrown + entity dead) proves the strip-then-destroy path executes cleanly. ✅

---

## Test Quality Assessment

**`LinearKinematicsSystemTests` (5 tests):**  
Test 2 (`DoesNotMove_EntityWithVehicleState`) is the most important: entity has all three components including `VehicleState(Speed=10f)`, runs system at dt=1.0, asserts position unchanged. If `Without<VehicleState>()` were missing, this would fail with position=(10,0,0). Clean negative test. ✅

**`MissionDirectorSystemTests` (4 tests):**  
Test 1 runs exactly 31 ticks at 1/60s = 0.5167s — above the 0.5s threshold. Checks both `CurrentPhase == 1` AND `ActiveDoctrineHash == DocB`. ✅  
Test 3 (ReachedDestination) runs one tick with `HasArrived=0` (no advance), then sets `HasArrived=1` and runs again — clean two-tick state transition. ✅  
Test 4 (StopsAtEndOfQueue) checks tick-by-tick: phase 0 fires → phase 1 → mission complete. Third tick wrapped in `Record.Exception()` to assert no IndexOutOfRange. Final assert: `CurrentPhase` stays at 2. ✅

---

## Notable Design Findings

**`PostSimulationSystemGroup` created this batch** (Q4 #4): the group did not exist in `StandardSystemGroups.cs`. This is a significant infrastructure addition — it is now the canonical home for `LinearKinematicsSystem`, `BallisticsSystem`, `CarKinematicsSystem`, and `SpatialHashSystem`. ✅

**`HealthCritical` trigger (Q4 #2):** Correctly deferred with a `TODO (DEBT)` comment documenting the circular dependency. A shared `IHasHealth` interface in `Fdp.Kernel` is the clean resolution path. Adding as DEBT-033.

**`[InlineArray(8)]` for MissionPhaseBuffer (Q4 #1):** Correct solution. C# fixed buffers only accept blittable primitives; `[InlineArray]` (C# 12 / .NET 8) gives the same `Phases[i]` syntax without requiring `unsafe`. ✅

**`MissionDirectorSystem` preemption mechanism confirmed (Q3):** `ChannelArbitrationSystem` compares `channel.DoctrineInstanceId != doctrine.InstanceId` — an `unchecked { doctrine.InstanceId++ }` in `MissionDirectorSystem` is all that's needed. Documented in report. ✅

---

## Verdict

**APPROVED.** All correctives confirmed. `LinearKinematicsSystem` correctly implements DESIGN.md §2.3. `MissionDirectorSystem` correctly implements §8.1 with sound preemption integration. 10 new tests, all well-targeted.

---

## 📝 Commit Message

```
fix+feat: BATCH-10 correctives + LinearKinematicsSystem + MissionDirector (BATCH-11)

Corrective (BATCH-10 Issues 1-3):
  BallisticsSystem: moved SimulationSystemGroup → PostSimulationSystemGroup
    [UpdateAfter(typeof(LinearKinematicsSystem))] now live (LinearKinematics exists)
  DamageSystem: moved InputSystemGroup → SimulationSystemGroup; cross-group [UpdateAfter] removed
  DamageSystem: ActorCapabilityState.Capabilities stripped (CanMove|CanShoot) pre-mortem
    Required for HsmDamageBridgeSystem to detect mobility loss in same frame
    +1 test: Damage_StripsCapabilities_OnLethalHit (two-part design for FDP_PARANOID_MODE)

DEBT-032 — LinearKinematicsSystem (FDP.Toolkit.Physics/Systems/LinearKinematicsSystem.cs)
  PostSimulationSystemGroup, [UpdateBefore(SpatialHashSystem)]
  Query: With<SimTransform>+With<SimVelocity>+Without<VehicleState>
  ForEachParallel: tf.Position += vel.Linear * dt
  +5 tests including VehicleState exclusion proof

PostSimulationSystemGroup added to StandardSystemGroups.cs
  Canonical home: LinearKinematicsSystem, BallisticsSystem, CarKinematicsSystem, SpatialHashSystem

BCS-P6-T1 — MissionPlanQueue + MissionDirectorSystem
  MissionPhase: DoctrineId(int), Trigger(MissionTrigger), TriggerParam(float) — 12 bytes
  MissionPlanQueue: [InlineArray(8)] MissionPhaseBuffer, CurrentPhase, PhaseCount, PhaseElapsedSeconds
  MissionDirectorSystem: Simulation, [UpdateBefore(ChannelArbitrationSystem)]
    Triggers: TimerElapsed, ReachedDestination, UnderAttack (TargetMemory)
    HealthCritical: TODO (DEBT-033) — circular dependency (Combat→Behavior) blocks implementation
    On trigger: queue.CurrentPhase++, doctrine.InstanceId++ → ChannelArbitrationSystem preempts
  +4 tests: timer advance, no-advance, destination flip, queue exhaustion guard

Tests: +10 new (1 Combat, 5 Physics, 4 Behavior); full solution 0 errors, all green
```

---

**Next Batch:** BATCH-12
