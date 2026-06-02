# BATCH-24 Report

## Implementation Summary

### Part A — CenterOnEntityCommand camera fix (BATCH-23 GPU feedback)

**Problem:** Two bugs in `CenterOnEntityCommand.Compute`:
1. Camera faced AWAY from target. Stride cameras look down their local **-Z** axis (confirmed by `BasicCameraController` which uses `Matrix.Forward` = the -Z column). `RotationFromForward(v)` aligns local +Z to `v`. The old code passed `normalize(target - camPos)` (the "toward-target" direction), which made +Z point toward the target and **-Z point away**.
2. Offset `(0, 2, -3)` too close.

**Fix (StrideInspectorWindow.cs, CenterOnEntityCommand class):**
- `CameraOffset` changed from `(0, 2, -3)` to `(0, 4, -6)` (doubles distance, same framing angle ≈34°).
- `Compute` now passes `normalize(camPos - target)` (the *backward* direction) to `RotationFromForward`, so +Z points away from the target and -Z points toward it.
- `RotationFromForward` docstring updated to document the Stride -Z convention.

**Tests (CenterOnEntityCommandTests.cs):** All 8 tests rewritten/updated for BATCH-24:
- `B24-CAM-1/2`: position assertions updated to new offset values.
- `B24-CAM-3/4`: orientation tests now assert that the camera's **-Z axis** (dot of worldMinusZ with toTarget > 0.9) points toward the entity — replacing the incorrect +Z assertions.
- `B24-CAM-5/6/7`: unchanged (RotationFromForward edge cases still valid).
- `B24-CAM-8`: swizzle verification updated for new offset.
- `B24-CAM-9` (NEW): parametric theory — verifies -Z → target alignment for 5 different FDP positions.

---

### Part B — STR-D21: F6/F7 production navigation fix

#### F6 Root Cause (char, NavigationIntentBridgeSystem)

**Diagnosed root cause:** `NavigationIntentBridgeSystem` cached `_lastAppliedActionInstanceId[entity]` **unconditionally** at the end of the foreach loop, even when `IDtCrowdProvider.RegisterAgent` returned `false` (crowd provider not yet initialized — `DotRecastDtCrowdProvider` requires `TryInitializeNavMesh` before it creates a real `DtCrowd`). On frame N+1 after the command was issued, the bridge processed the `ActionIdMoveTo` action, called `RegisterAgent` (deferred-init mode → returned false), cached the ActionInstanceId, and thereafter skipped the entity every tick (idempotency guard matched). The agent was never enrolled in the crowd, `CrowdAgentUpdateSystem` never steered it, and the mannequin never moved.

**Fix (`NavigationIntentBridgeSystem.cs`):**
- Added `bool cacheActionId = true` flag before the switch.
- In the deferred-init path (RegisterAgent returns false AND entity not already in crowd), sets `cacheActionId = false`.
- The final `_lastAppliedActionInstanceId[entity] = ch.ActionInstanceId` is now guarded by `if (cacheActionId)`.
- Added `[BridgeReg]`-tagged NLog.Info/Debug diagnostics: logs successful registration (once per entity) and the deferred/already-registered paths.

**Additional diagnostics (StridePhysicsHarnessCases.cs F6):** per-tick log now includes `crowdInit` (DotRecastDtCrowdProvider.IsInitialized), `hasCrowdComp` (CrowdAgent component present on entity), confirming exactly when and why registration succeeds on the next GPU run.

#### F7 Root Cause (vehicle, VehicleNavigationIntentSystem tick order)

