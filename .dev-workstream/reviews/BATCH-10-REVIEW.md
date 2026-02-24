# BATCH-10 Review

**Batch:** BATCH-10  
**Reviewer:** Development Lead  
**Date:** 2026-02-24  
**Status:** ⚠️ NEEDS FIXES

---

## Issues Found

### Issue 1: `BallisticsSystem` in wrong system group (P2)

**File:** `Toolkits/FDP.Toolkit.Combat/Systems/BallisticsSystem.cs` (line 39)  
**Problem:** System is declared `[UpdateInGroup(typeof(SimulationSystemGroup))]`. DESIGN.md §10 frame pipeline (lines 502–504) places `BallisticsSystem` in **PostSimulation**, after `FireProcessingSystem` (Simulation) and before `LinearKinematicsSystem` (PostSimulation). The attribute must be `[UpdateInGroup(typeof(PostSimulationSystemGroup))]`.  
**Fix:** Change group attribute. The commented-out `[UpdateBefore]` must also be flipped to `[UpdateAfter(typeof(LinearKinematicsSystem))]` (Ballistics runs **after** LinearKinematics per the design: kinematics advances position first, then Ballistics snapshots it and submits the raycast for that swept segment). See DESIGN.md §10 line 503.

### Issue 2: `DamageSystem` in wrong system group (P2)

**File:** `Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs` (line 35)  
**Problem:** `[UpdateInGroup(typeof(InputSystemGroup))]`. DESIGN.md §10 frame pipeline (line 484) places `DamageSystem` in **Simulation**. The `[UpdateAfter(typeof(HitResolutionSystem))]` constraint is correct but is meaningless when in a different group than `HitResolutionSystem` (which is Input). The fix is: `[UpdateInGroup(typeof(SimulationSystemGroup))]` — `HitEvents` published during Input are available to Simulation systems via the bus swap.  
**Fix:** Change group attribute to `SimulationSystemGroup`. Remove the cross-group `[UpdateAfter]` attribute (it cannot span groups).

### Issue 3: `DamageSystem` missing `ActorCapabilityState` stripping on lethal hit (P2)

**File:** `Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs` (line 72–76)  
**Problem:** DESIGN.md §6.4 and TASK-DETAIL.md §BCS-P5-T5 both specify: on lethal hit, clear `CanMove` and `CanShoot` from `ActorCapabilityState`. The current implementation only destroys the entity — it does not strip capabilities first. For entities that survive multi-hit (Health→0 but they remain in the world for one more frame as a corpse), dispatchers would still perceive them as capable. Also, `HsmDamageBridgeSystem` (BATCH-11) requires `CanMove` being cleared as its trigger — if `DamageSystem` skips this, the HSM damage bridge will never fire.  
**Fix:** Before `World.DestroyEntity(evt.HitEntity)`:
```csharp
if (World.HasComponent<ActorCapabilityState>(evt.HitEntity))
{
    ref var caps = ref World.GetComponentRW<ActorCapabilityState>(evt.HitEntity);
    caps.Flags &= ~(ActorCapabilityFlags.CanMove | ActorCapabilityFlags.CanShoot);
}
```
**New required test:**
```csharp
[Fact] void Damage_StripsCapabilities_OnLethalHit()
// Entity with Health(20f) + ActorCapabilityState(CanMove|CanShoot). Damage=25f.
// Assert: CanMove == false, CanShoot == false (even if entity is destroyed).
// Note: test must snapshot capabilities BEFORE DestroyEntity makes component reads invalid.
```

### Issue 4: `HitEvent` moved to `Fdp.Kernel` — architectural concern (P3)

**File:** `Kernel/Fdp.Kernel/Events/HitEvent.cs` (new file)  
**Problem:** `HitEvent` is a domain-specific combat game event. Moving it to `Fdp.Kernel` breaks the kernel's responsibility as a pure engine layer (no game data). The correct fix for the `Combat → Physics → Combat` circular dependency is a shared event contract assembly or keeping `HitEvent` in a thin `FDP.Toolkit.Combat.Contracts` assembly that Physics can depend on without depending on all of Combat. However, given the current project structure, a pragmatic P3 alternative is to keep `HitEvent` in `Fdp.Kernel` but add a comment documenting the violation and flagging it for cleanup when project structure allows. This does not block approval — it is a tech debt item.  
**Action this batch:** Add DEBT-031 to tracker. No code change required in this batch.

---

## Test Quality Assessment

- `FireProcessingSystemTests` — all 5 tests check actual entity state (position, velocity, collider values, query result counts). ✅
- `BallisticsSystemTests` — Test 5 directly reads `batch.Requests[0].IgnoreEntity` and compares to the shooter entity handle — exactly the right thing to assert. Test 6 pre-fills count to capacity and confirms no overflow. ✅
- `DamageSystemTests` — Test 5 destroys the bullet before system runs and asserts health unchanged (DEBT-027 regression guard). ✅ Missing: capability-stripping test (Issue 3 above).

---

## Verdict

**NEEDS FIXES** — Issues 1, 2, 3 are required before approval.  
**Required Actions:**
1. `BallisticsSystem` → `PostSimulationSystemGroup`, `[UpdateAfter(typeof(LinearKinematicsSystem))]` (commented out pending BATCH-11).
2. `DamageSystem` → `SimulationSystemGroup`, remove cross-group `[UpdateAfter]`.
3. Add `ActorCapabilityState` stripping in `DamageSystem` + one new test.
4. No re-review of other systems necessary — spot check Issues 1–3 only.

---

**Next Batch:** BATCH-11 (LinearKinematicsSystem — unblocking BallisticsSystem ordering + Phase 6 start)
