# BATCH-20 Report — Navigation via the PRODUCTION NavigationIntent front door (char + vehicle), STR-D19 full discharge

## Implementation Summary

Two independently-demoable parts, both driving entities through the **production FDP navigation
interface** instead of the demo's direct `DtCrowd.RegisterAgent` shortcut.

### STEP 0 — Production front-door research (findings)

**The exact character auto-registration trigger.** `NavigationIntentBridgeSystem.Execute`
(`FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/NavigationIntentBridgeSystem.cs`) has two loops:

1. A `NavigationIntent + NavState` delta loop that maps `NavigationIntent` → `NavState` (Mode,
   FinalDestination, TargetSpeed, ArrivalRadius). This does **not** touch the crowd.
2. A `LocomotionChannel` loop that, on `ch.ActiveAction == NavigationConstants.ActionIdMoveTo`
   (keyed by a changed `ActionInstanceId`), and **only for entities without `VehicleState`**, calls
   `_dtCrowd.RegisterAgent(entity, …)` + `SetAgentTarget(entity, dest)` and adds the `CrowdAgent`
   tag (lines ~214–239).

**Therefore: setting `NavigationIntent` (Mode=DirectPoint, IntentId++) ALONE does NOT register the
crowd agent.** The crowd auto-registration is gated on the **`LocomotionChannel` MoveTo action**
(`ActiveAction=ActionIdMoveTo` + a fresh `ActionInstanceId` + a `MoveToParams` payload) — exactly
what a BehaviorTree/HSM node emits and what `MoveToExecutor.Execute` observes.

**`MoveToExecutor` is NOT ticked in editor_stride.** `EditorStrideSubsystem.Initialize` registers
`NavigationIntentBridgeSystem`, `RouteTrajectorySyncSystem`, and the `StrideKinematicsModule` sim
systems (`SpatialHashSystem`, `FormationTargetSystem`, `VehicleCommandSystem`,
`NavigationExecutionSystem`, `CrowdAgentUpdateSystem`) — but **no `LocomotionDispatcherSystem` /
executor pump**. So `MoveToExecutor.OnEnter` (which would write `NavigationIntent` from the channel
action) never runs.

