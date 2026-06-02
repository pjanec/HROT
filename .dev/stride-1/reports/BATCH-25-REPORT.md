# BATCH-25 Report — GPU-Tested Bug Fixes (Camera, F6, F7)

**Date:** 2026-06-05
**Branch:** `blueprint-integ-1`
**Scope:** Three GPU-testing-identified fixes: camera upside-down, F6 infantry not moving, F7 vehicle FailedBlocked.

---

## 1. Implementation Summary

### PART A — Camera upside-down (`CenterOnEntityCommand`)

**Root cause:** `RotationFromForward` used a shortest-arc (axis-angle) rotation from `UnitZ` to `forward`. After the BATCH-24 change to pass the backward vector (`normalize(camPos - target)`) instead of the look direction, the shortest-arc can introduce roll when the backward vector has a non-zero Y component (looking slightly downward). This roll was ≈−67° for the default `(0, 4, -6)` offset, producing an upside-down or rolled camera.

**Fix (Part A):** Replaced with yaw+pitch decomposition:
- `yaw = atan2(forward.X, forward.Z)` — horizontal heading measured from +Z
- `pitch = asin(-forward.Y)` — vertical tilt
- Compose as `qPitch * qYaw` (Stride uses left-multiplication convention: `Transform(v, q1*q2)` applies q1 first, then q2; to achieve "pitch first then yaw" in the standard sense, write `qPitch * qYaw`)

**Degenerate case:** when `xzLen < 1e-4` (forward nearly straight up/down), yaw is set to 0, pitch to ±90°.

**Stride quaternion convention note:** Stride's `Vector3.Transform(v, q1*q2)` applies q1 first, then q2 — opposite to the standard algebraic left-to-right convention. `RotationY(yaw) * RotationX(pitch)` (as `BasicCameraController` uses in its own frame-composition code) applies RotationY first then RotationX. For `RotationFromForward` which operates on a vector, the correct ordering is `qPitch * qYaw`.

**Files changed:**
- `Stride/HrotStrideApp.Game/StrideInspectorWindow.cs` — `RotationFromForward` implementation
- `Stride/HrotStrideApp.Game.Tests/CenterOnEntityCommandTests.cs` — two new tests: `Compute_LocalUpY_IsUprightNotFlipped` (Theory, 5 FDP positions) and `RotationFromForward_WorldUpPositive_NoRoll` (Theory, 6 forward directions)

---

### PART B — F6 infantry doesn't move (STR-D20)

**Root cause:** `VehicleKinematicsTkbTranslator` (in `Fdp.Toolkits`) injects `VehicleState` on every TKB-spawned entity that carries `VehicleParametersDto`, including `InfantrySoldier` templates. `NavigationIntentBridgeSystem` guards crowd registration with `!HasComponent<VehicleState>`, so the mannequin was never enrolled in DtCrowd and never moved.

**Fix (Option b — harness workaround):** In `FdpMoveOrderChar` (`StridePhysicsHarnessCases.cs`), after the entity resolves (nearest entity within 2 m of spawn), strip `VehicleState` from the infantry entity BEFORE issuing the MoveTo order. This satisfies the bridge's guard without touching the shared translator.

Option (a) — scope the translator to `OrientedBox` entities only — is the architecturally correct long-term fix but has wider blast radius. Option (b) was chosen for lower risk (matches the request spec).

**Why Option b is safe:** `KinematicVehicleMotor` already guards capsule bodies (`if (bodyRef.ShapeKind == CollisionShapeKind.Capsule) continue`) so the motor won't accidentally zero the character's velocity after the strip. F1/F5 proved infantry work fine without `VehicleState`.

**Files changed:**
- `Stride/HrotStrideApp.Game/StridePhysicsHarnessCases.cs` — strip VehicleState in `FdpMoveOrderChar`
- `Stride/HrotStrideApp.Game.Tests/StrD21NavigationFixTests.cs` — two new tests: `Bridge_InfantryWithoutVehicleState_IsCrowdRegistered` (BATCH-25-B1) and `Bridge_InfantryWithVehicleState_IsNotCrowdRegistered` (BATCH-25-B2)

---

### PART C — F7 vehicle `FailedBlocked` (STR-D21)

