# BATCH-02 Review

**Batch:** BATCH-02  
**Reviewer:** Development Lead  
**Date:** 2026-02-23  
**Status:** ✅ APPROVED (with one corrective carried to BATCH-03)

---

## Summary

Corrective fix delivered correctly. `FDP.Toolkit.Behavior` project created with all component types, `IActionExecutor<T>` interface, and `ChannelArbitrationSystem`. 9 new tests, all passing. Foundation is solid for Phase 1 systems.

---

## Issues Found

### Issue 1: `ChannelArbitrationSystem` uses `GetComponent`/`SetComponent` (copy round-trip) — use `GetComponentRW` instead (P1, fix in BATCH-03)

**File:** `Toolkits/FDP.Toolkit.Behavior/Systems/ChannelArbitrationSystem.cs` (lines 21–28, 40–46, 58–64)

**Problem:** The developer's Q4 observation (copy overhead on write-back) is valid BUT the proposed solution (a new API) is wrong. `EntityRepository` already has `GetComponentRW<T>()` returning `ref T` directly into the chunk memory — this is the in-place mutation path for main-thread systems. The pattern used throughout `CarKinematicsSystem` for `state`, `nav`, `tf`, `vel` demonstrates this.

`ChannelArbitrationSystem` runs synchronously on the main thread (`[UpdateInGroup(typeof(SimulationSystemGroup))]`). It should cast `World` to `EntityRepository` (or use it directly as `ComponentSystem.World` already is `EntityRepository`) and call `GetComponentRW`.

**Fix:**
```csharp
// Instead of:
var channel = World.GetComponent<LocomotionChannel>(entity);
// ... modify ...
World.SetComponent(entity, channel);

// Use:
ref var channel = ref World.GetComponentRW<LocomotionChannel>(entity);
channel.ActiveAction = 0;
channel.ActionInstanceId++;
channel.Status = NodeStatus.Failure;
// No SetComponent call needed — mutation is in-place
```

This matters especially for dispatcher systems in BATCH-03 which will mutate channels on every frame for every entity.

**Note on Q4 / background systems:** The copy pattern (`GetComponentRO` → modify on stack → `cmd.SetComponent`) is correct and required for async/SoD background modules — those operate on a read-only snapshot shared across threads and must use the `EntityCommandBuffer`. The copy cost there is intentional and negligible (~72 bytes = nanoseconds). Do not confuse the two contexts.

---

### Issue 2: Minor — stale comment in test (cosmetic, fix opportunistically)

**File:** `FDP.Toolkit.Behavior.Tests/ChannelArbitrationTests.cs` lines 86–88

Left-in comment explaining the implementation reasoning inside the test body. Tests should assert, not explain. Remove it.

---

## Test Quality Assessment

Tests are behavioural: stale channel check asserts specific field values (`ActiveAction == 0`, `NodeStatus.Failure`), valid channel asserts unchanged values, empty action asserts skip logic works. Covers all three required scenarios from the spec. `TestWorldFactory` is clean — correct scope of minimal registrations. Good.

Note: `Arbitration_ClearsStaleChannel` line 33 comment says `// default is 0 (Failure)` — this assumes `NodeStatus.Failure == 0`. Verify this is actually true in `Fbt.NodeStatus` definition; if the enum order changes, the test becomes misleading. Consider asserting against the enum value explicitly (the assertion itself does, so this is just a comment cleanup).

---

## Verdict

**Status: APPROVED**

Issue 1 is a correctness-adjacent performance issue (not a bug today, but will create measurable overhead in dispatcher systems that run every frame for every entity). Mandated fix in BATCH-03 Task 0 corrective.

---

## 📝 Commit Message

```
feat: behavior component types + channel arbitration (BATCH-02)

Completes BCS-P1-T1, BCS-P1-T2; fixes BATCH-01 UnitX regression

FDP.Toolkit.Behavior (new project):
- BehaviorComponents.cs: DoctrineState, ActorCapabilityState, SimTier, BrainBlackboard (128b)
- ChannelComponents.cs: LocomotionChannel, WeaponChannel, InteractionChannel (≤96b each)
- BrainComponents.cs: BrainBTreeState (wraps BehaviorTreeState), BrainHsm64, BrainHsm128
- MissionComponents.cs: MissionPlanQueue, MissionPhase, MissionTrigger
- IActionExecutor<TChannel>: OnEnter/Execute/OnExit interface
- ChannelArbitrationSystem: clears stale channels on DoctrineInstanceId mismatch

Corrective (BATCH-01 Issue 1):
- CarKinematicsSystem.GetFormationTarget: UnitY → UnitX
- FormationTargetSystem: UnitY → UnitX
- Regression test: GetFormationTarget_FallbackHeading_MatchesXForwardConvention

Tests: ComponentLayoutTests (6), ChannelArbitrationTests (3),
       Formation regression (1) — all green

Related: FDP/Docs/projects/behavior-control/DESIGN.md §3.1–3.2
```

---

**Next Batch:** BATCH-03 (LocomotionDispatcherSystem + WeaponDispatcherSystem + InteractionDispatcherSystem, with GetComponentRW fix as Task 0)