**Front-door trigger used (Part A):** the F6 demo sets the `LocomotionChannel` MoveTo action the way
a BehaviorTree node would (the bridge's actual consumer) **and** writes `NavigationIntent`
(Mode=DirectPoint, FinalDestination, TargetSpeed, ArrivalRadius, IntentId++) exactly the way
`MoveToExecutor.OnEnter` does — so both the crowd auto-registration path and the
`NavState`/`NavigationStatus` feedback path are exercised identically to production, without ticking
the executor and without any direct crowd-provider call from the harness. This is the highest-fidelity
path available given the editor_stride system set, and it is documented in the F6 case header.

**Vehicles are excluded from the crowd bridge** (`!HasComponent<VehicleState>`), so they need a
separate production system — Part B.

---

### PART A — Character navigation via the production front door (F6)

**`FdpNavigationOrders.IssueMoveTo`** (new, `Stride/Hrot.Stride.Core/FdpNavigationOrders.cs`):
- Writes a `NavigationConstants.ActionIdMoveTo` action into the entity's `LocomotionChannel` with a
  fresh (incremented) `ActionInstanceId` and a `MoveToParams` payload (Destination, ArrivalRadius,
  Speed, LayerMask). Returns the `ActionInstanceId` written.
- Lives in `Hrot.Stride.Core` (which has `AllowUnsafeBlocks`) because writing into the channel's
  `fixed byte Params` buffer needs an `unsafe` context — the GPU app project does not enable unsafe.
  This keeps `unsafe` out of `HrotStrideApp.Game` and makes the front-door write unit-testable.

**F6 "FDP Move Order (char)"** (`RegisterFdpMoveOrderCharCase` / `FdpMoveOrderChar` in
`Stride/HrotStrideApp.Game/StridePhysicsHarnessCases.cs`, index 15 → key **F6**):
- Spawns an InfantrySoldier (TKB 2002, same as F1/F5). Start FDP (−4, 2, 0), goal FDP (4, 13, 0) —
  the straight line crosses an interior wall cluster (same as F5).
- Snaps start/goal to the navmesh (reuses the BATCH-19 `TrySnapToNavmesh`).
- On the first post-resolve frame issues the **production order**: adds `NavAgentProfile`
  (radius 0.3, height 1.8, Infantry layer), `CrowdMotorIntent`, `NavigationStatus`; writes
  `NavigationIntent` (DirectPoint, goal, IntentId++); then calls `FdpNavigationOrders.IssueMoveTo`
  (the bridge's crowd-registration trigger). **No `RegisterAgent`/`SetAgentTarget` call from the harness.**
- Per-frame (~0.5 s) diagnostics: NavigationStatus (phase/result/IntentId), `bridgeRegisteredAgent`
  (probed via `TryGetAgentSnapshot` — proves the *bridge* enrolled the agent), crowd desired velocity,
  `CrowdMotorIntent.Velocity`, `SimVelocity`. Arrival (≤1.5 m) + 60 s timeout-failure logging. Goal
  marker reuses the F5 mechanism.

**Component registration added** (`EditorStrideSubsystem.Initialize`): `NavAgentProfile`
(`World.RegisterComponent<NavAgentProfile>()`) so the bridge sizes the auto-registered agent correctly
and `HasComponent<NavAgentProfile>` is a safe registered-type query.

### PART B — Vehicle navigation via NavigationIntent (new bridge system) (F7)

**`VehicleNavigationIntentSystem`** (new, `Stride/Hrot.Stride.Core/VehicleNavigationIntentSystem.cs`):
- `[UpdateInPhase(SystemPhase.Simulation)] IEcsModuleSystem`. Queries `NavigationIntent + VehicleState
  + SimTransform` — exactly the vehicles the crowd bridge excludes.
- Reads the `INavmeshProvider` **singleton** from the world each tick (`HasSingletonManaged` /
  `GetSingletonManaged`); falls back to an optional injected provider for tests. **Graceful no-op**
  when neither is present (never throws, never mutates `VehicleState`).
- On a **new intent** (IntentId changed, Mode=DirectPoint): converts current pos + FinalDestination
  FDP→navmesh space via `FdpStrideTransform.ToStridePosition`, calls
  `INavmeshProvider.PlanPath(…, NavLayerMask.Vehicle)`, converts corners back to FDP 2-D, and stores
  the corner list + a current-corner index in a managed `Dictionary<Entity, RouteState>` keyed by the
  full generation-safe handle. Drops a leading corner coincident with the start (FindStraightPath
  always emits the start point). On `PlanPath` == 0: writes `NavigationStatus` (Result=**NoPath**),
  halts (Speed=0), logs loudly.
- **Each tick:** runs `VehicleWaypointController.Compute(pos, heading, corner)` → writes
  `VehicleState.Speed`/`SteerAngle`; advances the corner index on arrival; on the final corner sets
  Speed=0 and writes `NavigationStatus` (Result=**Arrived**, echoing IntentId, Phase=Completed).
  Movement-based stuck guard (no displacement over a 3 s window) advances past a wedged corner (same
  pattern as F3/F4).
- Exposes `GetCornerCount(entity)` / `GetCurrentCorner(entity)` / `MinTurningRadiusM` for diagnostics
  and tests.

**Wiring** (`EditorStrideSubsystem.Initialize`): the system is added to the combined `simSystems`
list **after** `strideKinematics.SimulationSystems` (which ends with `NavigationExecutionSystem` +
`CrowdAgentUpdateSystem`) and **before** `UnitHierarchySystem`/`EqsResultUpdateSystem`. Because the
motors (`KinematicVehicleMotor`) run pre-physics in `Tick` and the FDP kernel runs after, the
`VehicleState` this system writes during the kernel's Simulation phase is consumed by the motor on the
next frame's pre-physics step — the same run-order relationship F3/F4 rely on. Exposed as
`EditorStrideSubsystem.VehicleNavIntentSystem`.

**F7 "FDP Move Order (vehicle)"** (`RegisterFdpMoveOrderVehicleCase` / `FdpMoveOrderVehicle`, index 16
→ key **F7**):
- Spawns a MilitaryAPC (TKB 2001). Start FDP (−5, 3, 0), goal FDP (5, 12, 0) — a wall sits between
  (same as F4). Sets **only** `NavigationIntent` (DirectPoint, goal, IntentId++) + `NavigationStatus`.
  **No manual `PlanPath` in the harness** — `VehicleNavigationIntentSystem` does it.
- Per-frame diagnostics: pos, distToGoal, planned corner count + current corner (from the system),
  `VehicleState` (speed/steer), `NavigationStatus`. Arrival (geometric ≤1.5 m OR
  `NavigationStatus.Result==Arrived`), NoPath, and 45 s timeout logging. Goal marker.

**Keymap extension** (`StrideTestHarness.TryGetCaseKey`): F-key range widened from F1–F6 (indices
10–15) to **F1–F12 (indices 10–21)** so F6 (index 15) and F7 (index 16) map correctly. `Keys.F1..F12`
are contiguous in Stride. Verified index→key in the updated `TryGetCaseKey_CoverageTable_MatchesSpec`
test (now asserts the full F1–F12 table + index 22 = out of range).

---

## Design Decisions

1. **`FdpNavigationOrders` helper in `Hrot.Stride.Core` (not the app project).** The channel-write
   needs `unsafe` (the `fixed byte Params` buffer). The GPU app project does not enable unsafe; the
   core library does. Putting the helper there keeps unsafe out of the app and makes the production
   front-door write directly unit-testable (B20-A1/A2 call it).

2. **F6 sets BOTH the LocomotionChannel action AND NavigationIntent.** The bridge's crowd registration
   keys on the channel action; the `NavState`/`NavigationStatus` mapping keys on `NavigationIntent`.
   Writing both reproduces precisely what `MoveToExecutor.OnEnter` + a BehaviorTree node produce in a
   fully-executor-driven node — the highest fidelity reachable without ticking the executor.

3. **`VehicleNavigationIntentSystem` reads the navmesh singleton per-tick, not at construction.** The
   `INavmeshProvider` singleton is registered by `BakeNavmesh` *after* the kernel is initialized, so a
   construction-time capture would be null. Reading it each tick also makes the system robust to a
   future re-bake. The optional `navmeshFallback` ctor arg is purely for headless tests.

4. **System placed in Hrot.Stride.Core, not Fdp.Toolkits CarKinem.** It depends on
   `VehicleWaypointController` and `FdpStrideTransform`, both in `Hrot.Stride.Core`; the batch
   instructions flagged this as the likely home. CarKinem has no knowledge of the Stride swizzle.

5. **Leading-corner drop.** DotRecast `FindStraightPath` always emits the start position as corner 0;
   without dropping it the controller would "arrive" instantly and skip a corner. The system drops
   corner 0 only when it is within the arrival tolerance of the start.

6. **No change to F4/F5.** The earlier demos (direct `PlanPath` for F4, direct `RegisterAgent` for F5)
   are retained as lower-fidelity demos; F6/F7 are the production-interface versions.

---

## Deviations

- **F6 uses the `LocomotionChannel` action rather than ticking `MoveToExecutor`.** The batch spec
  anticipated this ("if MoveToExecutor isn't ticked … set NavigationIntent directly the same way
  MoveToExecutor does, document which"). Finding: the bridge's crowd auto-registration is NOT driven by
  `NavigationIntent` at all — it is driven by the `LocomotionChannel` MoveTo action. So the faithful
  trigger is the channel action (which is also what `MoveToExecutor` consumes via `Execute`, not what
  it writes). F6 sets the channel action exactly as a BehaviorTree node does, plus `NavigationIntent`
  for the `NavState`/status path. No executor was wired (not needed; would add a dispatcher + arbitration
  stack for no fidelity gain in a GPU-only demo). Documented in the F6 case header.

---

## Test Results

```
Stride Core Tests:     Passed 307/307  (+6 new BATCH-20 tests)  [was 301]
Stride Animation:      Passed  48/48
HrotStrideApp.Game:    Passed 136/136  (TryGetCaseKey coverage test updated for F1–F12)
Build (HrotStrideApp.Game): 0 errors, 8 pre-existing warnings (NU1608 NuGet + CS0108 Log-hides-base)
```

New BATCH-20 tests (`Stride/Hrot.Stride.Core.Tests/FdpMoveOrderIntegrationTests.cs`):

| Test | What it proves |
|------|----------------|
| **B20-A1** `CharacterFrontDoor_MoveToChannel_AutoRegistersAgent_AndDrivesNonzeroMotorIntent` | Issuing the production front door (`FdpNavigationOrders.IssueMoveTo`) makes `NavigationIntentBridgeSystem` AUTO-REGISTER the crowd agent (asserts `CrowdAgent` tag + `TryGetAgentSnapshot` succeed — neither true before the order, and no direct `RegisterAgent` call). Then `CrowdAgentUpdateSystem` × 20 ticks → `CrowdMotorIntent.Velocity` magnitude > 0.05 **and** north component (FDP +Y) > 0.1 over the L-corridor (proves real pathfinding around the gap). |
| **B20-A2** `VehicleFrontDoor_MoveToChannel_IsExcludedFromCrowdBridge` | A `VehicleState` entity issued the same MoveTo channel action is NOT tagged `CrowdAgent` and NOT registered with the crowd — the split that motivates Part B. |
| **B20-B1** `VehicleNavSystem_DirectPointIntent_PlansPath_AndSteersTowardFirstCorner` | `VehicleNavigationIntentSystem` over floor+wall geometry plans ≥1 corner from a DirectPoint intent, writes non-zero `VehicleState.Speed` on the first tick, and `NavigationStatus` = InProgress echoing IntentId. |
| **B20-B2** `VehicleNavSystem_ClosedLoop_AdvancesCorners_AndArrivesAtGoal` | Closed-loop: feeds commanded `VehicleState` back through a bicycle integrator; the system advances past the first corner (multi-corner detour) and sets `NavigationStatus.Result=Arrived` (IntentId echoed) within the arrival radius. Asserts the route reached |X| > 4 m — proving it detoured AROUND the wall, not straight through. |
| **B20-B3** `VehicleNavSystem_NoPath_HaltsVehicle_AndReportsNoPath` | Goal off-mesh → `PlanPath` returns 0 → Speed zeroed, `NavigationStatus.Result=NoPath` (IntentId echoed), 0 corners. |
| **B20-B4** `VehicleNavSystem_NoNavmesh_IsGracefulNoOp` | No `INavmeshProvider` singleton + no fallback → `Execute` does not throw and leaves `VehicleState` untouched. |

The B20-B2 floor+wall geometry mirrors the BATCH-18 `PlanPath_WallObstacle_PathDetoursMidpoint`
integration (10 m wall at Z=5, vehicle radius 1.5 m erodes to |X|>6.5 m clear passage).

---

## Developer Insights

1. **The crowd bridge's registration key is the easy thing to get wrong.** It is the `LocomotionChannel`
   *action* (`ActionInstanceId` change), not `NavigationIntent`. A reasonable reading of "set
   NavigationIntent and the bridge registers the agent" is simply false — `NavigationIntent` only feeds
   `NavState`. The B20-A1 test makes this explicit (asserts NOT-registered before the channel order).

2. **`NavAgentProfile` was unregistered in editor_stride.** The bridge guards it with `HasComponent`
   (safe), so absence only meant default agent sizing (radius 0.4 vs Infantry 0.3). Registering it +
   setting it on the F6 entity gives the correct infantry radius.

3. **Namespace collision footgun (STR-D3) bit again:** `Stride.Core.Mathematics` resolves against the
   `Hrot.Stride.Core` namespace inside files in that namespace — needed an `SMath = Stride.Core.Mathematics`
   alias in both the new system and the new test file.

4. **Run-order for the vehicle system is the same relationship F3/F4 already rely on.** The motor runs
   pre-physics in `Tick`, the kernel (where the system runs) after — so the written `VehicleState` is
   consumed the next pre-physics step. This is one frame of latency, identical to the existing
   harness-driven vehicle cases, and invisible at 60 Hz.

---

## Known Issues

1. **GPU verification of F6/F7 pending.** Both demos are headless-proven (B20-A1, B20-B1/B2) but the
   live walk/drive (animated mannequin around the wall; APC steering around the wall) requires the user
   to run `editor_stride` and press F6 / F7. Same posture as F4/F5 before their GPU confirmation.

2. **F6 still adds `NavigationStatus`/`CrowdMotorIntent` from the harness.** These are
   editor_stride-registered components the bridge expects present; a fully node-authored entity would
   carry them from its TKB template. Not a fidelity gap in the *navigation* trigger (the crowd
   registration is entirely the bridge's doing), but the component scaffolding is harness-set.

3. **`VehicleNavigationIntentSystem` does not yet consume `NavigationExecutionSystem`'s replan/
   frustration output.** It runs its own arrival + stuck logic. Integrating with the muscle-tier
   `NavigationStatus` replan policy is a future refinement (Mode-2 territory).

---

## Front-door trigger used (one line)

Character F6: `LocomotionChannel.ActiveAction = NavigationConstants.ActionIdMoveTo` + fresh
`ActionInstanceId` + `MoveToParams` (via `FdpNavigationOrders.IssueMoveTo`) — the BehaviorTree trigger
`NavigationIntentBridgeSystem` consumes to auto-register the DtCrowd agent (it does NOT register on
`NavigationIntent` alone); `NavigationIntent` (DirectPoint, IntentId++) is also written as
`MoveToExecutor.OnEnter` does. Vehicle F7: set `NavigationIntent` (DirectPoint) only — the new
`VehicleNavigationIntentSystem` plans + steers.

## Keys
- **F6** = "FDP Move Order (char)"  (index 15)
- **F7** = "FDP Move Order (vehicle)" (index 16)

## [VERIFY]'d APIs
- `EntityRepository.HasSingletonManaged<T>()` / `GetSingletonManaged<T>()` — returns `T?` (null when
  unset in non-paranoid mode); used by `VehicleNavigationIntentSystem` for the graceful no-op path.
  (Confirmed in `FDP/Engine/Fdp.Core/EntityRepository.cs`.)
- `NavigationIntentBridgeSystem` crowd registration is gated on `LocomotionChannel.ActiveAction ==
  ActionIdMoveTo` + `!HasComponent<VehicleState>` (lines ~188–239) — confirmed by reading the source
  and by the existing `NavigationIntentBridgeCrowdTests`.
- `Keys.F1..F12` contiguous in Stride.Input (used by the extended keymap).

## Suggested Commit Message

`feat(stride): drive char + vehicle navigation through the production NavigationIntent front door — F6 LocomotionChannel MoveTo auto-registers the crowd agent, new VehicleNavigationIntentSystem + F7 demo (BATCH-20, STR-D19 full discharge)`
