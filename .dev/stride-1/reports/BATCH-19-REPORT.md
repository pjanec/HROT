# BATCH-19 Report — Real Infantry Crowd Navigation (STR-D19 Discharge)

## Implementation Summary

### STEP 0 — Chain Research (findings)

**Full NavigationIntent → CrowdMotorIntent chain confirmed:**

| Step | Component/System | What it does |
|------|-----------------|--------------|
| 1 | `NavigationIntent` | Goal spec: Mode=DirectPoint, FinalDestination (FDP coords), TargetSpeed, ArrivalRadius, IntentId |
| 2 | `NavigationExecutionSystem` | Monitors intent, checks arrival, frustration/replan logic; writes `NavigationStatus.Result` |
| 3 | `NavigationIntentBridgeSystem` | Translates `NavigationIntent→NavState` on change; on `LocomotionChannel.ActionIdMoveTo` also calls `IDtCrowdProvider.RegisterAgent` + `SetAgentTarget` if entity has no `VehicleState` |
| 4 | `CrowdAgentUpdateSystem` | Each tick calls `dtCrowd.Update(dt, view)` then reads `GetAgentVelocity(entity)` for each entity with `CrowdAgent + CrowdMotorIntent + NavigationStatus`; writes `CrowdMotorIntent.Velocity` |
| 5 | `BulletCharacterMotor` | Reads `CrowdMotorIntent.Velocity`, swizzles FDP→Stride, calls `CharacterComponent.SetVelocity` |
| 6 | Bullet + `BulletReverseSyncSystem` | Physics moves capsule; writes back `SimTransform`/`SimVelocity` → animation bridge picks up velocity and blends idle/walk/run |

**Registration mechanism:** An entity becomes a crowd agent via `NavigationIntentBridgeSystem` processing a `LocomotionChannel` with `ActionIdMoveTo` on entities without `VehicleState`. The system calls `_dtCrowd.RegisterAgent(entity, params)` + `SetAgentTarget(entity, destination)` and adds `CrowdAgent` tag if absent. In the F5 harness case, the registration is done directly (no `LocomotionChannel`) since the demo sets up the crowd agent manually — matching the pattern used by `F1 Physics Walk` for `CrowdMotorIntent`.

**Systems confirmed registered+ticked in editor_stride:**
- `NavigationExecutionSystem` — in `StrideKinematicsModule.SimulationSystems`
- `CrowdAgentUpdateSystem(dtCrowd)` — in `StrideKinematicsModule.SimulationSystems`
- `NavigationIntentBridgeSystem(trajectoryPool, dtCrowd)` — in the combined `simSystems` list (BATCH-19 now passes the crowd provider)
- `INavmeshProvider` singleton — registered by `StrideHrotGame.BakeNavmesh` (BATCH-18)

**Pre-BATCH-19 gap:** `StrideKinematicsModule` was constructed with `FakeDtCrowdProvider` (no-op). `NavigationIntentBridgeSystem` was constructed with no crowd provider (no-arg overload). Neither could register real DtCrowd agents.

---

### STEP 1 — Deferred DotRecastDtCrowdProvider + Wiring