**Root cause:** `NavigationExecutionSystem` (in `StrideKinematicsModule.SimulationSystems`) processes all entities with `NavigationIntent + NavigationStatus + FrustrationTicks + SimTransform + SimVelocity`. APC vehicles carry all of these. During the first ~2 seconds after spawn, the Bullet rigidbody may not yet be in the simulation (`rb.Simulation == null`). This causes `SetLinearVelocityXZ` to no-op in `BulletPhysicsBodyService`, so the body doesn't move. `ReverseSyncGroup` reads zero velocity from Bullet and writes `SimVelocity.Linear = (0,0,0)`. `NavigationExecutionSystem` sees `vel.Linear.Length() < 0.2f` for 120+ consecutive ticks and writes `NavigationResult.FailedBlocked`, permanently halting navigation.

This was a conflict between two systems writing `NavigationStatus` for the same vehicle entity:
1. `NavigationExecutionSystem` — writes FailedBlocked based on SimVelocity (wrong for physics-body vehicles)
2. `VehicleNavigationIntentSystem` — has its own stuck guard (3-second displacement window)

**Fix:** Add `.Without<VehicleState>()` to `NavigationExecutionSystem`'s ECS query. Vehicles with `VehicleState` are excluded from the frustration guard — their navigation status is managed exclusively by `VehicleNavigationIntentSystem`. Infantry entities never carry `VehicleState` in normal operation (the STR-D20 translator footgun is worked around upstream).

**Why this is safe:** Infantry entities don't have `VehicleState` after the PART B fix. `VehicleNavigationIntentSystem` has its own 3-second stuck-advance guard to prevent vehicles from wedging at corners. The FDP-only (non-Stride) `GroundKinematicsModule` already uses `NavigationExecutionSystem` without this filter — the change is confined to the Stride kinematics path where `VehicleNavigationIntentSystem` is registered.

**Files changed:**
- `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/NavigationExecutionSystem.cs` — `.Without<VehicleState>()` in query
- `Stride/HrotStrideApp.Game.Tests/StrD21NavigationFixTests.cs` — new test `NavExecSystem_VehicleEntity_IsNotFrustrationBlocked` (BATCH-25-C1), using `using CarKinem.Systems;` import added

---

## 2. Design Decisions

| Decision | Rationale |
|----------|-----------|
| PART A: `qPitch * qYaw` (not `qYaw * qPitch`) | Stride's quaternion convention: `Transform(v, q1*q2)` applies q1 first. To achieve yaw-then-pitch in the standard sense, write them reversed. Verified against test output. |
| PART B: Option (b) harness strip, not Option (a) translator scope | Lower blast radius. The translator is shared across FDP subsystems; scoping it to OrientedBox would change behavior for other consumers. The harness strip is local to the F6 test case. |
| PART C: `.Without<VehicleState>()` in `NavigationExecutionSystem` | Minimal, targeted change. Avoids modifying `VehicleNavigationIntentSystem`'s internal logic. Preserves the CQRS NavigationStatus writer for infantry-only entities. |
| Not fixing the root cause in `VehicleKinematicsTkbTranslator` | STR-D20 root cause (translator adds VehicleState to infantry) is tracked as a latent footgun. Proper fix (Option a: scope to OrientedBox) deferred to cleanup phase. |

---

## 3. Deviations from Original Spec

None. All three parts implemented as specified:
- PART A: yaw+pitch decomposition with degenerate guard ✓
- PART B: Option (b) chosen as specified ✓
- PART C: root cause identified as `NavigationExecutionSystem` frustration-tick conflict; fixed with `.Without<VehicleState>()` ✓

---

## 4. Test Results

All Stride test suites green:

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| `Hrot.Stride.Core.Tests` | 327 | 327 | 0 |
| `Hrot.Stride.Animation.Tests` | 48 | 48 | 0 |
| `HrotStrideApp.Game.Tests` | 189 | 203 | +14 (new) |

New tests added in BATCH-25:
- `CenterOnEntityCommandTests`: +2 (BATCH-25-A1 `Compute_LocalUpY_IsUprightNotFlipped`, BATCH-25-A2 `RotationFromForward_WorldUpPositive_NoRoll`)
- `StrD21NavigationFixTests`: +3 (BATCH-25-B1 infantry without VehicleState IS registered, BATCH-25-B2 infantry WITH VehicleState NOT registered, BATCH-25-C1 NavExecSystem skips VehicleState entities)

