# BATCH-08: DtCrowd provider + CrowdAgentUpdateSystem refactor + road-graph/Auto (Phase 2 complete)
**Tasks:** STR-P2-T3, STR-P2-T4, STR-P2-T5   **Phase:** P2 (Navigation)   **Est:** ~10–12h
**Dependencies:** BATCH-07 (`DotRecastNavmeshProvider`, navmesh-query convention), BATCH-05 (`CrowdMotorIntent`, `BulletCharacterMotor`).

Goal — finish navigation: (T3) `DotRecastDtCrowdProvider : IDtCrowdProvider` real local-avoidance/steering (pure DotRecast — headlessly validatable, drop-in for `FakeDtCrowdProvider`); (T4) refactor `CrowdAgentUpdateSystem` to write **only** `CrowdMotorIntent` and stop mutating `SimTransform` (closes the crowd→`BulletCharacterMotor` loop and **resolves STR-D12**); (T5) materialize `ZoneEnvironmentData` from the scenario and verify `PathfindingSolverSystem`'s `Auto` selection (RoadGraph / Navmesh / Hybrid).

No Corrective Task 0 (BATCH-07 approved). **This batch resolves STR-D12.**

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract.
2. `.dev/stride-1/Stride-Integration_v0_3.md` §10.1 (dtCrowd), §5.3 (`CrowdAgentUpdateSystem` refactor — spec for T4), §10.2 (road-graph), §10.3 (Auto selection — spec for T5).
3. `.dev/stride-1/TASK-DETAIL.md` — STR-P2-T3, STR-P2-T4, STR-P2-T5.
4. `reviews/BATCH-07-REVIEW.md` + `DEBT-TRACKER.md` (STR-D12).

Use the **codebase-memory MCP first** (project `D-Work-IOS-IG-SimHost-FDP`).

### Verified facts & exact references
- **Contract to implement (T3)** = `IDtCrowdProvider` ([IDtCrowdProvider.cs](../../../FDP/Toolkits/Fdp.Toolkits/Navigation/IDtCrowdProvider.cs)): `RegisterAgent(entity, CrowdAgentParams)`, `UnregisterAgent`, `SetAgentTarget`, `Update(dt, view)` (reads `SimTransform` per agent, writes per-agent velocity), `GetAgentVelocity`, `TryGetAgentSnapshot`. **Drop-in target** = `FakeDtCrowdProvider` ([FakeDtCrowdProvider.cs](../../../FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeDtCrowdProvider.cs)) — match its contract semantics.
- **DotRecast DtCrowd API [VERIFY]** (2026.1.3): `DtCrowd`, `DtCrowdConfig`, `DtCrowdAgentParams`, `AddAgent`/`RemoveAgent`/`RequestMoveTarget`, `Update(dt, ...)`, agent `npos`/`nvel`/`vel`. Confirm against the installed `DotRecast.Detour.Crowd` package; DtCrowd needs the baked `DtNavMesh` from BATCH-07 (`DotRecastNavmeshProvider`/baker) to steer over. **Coordinate convention: same as the navmesh — (X=East, Y=Up, Z=North)** (BATCH-07 headline); convert `SimTransform` ↔ crowd space accordingly (callers/`Update` read FDP `SimTransform` — apply the swizzle).
- **T4 target** = `CrowdAgentUpdateSystem` ([CrowdAgentUpdateSystem.cs](../../../FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/CrowdAgentUpdateSystem.cs)) — currently integrates `tf.Position += velocity*dt` (STR-D12). Refactor: poll `_dtCrowd.GetAgentVelocity(entity)`, write **only** `CrowdMotorIntent.Velocity` (FDP space), **stop** writing `SimTransform`/`SimVelocity`. `BulletCharacterMotor` (BATCH-05) then consumes the intent.
- **T5 symbols** ([VERIFY] all): `PathfindingSolverSystem` ([PathfindingSolverSystem.cs](../../../FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/PathfindingSolverSystem.cs)) — **already has multi-modal `Auto` backend selection** from prior nav work; T5 mainly *verifies* it. `ZoneEnvironmentData` ECS singleton ([ZoneEnvironmentData.cs](../../../FDP/Toolkits/Fdp.Toolkits/CarKinem/ZoneEnvironmentData.cs)); `ZoneManagerService` ([ZoneManagerService.cs](../../../Hrot/Engine/Hrot.Core/Services/ZoneManagerService.cs)) + `RoadNetworkLoader.LoadFromJson` ([RoadNetworkLoader.cs](../../../FDP/Toolkits/Fdp.Toolkits/CarKinem/Road/RoadNetworkLoader.cs)); `ZoneDefinitionDto.RoadNetworkPath` ([ZoneDefinitionDto.cs](../../../Hrot/Engine/Hrot.Core/Scenario/Map/ZoneDefinitionDto.cs)); `RoadRadiusThresholdSq`. Reference `ZoneAuthoring_RoadNetworkUpdate_InjectsZoneEnvironmentDataSingleton` for the existing wiring.