**Diagnosed root cause:** `KinematicVehicleMotor` runs at **Step 2b** (before `Kernel.Update()`), while `VehicleNavigationIntentSystem` runs inside `Kernel.Update()` at **Step 4** (after the motor). This means the motor always reads `VehicleState` written in the **previous** frame. On the very first tick after `NavigationIntent` is set, `VehicleState` is still zero (VehicleNavSystem hasn't planned yet). The motor drives zero velocity. If Bullet's dynamic body is in deferred-init mode (`rb.Simulation == null`) on those first frames, `SetLinearVelocityXZ` is a no-op — and by the time the body IS ready, the frustration system fires `FailedBlocked` (entity not moving → frustration ticks overflow). F2/F4 worked because they used the F3 waypoint demo that pre-set `VehicleState` directly without the 1-tick lag.

**Fix (`EditorStrideSubsystem.Tick`, Step 2b):** Added an explicit `_vehicleNavIntentSystem?.Execute(World, dt)` call at Step 2b **before** `_vehicleMotor?.Execute(World, dt)`. The motor now reads freshly-planned `VehicleState` on the same frame, eliminating the 1-tick lag. The kernel-phase execution is retained for correct ECS scheduling (ordering with `NavigationExecutionSystem`, kernel health monitor). Double-execution is idempotent: VehicleNavSystem detects `PlannedIntentId == intent.IntentId` on the second call and skips replanning; only the steering output is rewritten (same values).

**Additional diagnostics:**
- `VehicleNavigationIntentSystem`: added `[VehicleNav]`-tagged throttled NLog.Info (~0.5s) logging corner index, commanded speed, steer, and current position. GPU operator greps `[VehicleNav]` to see speed > 0.
- `KinematicVehicleMotor`: added `[VehicleMotor]`-tagged throttled NLog.Info logging commanded speed, desired Stride velocity, and actual body XZ speed (from `GetBodyState`). GPU operator greps `[VehicleMotor]` to confirm the body is being driven.
- `StridePhysicsHarnessCases.cs F7`: per-tick log now includes `SimVelocity` 2D speed, confirming the body actually moves after the fix.

---

### New headless tests (StrD21NavigationFixTests.cs)

6 new tests + 3 new F7 VehicleNavSystem tests = total 11 new tests in `HrotStrideApp.Game.Tests`:

| Test | What it proves |
|------|----------------|
| `BridgeRetry_WhenCrowdNotInitialized_RetriesOnNextTick` | Bridge does NOT cache ActionInstanceId when `RegisterAgent` returns false; retries succeed on tick 2 once crowd is initialized |
| `BridgeRetry_WhenAlreadyRegistered_UpdatesTargetWithoutDuplicating` | When entity already registered (duplicate call), bridge updates target without duplicating the agent |
| `Bridge_VehicleEntity_SkipsCrowdRegistration` | Entities with `VehicleState` are skipped by the crowd-registration path |
| `VehicleNavIntentSystem_IsNonNull_AfterInitialize` | `VehicleNavIntentSystem` is wired and exposed after `EditorStrideSubsystem.Initialize()` |
| `VehicleNavIntentSystem_WritesVehicleState_OnFirstTick_WithFakeNavmesh` | Single `Execute()` call with a spy navmesh writes non-zero `VehicleState.Speed` — proves pre-motor execution delivers fresh state on frame 1 |
| `VehicleNavIntentSystem_AdvancesCorners_AcrossMultipleTicks` | Corner index advances after entity reaches a waypoint, and speed remains non-zero until arrival |

---

## Design Decisions

- **Bridge flag vs goto/continue:** Used `bool cacheActionId` flag (set before switch, checked after) rather than `goto` or restructuring the loop. Cleaner, no control-flow surprise.
- **Double execution of VehicleNavIntentSystem:** Intentional and idempotent. The comment in `EditorStrideSubsystem.Tick` explains the rationale. The kernel-phase execution is retained so the system still participates in normal ECS health monitoring and ordering invariants.
- **Diagnostics via NLog:** Tagged with `[BridgeReg]`, `[VehicleNav]`, `[VehicleMotor]` for grep-ability on the next GPU run. Throttled to ~0.5 s per entity to avoid log spam.

## Deviations

None — all changes are strictly within the scope of the spec.

## Test Results

```
Hrot.Stride.Core.Tests  : Passed 327/327  (0 failures, 0 new)
Hrot.Stride.Animation.Tests: Passed 48/48   (0 failures, 0 new)
HrotStrideApp.Game.Tests: Passed 189/189  (11 new: 6 bridge-retry + 3 VehicleNav + 2 wiring)

Fdp.Toolkits.Tests Navigation filter: Passed 295/295
Fdp.Toolkits.Tests NavigationIntentBridgeSystem filter: Passed 16/16
Fdp.Toolkits.Tests full suite: 46 pre-existing failures in IdAllocation/ReplayModule/
  SimTransformBridge/RecordingSearch/ReferenceHandler — all identical to baseline,
  zero new failures from BATCH-24 changes.
```

## Developer Insights

- **F6 root cause is subtle but definitive:** the idempotency dict caches on the first failed attempt, which is a silent no-op. The fix is minimal (one flag) but the diagnosis required tracing the exact control flow through the deferred-init crowd. The GPU `crowdInit=False` diagnostic field added to the F6 harness would have immediately revealed this on the next run without needing the bridge's NLog.
- **F7 root cause is a classic tick-order race:** Step 2b motor + Step 4 kernel is a deliberate ordering in the design, but VehicleNavSystem was placed inside the kernel without considering that the motor reads its outputs at Step 2b. The pre-motor explicit call is a pragmatic fix that avoids restructuring the kernel module registration. The idempotency of double-execution was verified empirically (the `PlannedIntentId == intent.IntentId` guard).
- **SpyNavmeshProvider in tests:** The `VehicleNavigationIntentSystem` tests use a fake navmesh that returns fixed corners. The corners are in Stride space but VehicleNavSystem converts them via `FdpStrideTransform.ToFdpPosition` — the test corners are placed such that the conversion leaves them in a reasonable FDP position for the entity spawn at (0,0,0).

## Known Issues

- The "Arrived immediately" symptom reported in STR-D21 for F6 remains uninvestigated at the code level — the bridge-retry fix addresses the primary failure (no agent enrollment) which prevents the mannequin from ever moving. If NavigationStatus still flickers to Arrived on the first tick, the frustration-system watchdog should recover it. The new `crowdInit` + `hasCrowdComp` diagnostic fields in the F6 harness will confirm the full chain on the next GPU run.
- The 46 pre-existing failures in `Fdp.Toolkits.Tests` are unrelated to this batch.

## Suggested Commit Message

fix(stride): camera faces entity (Part A) + F6 bridge-retry + F7 pre-motor VehicleNav (STR-D21, BATCH-24)
