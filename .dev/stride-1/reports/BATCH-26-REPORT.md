# BATCH-26 Report — Assembled-Subsystem Integration Tests + F6/F7 Root-Cause Fixes

**Date:** 2026-06-05
**Branch:** `blueprint-integ-1`
**Scope:** Build headless assembled integration tests for F6 (infantry crowd nav) and F7 (vehicle navmesh nav) failures; fix all remaining root causes; green CI across Stride test suites.

---

## 1. Problem Statement

BATCH-24 and BATCH-25 fixed the F6 and F7 GPU failures at the system level. However, those fixes were validated only through unit tests (individual systems in isolation) and the BATCH-25 harness workaround (stripping `VehicleState` after spawn). The "assembled system broken, units green" failure class was never headlessly reproduced: the full `EditorStrideSubsystem` stack was not exercised end-to-end in a test.

BATCH-26 closes this gap with five assembled integration tests in `HrotStrideApp.Game.Tests/AssembledNavIntegrationTests.cs`, and fixes two additional root causes discovered during the assembly process.

---

## 2. Changes Made

### STEP 1 — Assembled Integration Tests (`AssembledNavIntegrationTests.cs`)

Five new `[Fact]` tests in `HrotStrideApp.Game.Tests`:

| Test | What it proves |
|------|---------------|
| `B26_F6_1_InfantrySpawnedViaTranslator_HasNoVehicleState_AfterFix` | Infantry (Capsule) has no `VehicleState` after the translator fix (STR-D20 Option a). |
| `B26_F6_2_InfantryMoveTo_BridgeEnrolls_AndProducesNonzeroMotorIntent` | Full assembled proof: spawn infantry → register DtCrowd agent with L-corridor navmesh → tick 30 frames → `CrowdMotorIntent.Velocity` > 0. |
| `B26_F6F7_Shape_VehicleHasVehicleState_InfantryDoesNot` | Translator fix is scoped correctly: APC still has `VehicleState`; infantry does not. |
| `B26_F7_1_VehicleSpawn_SimTransformMatchesRequestedPosition` | APC spawn position is preserved after materialisation (within 0.5 m tolerance). |
| `B26_F7_2_VehicleProductionIntent_PlansPath_AndWritesNonzeroVehicleState` | Full assembled proof: spawn APC → set `NavigationIntent` → tick → corners > 0 + `VehicleState.Speed` > 0. |

All five tests use the **real** `EditorStrideSubsystem` (DotRecast, TKB translators, kernel, CgfPack, StrideKinematicsModule), NoOp physics, and synthetic geometry. No stubs for navigation logic.

### STEP 2a — STR-D20 Fix (Option a): `VehicleKinematicsTkbTranslator` scoped to OrientedBox

**File:** `FDP/Toolkits/Fdp.Toolkits/CarKinem/Tkb/VehicleKinematicsTkbTranslator.cs`

**Root cause:** The translator injected `VehicleState` + `VehicleParams` onto every TKB-spawned entity carrying a `VehicleParametersDto`, including infantry (TKB 2002) which carries a `VehicleParametersDto` to control walk speed. `NavigationIntentBridgeSystem` guards crowd-registration with `!HasComponent<VehicleState>` — so infantry was never enrolled in DtCrowd.

**Fix:** Added a `StrideRenderModelDefDto` check — only add `VehicleState`/`VehicleParams` when `dto.ShapeKind == CollisionShapeKind.OrientedBox`. Infantry has `ShapeKind == Capsule` and is now excluded. Vehicles (OrientedBox) are unaffected.

This is the authoritative fix for STR-D20. The BATCH-25 harness workaround (stripping `VehicleState` post-spawn) becomes redundant. The BATCH-25 regression tests (B25-B1/B2) remain green.

### STEP 2b — F6 Root Cause 2: `FdpNavigationOrders.IssueMoveTo` — `BehaviorInstanceId` claim

**File:** `Stride/Hrot.Stride.Core/FdpNavigationOrders.cs`

**Root cause (discovered during assembled test assembly):** `ChannelArbitrationSystem` clears any `LocomotionChannel` whose `BehaviorInstanceId != BehaviorState.InstanceId`. When `IssueMoveTo` sets `ActiveAction=ActionIdMoveTo` but leaves `BehaviorInstanceId=0`, and `BehaviorTkbTranslator` initialises `BehaviorState.InstanceId=1` at spawn, the arbitration system clears the channel on the NEXT tick — the MoveTo order lives for exactly 0 ticks.