**DotRecastDtCrowdProvider — deferred-init pattern** (`Stride/Hrot.Stride.Core/DotRecastDtCrowdProvider.cs`):
- Added second constructor `DotRecastDtCrowdProvider(float maxAgentRadius = 0.4f)` that leaves `_crowd` and `_navQuery` null.
- Added `bool IsInitialized` property.
- Added `bool TryInitializeNavMesh(DtNavMesh navMesh)` — initializes `DtCrowd` + `DtNavMeshQuery` on first call, returns false if already initialized.
- All `IDtCrowdProvider` methods (`RegisterAgent`, `UnregisterAgent`, `SetAgentTarget`, `Update`) guard `_crowd == null` with early returns (no-op in deferred mode — identical to `FakeDtCrowdProvider` from the caller's perspective).
- Changed `_crowd` and `_navQuery` fields from `readonly` to mutable to support deferred init.

**DotRecastNavmeshProvider — TryGetNavMesh** (`Stride/Hrot.Stride.Core/DotRecastNavmeshProvider.cs`):
- Added `bool TryGetNavMesh(NavLayerMask layer, out DtNavMesh? navMesh)` to expose the baked `DtNavMesh` for a given layer (used by `BakeNavmesh` to extract the Infantry mesh).

**EditorStrideSubsystem** (`Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs`):
- Removed `FakeDtCrowdProvider` and the `Fdp.Toolkit.Navigation.Fake` using.
- Added `public DotRecastDtCrowdProvider? InfantryCrowdProvider { get; private set; }` property.
- Construct a deferred `DotRecastDtCrowdProvider(maxAgentRadius: 0.4f)` at `Initialize` time; assign to `InfantryCrowdProvider`.
- Pass it to `StrideKinematicsModule` (for `CrowdAgentUpdateSystem`) and to `NavigationIntentBridgeSystem` (3-arg constructor with `trajectoryPool + dtCrowd`).
- **Graceful fallback:** the deferred provider acts as a no-op until `TryInitializeNavMesh` is called — no crash if bake fails.

**StrideHrotGame.BakeNavmesh** (`Stride/HrotStrideApp.Game/StrideHrotGame.cs`):
- After baking the navmesh (which already bakes both Vehicle and Infantry per BATCH-18), extract the Infantry `DtNavMesh` via `_navmeshProvider.TryGetNavMesh(NavLayerMask.Infantry, ...)`.
- Call `_editorSubsystem.InfantryCrowdProvider.TryInitializeNavMesh(infantryMesh)` to activate real DtCrowd steering.
- Log success/failure at `Info`/`Warn` level; set `_infantryCrowdProviderInitialized` flag for the F5 harness case.

**Infantry layer params** (from `StrideNavmeshBaker.InfantryParams`): agent radius 0.3 m, height 1.8 m, max slope 60°, step 0.4 m. Max-agent-radius for `DotRecastDtCrowdProvider` proximity grid: 0.4 m (slightly above agent radius).

---

### STEP 2 — F5 "Navmesh Walk" Harness Case

**`StridePhysicsHarnessCases.RegisterNavmeshWalkCase`** (index 14 → key F5):

- Spawns `TKB 2002 InfantrySoldier` at FDP (−4, 2, 0) — west side of arena, south of interior walls.
- Sets goal at FDP (4, 13, 0) — east side, north. The straight-line path crosses interior wall obstacles.
- Adds `CrowdAgent`, `CrowdMotorIntent`, `NavigationStatus` to the entity.
- Calls `DotRecastDtCrowdProvider.RegisterAgent(entity, {radius=0.3, height=1.8, maxSpeed=3, maxAccel=20})` and `SetAgentTarget(entity, goalFdp)`.
- Each frame: `CrowdAgentUpdateSystem` advances the crowd and writes `CrowdMotorIntent.Velocity`; `BulletCharacterMotor` drives the capsule.
- Spawns a visible Box2x1x1 pillar marker at the goal.
- Per-frame log (every 0.5 s): FDP position, distance-to-goal, `CrowdMotorIntent.Velocity`.
- Arrival: dist ≤ 1.5 m → logs "NAVMESH WALK COMPLETE — reached goal (pathfound around obstacle)"; unregisters crowd agent; removes marker.
- Timeout (60 s): logs failure with best distance.
- Guard: if navmesh or infantry crowd not initialized, logs loud error and aborts cleanly.

**Key assignment:** F5 = index 14 (after D1–D9=0–8, D0=9, F1=10, F2=11, F3=12, F4=13). Confirmed: `TryGetCaseKey` maps indices 10–15 → F1–F6.

**What the user should see:** The mannequin spawns west-south of arena interior walls, walks (with idle/walk animation blend from F1's animation pipeline) along a path that visibly curves around the wall clusters to reach the east-north goal pillar marker. The log shows `CrowdMotorIntent.vel` changing each 0.5 s and eventually "NAVMESH WALK COMPLETE".

---

### STEP 3 — Headless Tests (`NavmeshWalkIntegrationTests.cs`)

5 new tests in `Stride/Hrot.Stride.Core.Tests/NavmeshWalkIntegrationTests.cs`:

| Test | What it proves |
|------|---------------|
| B19-SC1: `InfantryLayer_BakesSuccessfully_AndIsRetrivedViaProvider` | Infantry bake + `TryGetNavMesh` returns the mesh; Vehicle layer absent when not baked |
| B19-SC2: `DeferredCrowd_BeforeInit_IsNoOp_AfterInit_IsFunctional` | Deferred provider starts no-op (RegisterAgent=false, vel=zero), then becomes functional after `TryInitializeNavMesh` |
| B19-SC3: `DeferredCrowd_TryInitializeNavMesh_ReturnsFalseOnSecondCall` | Idempotency: second `TryInitializeNavMesh` call returns false |
| B19-SC4: `DotRecastCrowd_AgentWithGoalAcrossWall_ProducesDetourVelocity` | L-corridor navmesh forces north detour: agent at west strip, goal at east strip (connected only via north), velocity has FDP +Y (north) component > 0.2 m/s |
| B19-SC5: `InfantryNavmeshChain_CrowdAgentUpdateSystem_WritesCrowdMotorIntentVelocity` | Full chain: Infantry bake → deferred init → `CrowdAgentUpdateSystem.Execute` × 15 → `CrowdMotorIntent.Velocity.X > 0` (toward east goal) |

SC4 uses a bespoke L-corridor geometry: west strip X=[−12,0], Z=[−5,+15]; east strip X=[0,+12], Z=[+5,+15]. The direct east route at Z≈0 is not walkable east of X=0, so the crowd pathfinds north (Z≥+5) then east.

---

## Design Decisions

1. **Deferred-init pattern** (not a mutable inner field or adapter class): The cleanest approach given the constraint that `StrideKinematicsModule` and `NavigationIntentBridgeSystem` take their crowd provider at construction time (before baking is possible). The provider acts as a transparent no-op until initialized — no observable difference from `FakeDtCrowdProvider`.

2. **Infantry `maxAgentRadius = 0.4 m`** (not 0.3 m): `DtCrowdConfig.maxAgentRadius` controls the proximity grid cell size and must be ≥ any individual agent's radius. 0.4 m gives a small margin above the 0.3 m infantry agent radius.

3. **F5 case registers agent directly** (not via `LocomotionChannel`): The harness case directly calls `RegisterAgent` + `SetAgentTarget` on the crowd provider, bypassing the `NavigationIntentBridgeSystem`'s `LocomotionChannel` path. This is the same pattern as F1 (directly sets `CrowdMotorIntent.Velocity`). The `NavigationIntentBridgeSystem` crowd-registration path requires a `LocomotionChannel` with `ActionIdMoveTo` — wiring that would add unnecessary complexity to a GPU-only demo.

4. **No change to `NavigationExecutionSystem`**: It doesn't need the crowd provider. It monitors `NavigationIntent + NavigationStatus + FrustrationTicks + SimTransform + SimVelocity`. For the F5 case those components are present but `NavigationIntent` is not set (Mode=None), so the system skips this entity — no interference.

---

## Deviations

None from the spec. BATCH-18 already bakes both Infantry and Vehicle layers; no additional bake step was needed. The `NavigationIntentBridgeSystem` already had the 3-arg constructor with `IDtCrowdProvider` — just needed to be wired with the real provider.

---

## Test Results

```
Stride Core Tests:     Passed 300/300 (+5 new BATCH-19 tests)  [was 295]
Stride Animation:      Passed 48/48
HrotStrideApp.Game:    Passed 136/136
Build:                 0 errors, 4 pre-existing NU1608 NuGet version warnings (not new)
```

New BATCH-19 tests (5/5 green):
- B19-SC1 `InfantryLayer_BakesSuccessfully_AndIsRetrivedViaProvider` — PASS
- B19-SC2 `DeferredCrowd_BeforeInit_IsNoOp_AfterInit_IsFunctional` — PASS
- B19-SC3 `DeferredCrowd_TryInitializeNavMesh_ReturnsFalseOnSecondCall` — PASS
- B19-SC4 `DotRecastCrowd_AgentWithGoalAcrossWall_ProducesDetourVelocity` — PASS (L-corridor, north detour FDP Y > 0.2)
- B19-SC5 `InfantryNavmeshChain_CrowdAgentUpdateSystem_WritesCrowdMotorIntentVelocity` — PASS (chain test)

---

## Developer Insights

1. **`NavigationIntentBridgeSystem` already had crowd support** — the 3-arg constructor `(TrajectoryPoolManager?, IDtCrowdProvider?)` was already implemented (BATCH-14 or earlier); BATCH-19 just needed to wire it with the real provider instead of passing null.

2. **Infantry layer was already baked by BATCH-18** — `BakeNavmesh` called `baker.Bake(verts, indices, NavLayerMask.Vehicle | NavLayerMask.Infantry)`. The only gap was that the `DtNavMesh` wasn't extracted and given to the crowd provider.

3. **SC4 geometry subtlety** — A simple "wall as raised platform" doesn't reliably block DotRecast agents because floor geometry still exists under the raised region. L-corridor (absent geometry) is the reliable approach for headless testing.

4. **DtCrowd position sync** — `DotRecastDtCrowdProvider.Update` teleports each registered agent's `npos` to the entity's `SimTransform.Position` before stepping the crowd. This keeps the crowd in sync with Bullet physics authority (not fighting it).

5. **CrowdAgent must be registered + target set BEFORE CrowdAgentUpdateSystem runs** — the crowd only outputs velocity once registered. The F5 harness case handles this by registering after entity resolution (first frame after spawn).

---

## Known Issues

1. **GPU verification pending** — the F5 live walk (mannequin walks around obstacles, animated) requires a GPU run by the user (same as F4 which was GPU-verified after BATCH-18). The chain is headless-proven (SC5).

2. **LocomotionChannel path not exercised in F5** — the `NavigationIntentBridgeSystem` crowd-registration via `ActionIdMoveTo` is wired but not exercised by the F5 demo. The direct-registration path is used instead. A future BATCH should add a test that exercises the full `NavigationIntent → LocomotionChannel → bridge → crowd` path.

3. **Arrival radius is geometric only** — the F5 case uses Cartesian distance-to-goal (≤ 1.5 m). The `NavigationStatus.Result` is set to `InProgress` but not updated by `NavigationExecutionSystem` in the F5 path (no `NavState` is added). This is intentional for the demo — the full `NavigationIntent` pipeline including `NavigationStatus.Arrived` would require also adding `NavState + FrustrationTicks` and having `NavigationExecutionSystem` run with a proper `NavMode`. The crowd/motor chain is still fully proven.

---

## Suggested Commit Message

`feat(stride): wire real Infantry DotRecastDtCrowdProvider + F5 Navmesh Walk demo (BATCH-19, STR-D19 discharge)`

---

## Fix: register CrowdAgent + nav component set; on-navmesh snap; F5 diagnostics

**Date:** 2026-06-04 | **Triggered by:** GPU log `[Navmesh Walk] WARNING: CrowdAgent component type not registered — cannot proceed.`

### Root cause

`EditorStrideSubsystem.Initialize` registered `CrowdMotorIntent` but NOT `CrowdAgent`.
`CrowdAgentUpdateSystem.Execute` guards on `repo.IsComponentTypeRegistered<CrowdAgent>()` and
returns early when absent — so no velocity was ever written and the F5 demo bailed immediately.

**Registered components audit (post-fix):**

| Component | Registered by | Status before fix |
|-----------|--------------|-------------------|
| `NavigationIntent` | `MuscleRoleComponentRegistry.RegisterAll` (via `SimHostComponentRegistry.RegisterAll`) | ✓ already present |
| `NavigationStatus` | `KinematicComponentRegistry.RegisterAll` (via chain) | ✓ already present |
| `NavState` | `KinematicComponentRegistry.RegisterAll` (via chain) | ✓ already present |
| `FrustrationTicks` | `KinematicComponentRegistry.RegisterAll` (via chain) | ✓ already present |
| `CrowdMotorIntent` | `EditorStrideSubsystem.Initialize` (explicit, BATCH-17) | ✓ already present |
| `CrowdAgent` | `EditorStrideSubsystem.Initialize` (explicit, **this fix**) | ✗ **MISSING — root cause** |

Only `CrowdAgent` was missing. One line added in `EditorStrideSubsystem.Initialize`:
```csharp
World.RegisterComponent<Fdp.Toolkit.Navigation.CrowdAgent>();
```

### On-navmesh snap

Added `DotRecastDtCrowdProvider.TrySnapToNavmesh(fdpPos, out snappedFdp)` (uses `FindNearestPoly`
with ±2/4/2 m half-extents). The F5 demo now snaps **both start and goal** before registration,
logs the input position, snapped position, on-mesh flag, and snap distance for each. The agent is
placed at the snapped start position on creation via the new `RegisterAgent(entity, params, Vector3 startFdp)` overload.

**Start FDP (-4, 2, 0) and goal FDP (4, 13, 0):** both should snap to the baked arena floor with
near-zero snap distance (both points are inside the arena interior). Snap logging will confirm this
in the next GPU run.

### F5 diagnostics added

Per-frame log (~0.5 s, throttled) now includes:
- `agentReg=` (bool — did `RegisterAgent` return true?)
- `crowdPos=` + `dvel=` + `reachedTarget=` + `nearbyAgents=` (from `TryGetAgentSnapshot`)
- `CrowdMotorIntent.vel=` magnitude
- `SimVelocity=` (post-physics BulletReverseSyncSystem output)
- `body=` (Bullet body present in lifecycle)

If velocity is zero, a `ZERO VELOCITY DIAGNOSIS` line explains why (off-navmesh snap fail,
DtCrowd desired-velocity also zero, or `CrowdAgentUpdateSystem` not finding entity in its query).

### New headless test

`B19Fix_CrowdAgent_RegisteredInWorld_FullChainProducesNonzeroMotorIntent` (in `NavmeshWalkIntegrationTests`):
- Asserts `CrowdAgent` is registered (the fix precondition).
- Snaps start FDP(-8,0,0) and goal FDP(+8,+10,0) on L-corridor navmesh — both assert on-mesh.
- Registers agent at snapped start; sets snapped goal.
- Steps `CrowdAgentUpdateSystem` × 15 ticks.
- Asserts `CrowdMotorIntent.Velocity` magnitude > 0.05 m/s AND north component (FDP Y) > 0.1 m/s
  (the only route is via the north connector — proves real pathfinding, not just non-zero output).

### Test results (post-fix)

```
Stride Core Tests:     Passed 301/301 (+1 new B19-FIX test)  [was 300]
Stride Animation:      Passed 48/48
HrotStrideApp.Game:    Passed 136/136
Build:                 0 errors, pre-existing NU1608 + CS0108 warnings (not new)
```
