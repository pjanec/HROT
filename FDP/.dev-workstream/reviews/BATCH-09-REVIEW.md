# BATCH-09 Review

**Batch:** BATCH-09  
**Reviewer:** Development Lead  
**Date:** 2026-02-24  
**Status:** ✅ APPROVED — no issues

---

## Issues Found

None.

---

## Test Quality Assessment

**`SimTransformBridgeSystemTests` — pitch/roll (6 new tests):**  
Level-flight, nose-up 30°, nose-down 30°, right-wing-down 45°, combined 20°+30°, integration regression guard.  
All use `Assert.InRange` with ±1–2° tolerance — correct for float Euler extraction. Integration test wires up a real `SimTransformBridgeSystem` with a `Mock<IGeographicTransform>`, exercises `Execute`, plays back the command buffer, and asserts `PitchDeg ∈ [18, 22]` — this test would directly catch any regression to the hardcoded-zero bug. ✅

**`AimAndFireExecutorTests` (5 tests):**  
Test 1 asserts `Shooter`, `Target`, direction normalisation (`Length ∈ [0.999, 1.001]`), and `Ammo` decremented — all four observable effects confirmed. ✅  
Test 5 (multi-tick cooldown): loops ticks 1–3 checking no event emitted and `Running`, then fires on tick 4. Checks `CooldownTicksRemaining == 0` after the loop as an intermediate invariant. ✅

**`CombatComponentTests` (6 tests):**  
`HitEvent_HasSameIdAsPhysicsToolkitHitEvent` checks both `CombatConstants.HitEventId == 5001` AND that the `[EventId]` attribute on the type carries that value — double-check guards against a type and constant going out of sync. ✅  
`BallisticProjectile_HasPreviousPosition_NotVelocity` uses `GetField("Velocity") == null` to assert the Phase 0 adaptation field removal is permanent. ✅

---

## Verdict

**APPROVED.** All BATCH-09 correctives applied; FDP.Toolkit.Combat bootstrapped with correct structure; 18 new tests, all passing.

---

## 📝 Commit Message

```
feat: Geographic P1 fix + Physics P2 fixes + Combat toolkit start (BATCH-09)

Completes DEBT-025, DEBT-021, DEBT-026, DEBT-027, DEBT-028, DEBT-023
Completes BCS-P5-T1 (Combat components + events), BCS-P5-T2 (AimAndFireExecutor)

DEBT-025 — SimTransformBridgeSystem: RotationToPitchRollDeg added (was 0f hardcode)
  UnitX-forward, UnitY=body-left; pitchDeg=asin(fwd.Z), rollDeg=atan2(left.Z, up.Z)
  UpdateEntity now calls RotationToPitchRollDeg; GeoTransform egress is correct for all orientations
  +6 tests (level, nose-up, nose-down, right-wing-down, combined, integration regression guard)

DEBT-021 — RaycastSolverSystem: Math.Min cap before Parallel.For
DEBT-026 — PhysicsConstants.MaxBroadphaseCandidates = 64; replaces raw literal; documented
DEBT-027 — HitResolutionSystem: DEBT-027 comment documents raw-index LOS gap
DEBT-028 — Intersection2DTests Test 4: new geometry (r=4, 20-unit ray; entry≈0.30 exit≈0.70)
DEBT-023 — HitEvent migrated from Physics to FDP.Toolkit.Combat.Events
P3 — QueryExpansionMeters → QueryExpansionRadius: const float 5f; stale test comment removed

New project: FDP.Toolkit.Combat + FDP.Toolkit.Combat.Tests
  CombatConstants: HitEventId=5001, FireRequestEventId=5002
  CombatComponents: WeaponState, Health, BallisticProjectile (no Velocity — Phase 0 adaptation)
  CombatEvents: FireRequestEvent, HitEvent (migrated from Physics)
  AimAndFireExecutor: IActionExecutor<WeaponChannel>
    OnEnter: reads AimAndFireParams from channel.Params, stores target in channel.State
    Execute: target-dead→Success, ammo=0→Failure, cooldown>0→decrement, fire→FireRequestEvent
    References SimTransform (not VehicleState) per Phase 0 adaptation

Tests: +18 new (6 geographic, 1 physics geometry fix, 6 combat components, 5 executor)
Full solution: 0 errors, all 772+ tests green
```

---

**Next Batch:** BATCH-10