**Fix:** `IssueMoveTo` now stamps `ch.BehaviorInstanceId = world.GetComponent<BehaviorState>(entity).InstanceId` when the entity carries a `BehaviorState`. This prevents the channel from being swept by the arb system.

### STEP 2c — F6 Root Cause 3: `NavigationIntentBridgeSystem` — DtCrowd placement with start position

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/NavigationIntentBridgeSystem.cs`

**Root cause:** The bridge's `LocomotionChannel` path called `_dtCrowd.RegisterAgent(entity, params)` (no start position), placing the DtCrowd agent at `(0,0,0)` in crowd space. DtCrowd found no polygon at `(0,0,0)` (it's off the baked navmesh), set the agent state to `DT_CROWDAGENT_STATE_INVALID`, and produced `dvel=0`.

**Fix:** The bridge now calls `RegisterAgent(entity, params, startPositionFdp)` passing the entity's current `SimTransform.Position` as the crowd start position. DtCrowd snaps to the nearest polygon, ensuring valid placement from frame 1.

**Interface change:** `IDtCrowdProvider` gained a new overload `RegisterAgent(Entity, in CrowdAgentParams, Vector3 startPositionFdp)`. All implementations updated:
- `DotRecastDtCrowdProvider` — snaps to nearest polygon via `TrySnapToNavmesh`
- `FakeDtCrowdProvider` — delegates to no-position overload (start position ignored)
- `EngineBackedDtCrowdProvider` — no-op, returns true
- `SpyDeferredCrowd` (test class in `StrD21NavigationFixTests.cs`) — delegates to no-position overload

---

## 3. Files Changed

| File | Change |
|------|--------|
| `Stride/HrotStrideApp.Game.Tests/AssembledNavIntegrationTests.cs` | NEW — 5 assembled integration tests |
| `FDP/Toolkits/Fdp.Toolkits/CarKinem/Tkb/VehicleKinematicsTkbTranslator.cs` | STR-D20 Option a: gate VehicleState on OrientedBox shape |
| `Stride/Hrot.Stride.Core/FdpNavigationOrders.cs` | IssueMoveTo: claim BehaviorState.InstanceId into BehaviorInstanceId |
| `FDP/Toolkits/Fdp.Toolkits/Navigation/IDtCrowdProvider.cs` | New RegisterAgent overload with startPositionFdp |
| `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/NavigationIntentBridgeSystem.cs` | Call RegisterAgent with entity's SimTransform.Position |
| `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeDtCrowdProvider.cs` | Implement new RegisterAgent overload (delegates to no-pos) |
| `FDP/Toolkits/Fdp.Toolkits/Navigation/EngineBacked/EngineBackedDtCrowdProvider.cs` | Implement new RegisterAgent overload (no-op) |
| `Stride/HrotStrideApp.Game.Tests/StrD21NavigationFixTests.cs` | SpyDeferredCrowd: implement new RegisterAgent overload |
| `.dev/stride-1/DEBT-TRACKER.md` | STR-D20 → RESOLVED; STR-D21 updated with BATCH-26 summary |

---

## 4. Root Cause Summary (F6)

Three independent root causes all had to be fixed before the assembled F6 test passed:

1. **STR-D20 (VehicleState on infantry):** `VehicleKinematicsTkbTranslator` injected `VehicleState` onto infantry. `NavigationIntentBridgeSystem`'s `!HasComponent<VehicleState>` guard excluded infantry from crowd registration. Fix: translator now checks `ShapeKind == OrientedBox`.

2. **ChannelArbitrationSystem sweep:** `IssueMoveTo` wrote `ActiveAction=1` but left `BehaviorInstanceId=0`. On the next tick, `ChannelArbitrationSystem` cleared the channel (BehaviorState.InstanceId=1 ≠ 0). The MoveTo lasted 0 ticks. Fix: `IssueMoveTo` stamps the entity's current `BehaviorState.InstanceId`.

3. **DtCrowd at (0,0,0):** `NavigationIntentBridgeSystem` called `RegisterAgent` without a start position, placing the agent at crowd-space origin `(0,0,0)`. The navmesh doesn't cover (0,0,0), so DtCrowd marked the agent `INVALID` and produced `dvel=0`. Fix: bridge passes `SimTransform.Position` as start position; DtCrowd snaps to nearest polygon.

---

## 5. Root Cause Summary (F7)

The BATCH-24 Step-2b ordering fix (run `VehicleNavigationIntentSystem` before the motor at Step 2b) was already in place. The assembled B26_F7_2 test confirmed it works end-to-end: spawn position is preserved, path is planned, `VehicleState.Speed > 0`.

The STR-D20 Option-a translator fix has no effect on F7 (vehicles are OrientedBox and still get `VehicleState`).

---

## 6. Test Results

### Build
```
Solution: 0 errors, 40 warnings (pre-existing xUnit2013 xref warnings)
```

### Stride Test Suites
| Suite | Tests | Passed | Failed | Notes |
|-------|-------|--------|--------|-------|
| `Hrot.Stride.Core.Tests` | 327 | 327 | 0 | All green |
| `Hrot.Stride.Animation.Tests` | 48 | 48 | 0 | All green |
| `HrotStrideApp.Game.Tests` | 208 | 208 | 0 | All green, incl. 5 new B26 tests |

### Toolkit/Scenario Suites (modified shared code)

> **Correction (post-implementation verification 2026-06-05):** The counts below supersede the
> original draft numbers.  The draft incorrectly stated 25 Scenarios failures (baseline was already
> 30 at HEAD = BATCH-25).  BATCH-26 introduces **zero new failures** in either suite.

| Suite | Tests | Passed | Failed | BATCH-26 regression | Notes |
|-------|-------|--------|--------|---------------------|-------|
| `Fdp.Toolkits.Tests` | 1869 | ~1831–1828 | ~38–41 | **0 new** | Failures vary per run due to pre-existing test-isolation flakiness (global-state contamination). BATCH-26 changes do not cause any new failure when tests are run in isolation. Baseline (HEAD without BATCH-26) shows the same variation. |
| `Fdp.Examples.Scenarios.Tests` | 68 | 38 | 30 | **0 new** | Identical failure set at baseline (stash-isolated) and with BATCH-26. The 30 pre-existing failures are: 9 DistributedTank, 5 ComponentDamage, 5 UrbanCombatNew, 4 BallisticsAndHit, 4 SensorGrid, 3 DdsMsgSerialization — all pre-existed before BATCH-13. The STR-D15 debt entry records "25" (measured at BATCH-13 time on a branch that had the FDP-G04 Event-2030 fix); the 5-test delta was introduced by a diverging branch not yet merged here. Not caused by BATCH-26. |

---

## 7. Debt Tracker Updates

- **STR-D20:** Marked **RESOLVED (BATCH-26)**. Translator fix (Option a) is the authoritative fix.
- **STR-D21:** Updated to note BATCH-26 assembled tests and three F6 root-cause fixes. GPU confirmation still pending.

---

## 8. Not Done / Out of Scope

- GPU re-confirmation of F6/F7: The assembled headless tests prove the full stack works without GPU physics. Confirmation that the same stack works with Bullet physics (GPU run) remains a human obligation.
- The per-vehicle `[VehicleYaw]` chatty log (STR-D20 secondary note): not addressed.
- The pre-existing `Fdp.Toolkits.Tests` failures (~38–41 per run): not caused by BATCH-26.
- The 5-test baseline delta vs STR-D15 (30 vs 25 Scenarios failures): pre-existed before BATCH-26, caused by branch divergence from the FDP-G04 fix (commit `b200cd14` on main). Closing this requires merging main into blueprint-integ-1; out of BATCH-26 scope.

## 9. Regression Investigation (post-report addition 2026-06-05)

A post-batch verification run was conducted to confirm BATCH-26 introduces no new failures in shared Toolkit/Scenario tests:

- **Method:** stash-isolated baseline vs BATCH-26 applied, both with clean rebuild, run 3× each.
- **Result:** The Scenarios failure set is IDENTICAL (same 30 test names) with and without BATCH-26.
  The translator `ShapeKind == OrientedBox` gating (STR-D20 fix) does NOT break any existing test
  because the existing `VehicleKinematicsTkbTranslatorTests` create templates WITHOUT a
  `StrideRenderModelDefDto`, so `renderDef == null` → `isVehicleShaped = true` and `VehicleState`
  is still stamped — exactly as before.
- **Conclusion:** STR-D20 fix stays RESOLVED.  No revert needed.  The task premise ("30 vs 25, +5
  new failures") was based on a stale Scenarios baseline; the actual baseline at HEAD is already 30.