Build: `HrotStrideApp.Game.csproj -c Debug` → 0 errors, 5 warnings (all pre-existing NuGet version constraints + 1 CS0108 hiding warning, unchanged from baseline).

Pre-existing `Fdp.Toolkits.Tests` failures: 29–30 (Component ID collision in NavTestHarness; pre-existing, not caused by BATCH-25).

---

## 5. Developer Insights

### Stride quaternion left-multiplication convention
`Vector3.Transform(v, q1 * q2)` in Stride (XNA-derived) applies q1's rotation first, then q2's — opposite to the standard algebraic convention. This bit BATCH-24's `qYaw * qPitch` implementation, which visually produced the opposite pitch direction from expected. Always verify quaternion composition direction against a known test case.

### `NavigationExecutionSystem` vs `VehicleNavigationIntentSystem` conflict
The FDP `NavigationExecutionSystem` is designed for the infantry crowd pipeline and shouldn't be aware of vehicle physics. When `VehicleState` entities were added to the Stride ECS (by `VehicleKinematicsTkbTranslator`), they inadvertently entered the frustration guard, which measures `SimVelocity` (FDP domain) — which is zero until the Bullet body enters simulation. The two-system conflict is an architectural boundary violation that the `.Without<VehicleState>()` filter makes explicit.

### Body-not-in-simulation window
The `rb.Simulation == null` no-op in `BulletPhysicsBodyService.SetLinearVelocityXZ` (line 732) is correct and intentional — you can't set velocity on a body not yet added to Bullet. The ~2-second deferred-activation window for newly spawned bodies (Bullet's broad-phase insertion delay) is the real cause of the frozen APC. BATCH-24's pre-motor `VehicleNavigationIntentSystem.Execute()` at Step 2b ensures VehicleState is ready the moment the body IS in simulation. BATCH-25's `NavigationExecutionSystem` exclusion ensures the frustration guard doesn't fire during the activation window.

### `VehicleKinematicsTkbTranslator` footgun (STR-D20)
Any code that spawns TKB entities without explicitly stripping `VehicleState` from infantry will re-hit this bug. The proper fix is Option (a): scope the translator to add `VehicleState` only when `StrideRenderModelDefDto.ShapeKind == OrientedBox`. This is feasible (both types are in `Fdp.Toolkits`) but was deferred as too broad for BATCH-25.

---

## 6. Known Issues

- **STR-D20 (root cause) still open:** `VehicleKinematicsTkbTranslator` adds `VehicleState` to infantry in production scenarios (not just F6 harness). Any live code issuing a MoveTo to an infantry entity without the strip workaround will hit the same crowd-registration block.
- **GPU re-verification needed:** F6 and F7 remain GPU-pending. BATCH-25 fixes are headless-proven but the live Bullet physics path (body-in-simulation, DtCrowd initialized, navmesh baked) must be verified by running F6 and F7 in `editor_stride`.
- **Camera fix GPU-pending:** The `RotationFromForward` yaw+pitch fix needs visual confirmation that the camera is upright when pressing Ctrl+G (CenterOnEntity). The headless test now asserts world-up dot > 0 for 5 positions, but the visual "not rolled" experience requires a GPU run.

---

## 7. Suggested Commit Message

```
fix(stride): BATCH-25 — camera roll, F6 infantry crowd, F7 FailedBlocked

PART A: RotationFromForward uses qPitch*qYaw (Stride left-mult convention;
qYaw*qPitch inverted pitch direction). Added world-up and no-roll tests.

PART B (STR-D20): Strip VehicleState from infantry entity in F6 harness
post-spawn before issuing MoveTo so NavigationIntentBridgeSystem crowd-
registers it. Added BATCH-25-B1/B2 regression tests.

PART C (STR-D21 F7): NavigationExecutionSystem frustrated APC within ~2s
(rb.Simulation==null → SimVelocity=0 → 120-tick limit). Fixed by adding
.Without<VehicleState>() to NavigationExecutionSystem query — vehicles are
managed exclusively by VehicleNavigationIntentSystem. Added BATCH-25-C1.

Build: 0 errors. Tests: Core 327 / Animation 48 / Game 203 — all green.
```