**Complete tasks in sequence (T3 → T4 → T5); do NOT start the next until the current is implemented, tested, and ALL tests (incl. prior batches') pass.** Work autonomously. Only stop on a genuine breaking design flaw or unrecoverable blocker.

---

## Task 1: `DotRecastDtCrowdProvider` (STR-P2-T3)
**File:** `Stride/Hrot.Stride.Core/DotRecastDtCrowdProvider.cs` (NEW). Spec: design §10.1.
Wrap DotRecast `DtCrowd` over the baked `DtNavMesh` to implement `IDtCrowdProvider`. Agent add/remove maps to `DtCrowd.AddAgent`/`RemoveAgent`; `SetAgentTarget` → `RequestMoveTarget` (project the target onto the navmesh); `Update(dt, view)` reads each agent's `SimTransform` (swizzled), steps `DtCrowd`, and stores per-agent velocity (converted back to FDP) for `GetAgentVelocity`.

**Tests required** (headless, real DotRecast DtCrowd over a baked synthetic navmesh):
- Add/remove agents (`RegisterAgent` returns false on duplicate; `UnregisterAgent` is safe when absent).
- `GetAgentVelocity(entity)` returns a steering velocity **pointing toward the agent's target** (set a target across the navmesh, `Update` a few steps, assert the velocity's direction is toward the target and magnitude ≤ MaxSpeed).
- `TryGetAgentSnapshot` reflects position/target/desired velocity; returns false for an unregistered entity.
- Contract parity with the fake on the key behaviors.

## Task 2: `CrowdAgentUpdateSystem` velocity-only refactor (STR-P2-T4)
**File:** edit `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/CrowdAgentUpdateSystem.cs`. Spec: design §5.3. **Resolves STR-D12.**
Refactor so it polls `_dtCrowd.GetAgentVelocity(entity)` and writes **only** `CrowdMotorIntent.Velocity`; it must **no longer** write `SimTransform` or `SimVelocity`. Preserve any existing non-position responsibilities. Mind that this system is in `Fdp.Toolkits` and `CrowdMotorIntent` is in `Fdp.Toolkit.Navigation` (same assembly, BATCH-05) — no new cross-assembly dependency.

**Tests required:**
- Writes `CrowdMotorIntent` from `_dtCrowd.GetAgentVelocity` (assert the intent velocity equals the crowd velocity for a known agent).
- Does **not** write `SimTransform` or `SimVelocity` (set a known `SimTransform`, run the system, assert position unchanged — this is the STR-D12 fix; update/replace the old test that asserted position integration).
- Integration with `BulletCharacterMotor` (BATCH-05): the intent written here is consumed by the motor (a small integration test: crowd velocity → `CrowdMotorIntent` → motor sets character velocity via the fake service).

## Task 3: Road-graph mode + `Auto` selection (STR-P2-T5)
**Files:** wire `ZoneEnvironmentData` materialization where `editor_stride` loads a scenario (or a focused integration harness) + verification tests. Spec: design §10.2, §10.3.
Materialize `ZoneEnvironmentData` from the scenario `Zones`/`RoadNetworkPath` via the standard path (`ZoneManagerService` → `RoadNetworkLoader.LoadFromJson` → `ZoneEnvironmentData` singleton), and verify `PathfindingSolverSystem`'s `Auto` selection with both singletons present. (If `PathfindingSolverSystem` already implements `Auto`, this task is mostly wiring + verification — do not duplicate the selection logic; verify it.)

**Tests required** (integration):
- With both `INavmeshProvider` (DotRecast) and `ZoneEnvironmentData` (road graph) singletons present: endpoints near road nodes (within `RoadRadiusThresholdSq`) select `RoadGraph`; off-road endpoints select `Navmesh`; mixed selects `Hybrid` (assert the chosen backend per the thresholds).

---

## Success Criteria
- [ ] STR-P2-T3: `DotRecastDtCrowdProvider` implements `IDtCrowdProvider` over real DtCrowd (drop-in for the fake); steering velocity points toward target; tests pass.
- [ ] STR-P2-T4: `CrowdAgentUpdateSystem` writes only `CrowdMotorIntent`, no `SimTransform`/`SimVelocity`; consumed by `BulletCharacterMotor`. STR-D12 resolved.
- [ ] STR-P2-T5: `ZoneEnvironmentData` materialized from the scenario; `Auto` selection verified (RoadGraph / Navmesh / Hybrid by threshold).
- [ ] Full test suite green (all prior batches + this); Stride solution builds clean; report submitted.

## Report Requirements (`reports/BATCH-08-REPORT.md`)
Answer: the DotRecast DtCrowd 2026.1.3 API used (real types) and how `SimTransform`↔crowd-space conversion is handled; how the steering-toward-target test asserts direction; exactly what changed in `CrowdAgentUpdateSystem` (and which old position-integration test you replaced) — STR-D12 closure; whether `PathfindingSolverSystem` already had `Auto` (and so T5 was verification) or needed changes; how the RoadGraph/Navmesh/Hybrid selection is tested; any coordinate-convention pitfalls between crowd and navmesh; weak points; suggested one-line commit message. Report actual test counts/output. Do NOT ask comprehension questions.
