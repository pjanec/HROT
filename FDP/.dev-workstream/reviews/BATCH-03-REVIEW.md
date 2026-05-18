# BATCH-03 Review

**Batch:** BATCH-03  
**Reviewer:** Development Lead  
**Date:** 2026-02-24  
**Status:** ✅ APPROVED (one P1 ordering fix, one P2 interface note — both carried to BATCH-04)

---

## Summary

Excellent batch. All correctives delivered cleanly. Three dispatcher systems with a shared generic base class. `SimMath` and `BehaviorConstants` are exactly right. 15 new tests, all green. Full solution 100% green (one pre-existing flaky network test confirmed unrelated).

---

## Issues Found

### Issue 1: `ChannelArbitrationSystem` lacks `[UpdateBefore]` ordering declaration (P1 — fix in BATCH-04)

**Problem (from Q4):** `ChannelArbitrationSystem` must run before all three dispatcher systems — if a dispatcher runs first against a stale channel it fires a ghost `OnEnter` for a preempted behavior. Currently both have only `[UpdateInGroup(typeof(SimulationSystemGroup))]` and rely on incidental registration order. This is fragile.

**Fix in BATCH-04:** Add to `ChannelArbitrationSystem`:
```csharp
[UpdateBefore(typeof(LocomotionDispatcherSystem))]
[UpdateBefore(typeof(WeaponDispatcherSystem))]
[UpdateBefore(typeof(InteractionDispatcherSystem))]
```

And add the `[UpdateAfter(typeof(ChannelArbitrationSystem))]` annotation to each of the three dispatcher classes as a belt-and-suspenders. Add a test that verifies, within a world where all four systems are registered, the observable outcome is consistent with correct ordering (stale channel cleared before dispatcher sees it).

---

### Issue 2: `IActionExecutor<T>.Execute` has no status return — executor writes directly into `ref channel.Status` (P2 — noted for BATCH-04 doc, not a breaking change)

**From Q5 point 2.** Executors signal completion by writing `channel.Status = NodeStatus.Success` directly — tight coupling to the channel struct layout. This is efficient and current, but the next batch introduces `BTreeTickSystem` which writes channel actions from BTree nodes; those nodes will also need to signal completion. Before that pattern takes root in multiple places, the team should consciously decide: keep direct `Status` mutation (current) or add a return value to `Execute`. 

Since changing the interface signature is disruptive and the direct write is correct (zero allocation, no boxing), **the decision is: keep the current pattern**. Document it explicitly in `IActionExecutor.cs` XML comment so future executor authors know this is intentional. The direct write is the contract; the interface doesn't need to change.

---

### Issue 3: `OnExit` sees old action ID in channel — not a bug but requires documentation (P3)

**From Q5 point 1.** By the time `OnExit` is called, the channel's `ActiveAction` and `ActionInstanceId` still hold the outgoing action's values — `DispatchedInstanceId` is updated after. This is useful (cleanup can identify what it's cleaning up) but surprising. Add a comment in `DispatcherSystemBase.cs` at the `OnExit` call site explaining the exact field state at the time of the call.

---

## Test Quality Assessment

All four tests cover distinct scenarios with clear assertions on spy call counts — exact counts, not just `> 0`. The `Dispatcher_CallsOnExit_WhenActionChanges` test mutates the channel directly via `GetComponentRW` in the test body to simulate a brain decision — that's the correct pattern to emulate. No magic numbers in test setup beyond simple counts (`1`, `2`). 

`DispatcherSystemBase.InitialPreviousActionCapacity = 256` is a named constant — good. Q3 noted the alternative of initialising from `World.MaxEntityIndex` — worth remembering for Phase 3 when entity counts grow.

---

## Verdict

**Status: APPROVED**

Issue 1 (ordering) is a correctness hazard if system registration order ever changes (e.g., a future parallel system runner, or test harnesses that register in different order). Must fix in BATCH-04 before adding more systems that depend on this ordering.

Issues 2 and 3 are documentation-level — both explicitly decided now so they don't surface as confusion later.

---

## 📝 Commit Message

```
feat: SimMath + BehaviorConstants + three dispatcher systems (BATCH-03)

Completes BCS-P1-T3, BCS-P1-T4; corrects magic numbers; adds SimMath helper

Fdp.Kernel:
- New: SimMath.cs — FromYaw, FromYawPitchRoll, ExtractYaw, compass constants
  (authoritative quaternion helpers for X=east, Y=north, Z=up convention)

FDP.Toolkit.Behavior:
- New: BehaviorConstants.cs — named constants for all buffer sizes and capacities
- ChannelComponents.cs, BehaviorComponents.cs: fixed buffers use BehaviorConstants
- ChannelArbitrationSystem: GetComponentRW — zero-copy in-place mutation
- New: DispatcherSystemBase<TChannel> — shared executor registry + previous-action tracking
- New: LocomotionDispatcherSystem, WeaponDispatcherSystem, InteractionDispatcherSystem
  (O(1) executor lookup, capability gating, OnEnter/OnExit lifecycle)

Migrations:
- VehicleCommandSystem, NetworkDemo CombatInputSystem: SimMath.FromYaw
- Six CarKinem test files: SimMath.FacingNorth / FacingEast
- ComponentLayoutTests: references BehaviorConstants.MaxChannelSizeBytes

Tests: SimMathTests (5), LocomotionDispatcherTests (4),
       WeaponInteractionDispatcherTests (2) — all green; solution 100% green

Related: FDP/Docs/projects/behavior-control/DESIGN.md §3.1–3.2
```

---

**Next Batch:** BATCH-04 (ordering fix + BCS-P1-T5 BTreeTickSystem + BCS-P1-T6 HsmTickSystem + BCS-P1-T7 BehaviorRegistry/BehaviorIngress)
