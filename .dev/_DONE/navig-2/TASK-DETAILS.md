# Navigation Subsystem v2 — Task Details

**Reference design documents (do not duplicate — read alongside):**
- [Navigation_Design_v2_0.md](./Navigation_Design_v2_0.md) — architecture, contracts, Phase-A roadmap (§20).
- [DD-Fake-Nav.md](./DD-Fake-Nav.md) — fake providers, `NavTestMap`, ImGui window, ComponentIds.
- [DD-EngineBacked-Nav.md](./DD-EngineBacked-Nav.md) — engine-backed providers, module wiring, kinematics routing.
- [DD-Tests-Nav.md](./DD-Tests-Nav.md) — three test layers, twelve integration scenarios.

This document gives the implementation detail and success conditions for every task.
It references the design chapters above rather than repeating them. Together a task entry
+ its referenced chapter is sufficient for a developer to implement and prove the task.

---

## ⚠️ Verified codebase facts & design discrepancies (read first)

These were verified against the live codebase via the code graph. The design docs were
written against an idealized contract; several statements do **not** match the current code.
Tasks below are written against the **true codebase** and carry corrective work. Each
discrepancy is also reflected as a hazard in the relevant task. (Per maintainer decision:
*bake corrective tasks + flag*.)

| # | Design claim | Reality in codebase | Affected task |
|---|---|---|---|
| **DSC-1** | §8.1 `INavmeshProvider` "amended in place" — add `layerMask` to `PathExists`/`PathCost`/`IsWalkable`/`SampleNavmeshPoints`/`QueryVersion`/`ProjectToNavmesh`. | Actual interface ([INavmeshProvider.cs](../../FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/INavmeshProvider.cs)) is `IsReachable(from,to)`, `TryGetPathDistance(from,to,out)`, `GetRandomPointsInRadius(center,radius,Span)`. **Different method names & signatures** — a full redefinition, not an amendment. Sole impl is `StubNavmeshProvider` (Euclidean). | [NAV-P0-T3](#nav-p0-t3) |
| **DSC-2** | §7.1 existing `KinematicsMode` = `{None,DirectPoint,RoadGraph,CustomTrajectory}`; add `Crowd=4,Naval=5,Flying=6`. | Actual enum ([NavigationEnums.cs](../../FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/NavigationEnums.cs)) = `{None=0,RoadGraph=1,CustomTrajectory=2,Formation=3,Direct=4}`. Proposed `Crowd=4` **collides with `Direct=4`**; `DirectPoint` does not exist (it is `Direct`). | [NAV-P0-T2](#nav-p0-t2) |
| **DSC-3** | §17 `NavigationIntent`/`NavigationStatus` "keep, extend". | Actual ([NavigationComponents.cs](../../FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs)): `NavigationIntent{Mode,ReverseAllowed,FinalDestination,TargetSpeed,ArrivalRadius,IntentId,TargetNodeId,TrajectoryId}` and `NavigationStatus{IntentId,Result,ProgressS}`. The design's action-id / params-blob / `RouteHandle` layout is a **rewrite**, not an extension. Channel header fields (`ActionInstanceId`, params blob, `Status`) actually live on `LocomotionChannel`, not on `NavigationIntent`. | [NAV-P0-T4](#nav-p0-t4) |
| **DSC-4** | §5.2 path-response event is `PathResponseEvent`. | Actual event is `PathfindingResultEvent` ([PathfindingEvents.cs](../../FDP/Toolkits/Fdp.Toolkits/Navigation/PathfindingEvents.cs)); materialized by `PathfindingResultMaterializationSystem`. `PathfindingRequestEvent` already carries `MobilityProfile` (0=Wheeled,1=Tracked,2=Infantry) but **no** `RouteHandle`/`NavLayerMask`/`BackendForce`/`MaxCost`. | [NAV-P1-T1](#nav-p1-t1) |
| **DSC-5** | DD-Tests-Nav §2.1 assemblies `Hrot.Navigation.*`. | No such assembly exists. Current nav code is in `Fdp.Toolkits` (`Fdp.Toolkit.Navigation`, `CarKinem`) which has no circular-dependency issue. (Per maintainer decision: **avoid assembly bloat — place new nav code in the existing `Fdp.Toolkits` assembly**, not new `Hrot.Navigation*` assemblies.) | [NAV-P0-T1](#nav-p0-t1) |
| **DSC-6** | §5/§2 solver "multi-modal backend selection". | Actual `PathfindingSolverSystem` ([PathfindingSolverSystem.cs](../../FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/PathfindingSolverSystem.cs)) does **road-graph Dijkstra only** (O(N²)); no navmesh, no backend selection, no `MobilityProfile`/`BackendForce` branching. `NavigationSolverModule` already exists at `SlowBackground(10Hz)`. | [NAV-P1-T3](#nav-p1-t3) |

**Editor wiring fact (maintainer requirement).** The editor / headless host registers the
Muscle tier through [SimHostNodeBootstrapper.cs](../../Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs)
→ `SimHostCoreLogicPack` (which builds `NavigationIntentBridgeSystem` + `GroundKinematicsModule`)
plus the 10 Hz `NavigationSolverModule`. Editor `MoveTo` today flows: `MoveToLocation` mission →
BTree → `MoveToExecutor` (writes `NavigationIntent.Mode=DirectPoint`) → `NavigationIntentBridgeSystem`
→ `NavState.Mode=Direct` → `CarKinematicsSystem`/`LinearKinematicsSystem` drive to `FinalDestination`.
The engine-backed module ([NAV-P6-T5](#nav-p6-t5)) must slot into this exact host so existing
`MoveTo` missions keep arriving while the new action surface is introduced.

---

## Task ID scheme

`NAV-P<phase>-T<n>`. Phases:

- **P0** Foundations, contracts & corrective migration
- **P1** Muscle ↔ Solver path-query pipeline
- **P2** Fake backends (DD-Fake-Nav)
- **P3** Crowd, off-mesh traversal & animation seam
- **P4** Brain-side execution & action surface
- **P5** Replan, corridor preview & auto-refresh
- **P6** Engine-backed module + editor wiring (DD-EngineBacked-Nav)
- **P7** Diagnostics, snapshot & gizmos
- **P8** Layer-1 unit tests (DD-Tests-Nav §3)
- **P9** Layer-2 system tests (DD-Tests-Nav §4)
- **P10** Layer-3 integration scenarios (DD-Tests-Nav §6)

---

# Phase 0 — Foundations, contracts & corrective migration

### NAV-P0-T1
**Establish navigation code placement in `Fdp.Toolkits` (no new production assemblies)**
*Design refs:* Navigation §2/§18; DD-Tests-Nav §2.1; DSC-5.

Per maintainer decision (avoid assembly bloat): place **all** new navigation production code in the
existing `Fdp.Toolkits` assembly, which already hosts `NavigationIntent`/`NavigationStatus`,
`INavmeshProvider`, `PathfindingSolverSystem`, `TrajectoryPoolManager`, `NavigationIntentBridgeSystem`,
`NavigationExecutionSystem` (`CarKinem`) and has no circular-dependency problem. Do **not** create the
`Hrot.Navigation*` assemblies named in DD-Tests-Nav §2.1, and do **not** add a thin contracts assembly.
Use namespaces to organize:
- `Fdp.Toolkit.Navigation` — provider interfaces (`IDtCrowdProvider`, `IVolumetricPathProvider`,
  `IPathRegistry`), `NavWaypoint`, `TraversalKind`/`SurfaceType`, `NavAgentProfile`, corridor components,
  `NavigationHandleAllocator`, new Muscle/Brain systems.
- `Fdp.Toolkit.Navigation.Fake` — the four fakes + `NavTestMap` (DD-Fake-Nav).
- `Fdp.Toolkit.Navigation.EngineBacked` — engine-backed providers + module (DD-EngineBacked-Nav); these
  naturally wrap `TrajectoryPoolManager`/`RoadNetworkBlob` already in `Fdp.Toolkits/CarKinem`.

UI (the ImGui inspector window [NAV-P7-T1](#nav-p7-t1) and the path gizmo [NAV-P7-T3](#nav-p7-t3)) live in
the existing editor assemblies (`Hrot.Editor.AiShared` / `Hrot.Editor`), since window/gizmo infra is there
and `Fdp.Toolkits` must stay UI-free. Tests: reuse the existing `Fdp.Toolkits.Tests` (already has
`Navigation/` + `CarKinem/` folders) for Layer-1/Layer-2; add a single integration-test project only if no
existing all-in-one integration assembly is suitable.

**Success conditions:**
- New nav types compile inside `Fdp.Toolkits`; no new production `.csproj` created.
- `dotnet build` succeeds; `Fdp.Toolkits` acquires **no** new outward project reference that creates a cycle
  (in particular it does not reference any editor/UI assembly).
- Layer-1/Layer-2 nav tests are discoverable under `Fdp.Toolkits.Tests`.

### NAV-P0-T2
**Resolve `KinematicsMode` extension without enum collision**
*Design refs:* §7.1; DSC-2.

Extend `KinematicsMode` ([NavigationEnums.cs](../../FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/NavigationEnums.cs))
to add `Crowd`, `Naval`, `Flying` using **next free values (5,6,7)** — NOT 4 (taken by `Direct`).
Update every `switch`/comparison on `KinematicsMode` (find via graph: `NavigationIntentBridgeSystem`,
`CarKinematicsSystem`, `LinearKinematicsSystem`, `NavigationExecutionSystem`) to handle the new members
or default-safely. Document the corrected mapping (the design's `DirectPoint` == existing `Direct`).

**Success conditions:**
- `Crowd`/`Naval`/`Flying` added with non-colliding values; existing members unchanged in value.
- Unit test asserting all enum values distinct and existing values preserved
  (`None=0,RoadGraph=1,CustomTrajectory=2,Formation=3,Direct=4`).
- Solution compiles; existing `CarKinem` tests stay green.

### NAV-P0-T3
**Redefine `INavmeshProvider` + migrate EQS callers**
*Design refs:* §8.1, §8.4; DSC-1.

Replace the current 3-method `INavmeshProvider` with the design's layer-aware surface
(`IsWalkable`, `ProjectToNavmesh`, `SampleNavmeshPoints`, `PathExists`, `PathCost`, `QueryVersion`
+ solver convenience `PlanPath`). This **breaks** existing EQS callers — migrate them:
- [StubNavmeshProvider.cs](../../FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/StubNavmeshProvider.cs) (reimplement against new surface, default layer).
- `NavmeshReachableTest`, `PathCostScoreTest`, `NavmeshSamplesGenerator` (`Spatial/Eqs/`) — map old
  `IsReachable`→`PathExists`, `TryGetPathDistance`→`PathCost`, `GetRandomPointsInRadius`→`SampleNavmeshPoints`,
  supplying `layerMask` default from `ctx.Self`'s `NavAgentProfile.PreferredLayerMask` (§8.3).
- EQS round-trip tests under `Hrot.ClusterRunner.Integration.Tests/Eqs/`.

The EQS template-level `NavLayerMask` parameter add is the separate mandatory-but-mechanical
EQS follow-up (§8.4) — **out of scope here**; only the interface + call-site migration is in scope.

**Success conditions:**
- New `INavmeshProvider` compiles; all current EQS callers migrated and building.
- Existing EQS tests (`NavmeshProviderTests`, `NavmeshTests`, `EqsRoundTripTests`, `PathCostInversionTests`,
  `AccurateLosPhaseTests`) pass with the migrated stub.
- Migration is documented inline; no caller still references the removed method names.

### NAV-P0-T4
**Introduce action-based navigation command layer (`ActiveAction` + params + `RouteHandle`)**
*Design refs:* §4.1, §4.2, §13.1, §13.6; DSC-3.

The current `NavigationIntent` is `Mode`-based and `LocomotionChannel` already carries the channel
header (`ActionInstanceId`, params blob, `Status`) consumed by `IActionExecutor<LocomotionChannel>`.
Add the **new action surface** on top of the existing channel model rather than discarding it:
- Add new `ActionId`s to the locomotion action set: `PlanRoute`, `FollowPath`, `FetchPathDetails`,
  `ReleasePath` (§13.6); keep `MoveTo`, `FollowRoute`, `Flee`, `JoinFormation`; remove `FollowRoadGraph`
  (subsumed by `MoveTo` + `BackendForce=RoadGraph`, §17 — see [NAV-P4-T2](#nav-p4-t2)).
- Define the 32 B param structs `MoveToParams` (extend existing), `PlanRouteParams`, `FollowPathParams`,
  `FetchPathDetailsParams`, `ReleasePathParams` (§13.1) — keep ≤ the `LocomotionChannel` 96 B budget
  (verified by `LocomotionChannel_SizeIsAtMost96Bytes`).
- Extend `NavigationIntent` (DSC-3): add `RouteHandle` (int, 0=none), and the per-action discriminant
  the Muscle needs. Extend `NavigationStatus` with `Phase`, `LastFailureReason`, `ReplanCount`, `RouteHandle`,
  `EstimatedTimeRemaining`, `NavmeshVersionObserved` (§14 forward-compat hook — carried, never differs
  initially), plus the new `Result` members (`FailedNoLayer`, `FailedInvalidHandle`,
  `PathFound`, `NoPath`). Update the DDS descriptors + translators
  (`NavigationIntent{Egress,Ingress}Translator`, `NavigationStatus*Translator` in `Hrot.Network.NED`).

**Success conditions:**
- New param structs are exactly 32 B (`[StructLayout(Sequential)]`, asserted by a layout test).
- `NavigationStatus`/`NavigationIntent` round-trip through their DDS translators without loss
  (extend `NavigationIntentEgressTranslatorTests`).
- `NavigationIntent` exempt from the 96 B channel budget but params blobs are not (§4 note).
- Existing `MoveTo` flow still compiles and passes `MoveToExecutorTests`.

### NAV-P0-T5
**`NavWaypoint`, `TraversalKind`, `SurfaceType`, `NavAgentProfile`, corridor components + ComponentId block**
*Design refs:* §4.3, §4.5, §4.6, §8.3; DD-Fake-Nav §12.

Add to `Hrot.Navigation`:
- `NavWaypoint` (24 B, §4.5), `TraversalKind`/`SurfaceType` enums (§4.6).
- `NavAgentProfile` component (§8.3) + its TKB DTO (`NavAgentProfileDto`, used by `NavTestTemplates`);
  new dtCrowd-relevant fields (radius / agent-height) get standard `ANIM00x`-style TKB hot-reload
  validators per §16 (DD-4 pattern).
- `NavigationCorridorMuscle` (Muscle-internal, no replication, §4.3).
- `NavigationCorridorPreview` + `PreviewWaypoint` (opt-in, replicated, §4.2) — component absent = zero traffic.
- `NavigationPathDetailsBuffer` (Brain-side cache backing, §4.1).
- `CrowdAgent` tag component.
- Allocate `[ComponentId]`s: production nav components in a new reserved block (coordinate with
  [GlobalComponentIds.cs](../../FDP/Engine/Fdp.Core/GlobalComponentIds.cs) / `NavigationContractsComponentIds`),
  and fake-only components in **block 250–279** per DD-Fake-Nav §12.

  **ComponentId allocation (architect-confirmed):** production nav components
  (`NavigationCorridorMuscle`, `NavigationCorridorPreview`, `NavigationPathDetailsBuffer`, `CrowdAgent`,
  `NavAgentProfile`) use the free slots **69–79** in the existing `50–79` navigation-contracts block
  (`NavigationIntent`=67, `NavigationStatus`=68 already occupy it — this block was reserved precisely to
  avoid circular assembly deps). If the 79 ceiling is exceeded, spill into the globally-reserved toolkit
  expansion block **215–249**. Fake-only components stay sandboxed in **250–279**.

**Success conditions:**
- `NavWaypoint` is 24 B; `NavigationCorridorPreview` is 144 B with 8 inline `PreviewWaypoint`s (§4.2).
- Production components occupy 69–79 (or 215–249 on spill); no value collides with 67/68 or any toolkit block.
- ComponentId uniqueness test passes (no duplicate IDs across the toolkit + fake blocks),
  mirroring `GlobalComponentIds_NoToolkitBlockDuplicates`.
- Components register through the component registry (extend `ComponentRegistryTests`).

---

# Phase 1 — Muscle ↔ Solver path-query pipeline

### NAV-P1-T1
**Extend `PathfindingRequestEvent`/result with `RouteHandle`, `NavLayerMask`, `BackendForce`, `MaxCost`**
*Design refs:* §4.4, §5.1; DSC-4.

Extend [PathfindingEvents.cs](../../FDP/Toolkits/Fdp.Toolkits/Navigation/PathfindingEvents.cs):
`PathfindingRequestEvent` gains `RouteHandle`, `NavLayerMask`, `BackendForce`, `MaxCost`,
`NavmeshVersionAtRequest` (§4.4); extend `MobilityProfile` doc to include `Naval=3`, `Flying=4`.
`PathfindingResultEvent` gains `NavmeshVersionAtPlan`, `FailureReason`, `PrimaryBackend`, and echoes
`RouteHandle`. Keep the existing `RequestId`/`SourceNodeId` mechanism. Update the scale-out DDS
batch shapes (`PathResponseBatch` etc. in `Hrot.Network.NED`) and translators for forward compat
(`PathRequestEgressTranslator`/`PathResponseIngressTranslator`, §5.2) — registered only in scale-out.

**Success conditions:**
- New fields present; struct stays blittable (`[StructLayout(Sequential)]`, `[EventId]` retained).
- `PathfindingSolverSystemTests.RunSolverPipeline` updated to assert handle echo round-trip.
- Default-mode flow uses the local `FdpEventBus` only (no DDS), per §2 summary table.

### NAV-P1-T2
**`NavigationIntentBridgeSystem` — publish path requests & route by action**
*Design refs:* §5.1, §7.1.

Extend [NavigationIntentBridgeSystem.cs](../../FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/NavigationIntentBridgeSystem.cs):
on a new intent (`ActionInstanceId`/`IntentId` change), construct a `PathfindingRequestEvent` from intent
+ entity state and publish it on the **local Muscle bus** for `MoveTo`/`PlanRoute`; for
`FollowPath`/`FetchPathDetails`/`ReleasePath` handle directly without the solver (§7). Set `NavState.Mode`
per `MobilityProfile` routing (§7.1) using corrected `KinematicsMode` values from [NAV-P0-T2](#nav-p0-t2).
The crowd-tag branch is added in [NAV-P3-T1](#nav-p3-t1); here just establish the request/route plumbing.

**Success conditions:** (see DD-Tests-Nav §4.3 — implemented in [NAV-P9-T3](#nav-p9-t3))
- New `MoveTo` intent publishes exactly one `PathfindingRequestEvent` on the local bus with correct fields.
- `PlanRoute` intent publishes a request carrying the Brain-allocated `RouteHandle`.
- `FollowPath` with a known handle starts following without a new path request; unknown handle →
  `NavigationStatus.Result=FailedInvalidHandle`.
- Idempotent on unchanged `ActionInstanceId`.

### NAV-P1-T3
**Multi-modal backend selection in `PathfindingSolverSystem`**
*Design refs:* §5.2, §9, §10; DSC-6.

Extend [PathfindingSolverSystem.cs](../../FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/PathfindingSolverSystem.cs)
(runs in the existing `NavigationSolverModule` `SlowBackground(10Hz)`): keep the road-graph Dijkstra as
the `RoadGraph` backend; add backend selection by `MobilityProfile` + `BackendForce` (§5.2 pseudocode):
`Auto` heuristic (road radius test → RoadGraph / splice "Hybrid" / Navmesh), `Flying`→`IVolumetricPathProvider`,
else `INavmeshProvider.PlanPath`. Register the resulting waypoint list in `TrajectoryPoolManager` keyed by
`RouteHandle` (Muscle-private handle ≥ `0x40000000` when handle is 0). Emit `PrimaryBackend` in the result.
Apply the §15 budget bands `[Critical 50% / Normal 35% / Low 15%]` (mirroring EQS §6) with
snapshot-on-demand + `EventAccumulator` for missed-frame events.

**Success conditions:**
- Backend chosen correctly for each `(MobilityProfile, BackendForce)` combination (system test, [NAV-P9](#phase-9--layer-2-system-tests)).
- Road-graph path identical to current behavior when `BackendForce=RoadGraph` (regression: existing
  `PathfindingSolverSystemTests` stay green).
- `Flying` requests invoke `IVolumetricPathProvider`, never the navmesh (asserted via fake instrumentation, S9).

### NAV-P1-T4
**Path-response materialization → `NavigationCorridorMuscle` + status**
*Design refs:* §5.3, §3.1, §3.2.

Extend the response handler (`PathfindingResultMaterializationSystem`, registered by `NavigationSolverModule`)
to: look up the originating entity, write `NavigationCorridorMuscle` (handle, version, segment counts,
distance, backend, flags), and branch on action — `MoveTo` → start following + `NavigationStatus{InProgress,Following}`
+ fire `MoveStartedEvent`; `PlanRoute` → `NavigationStatus{PathFound,RouteHandle,Idle}` (no following), and
fire `NavigationPathDetailsResponseEvent` if `IncludeFullPathDetails`; unreachable → `FailedUnreachable`/`NoPath`.
Resize `PathfindingBatchData` capacity 64→256 (§15) in
[PathfindingBatchData.cs](../../FDP/Toolkits/Fdp.Toolkits/Navigation/PathfindingBatchData.cs).

**Success conditions:**
- After a reachable `MoveTo`, `NavigationCorridorMuscle` is populated and `MoveStartedEvent` fired once.
- `PlanRoute` produces `PathFound` without starting movement (S11 covers end-to-end).
- Unreachable produces `FailedUnreachable` (MoveTo) / `NoPath` (PlanRoute); `MoveStartedEvent` not fired (S7).
- `PathfindingBatchData` capacity is 256 (layout test).

---

# Phase 2 — Fake backends (DD-Fake-Nav)

### NAV-P2-T1
**`FakeNavmeshProvider` + polygon A* + test API**
*Design refs:* DD-Fake-Nav §3; Navigation §8.

Implement in `Fdp.Toolkit.Navigation.Fake` per DD-Fake-Nav §3.1–3.4: `FakeNavLayer`/`NavPolygon`/`OffMeshLink`
state in `FakeNavmeshState` singleton; query algorithms (`IsWalkable`, `ProjectToNavmesh`, `PathExists`,
`PathCost`, `SampleNavmeshPoints`, `QueryVersion`, `PlanPath`) over polygon adjacency A* including off-mesh
links; `IFakeNavmeshProviderTestApi` (`BlockPolygon`/`UnblockPolygon`/`BumpVersion`/`GetLoadedMap`).

**Success conditions:** DD-Tests-Nav §3.1 (`FakeNavmeshProviderTests`) — implemented as [NAV-P8-T1](#nav-p8-t1).

### NAV-P2-T2
**`FakeDtCrowdProvider` + `IDtCrowdProvider` pinning + tick algorithm**
*Design refs:* DD-Fake-Nav §4; Navigation §7.2.

Pin the `IDtCrowdProvider` interface (DD-Fake-Nav §4.1) in `Fdp.Toolkit.Navigation`, plus `CrowdAgentParams`/
`CrowdAgentSnapshot`. Implement `FakeDtCrowdProvider` with per-agent `FakeCrowdAgentState` + singleton
`FakeCrowdGlobalState`; deterministic O(N²) desired-velocity + separation tick (§4.3); `IFakeDtCrowdProviderTestApi`.

**Success conditions:** DD-Tests-Nav §3.2 (`FakeDtCrowdProviderTests`) — [NAV-P8-T2](#nav-p8-t2).

### NAV-P2-T3
**`FakeVolumetricPathProvider` + `IVolumetricPathProvider` pinning**
*Design refs:* DD-Fake-Nav §5; Navigation §9.

Pin `IVolumetricPathProvider` + `FlyProfile` in `Fdp.Toolkit.Navigation`. Implement `FakeVolumetricPathProvider`
(no-fly boxes from `NavTestMap`; straight line, fall back to coarse 3D grid A*); `IFakeVolumetricPathProviderTestApi`.

**Success conditions:** DD-Tests-Nav §3.3 (`FakeVolumetricPathProviderTests`) — [NAV-P8-T3](#nav-p8-t3).

### NAV-P2-T4
**`IPathRegistry` + `MusclePathRegistry` / `BrainPathRegistry` / `SharedPathRegistry`**
*Design refs:* §6.2; DD-Fake-Nav §6.

Define `IPathRegistry` + `PathSummary` in `Fdp.Toolkit.Navigation`. Implement `MusclePathRegistry`
(dictionary-backed authoritative pool, Muscle-private handle allocation ≥ `0x40000000`),
`BrainPathRegistry` (per-entity LRU cache, strict `ReplanCount` cache-miss policy), and the all-in-one
`SharedPathRegistry` (forwarding). Test APIs `IFakeMusclePathRegistryTestApi`/`IFakeBrainPathRegistryTestApi`
+ `FakePathRegistryStats`. Implement `NavigationHandleAllocator` (§6.3) as engine code in `Fdp.Toolkit.Navigation`.

**Success conditions:** DD-Tests-Nav §3.4–3.6 — [NAV-P8-T4](#nav-p8-t4), [NAV-P8-T5](#nav-p8-t5), [NAV-P8-T6](#nav-p8-t6).

### NAV-P2-T5
**`NavTestMap` format (JSON + DSL) + canonical fixtures + `NavigationFakesModule`**
*Design refs:* DD-Fake-Nav §2, §7, §11, §16.

Implement `NavTestMap`, `NavTestMapLoader` (JSON under `tests/data/navmaps/`), `NavTestMapBuilder`
fluent DSL, and the 10 canonical fixtures + `NavTestMaps` static helpers (DD-Fake-Nav §7.3). Implement
`NavigationFakesModule : IEcsModule, IDisposable, IWindowRegistrar` registering the four fakes (shared
registry in all-in-one), headless-guarding the window. Determinism + hard-assert discipline (§11).

**Success conditions:**
- All 10 canonical maps load from JSON and via DSL to equivalent in-memory `NavTestMap`.
- `NavigationFakesModule` registers all four providers as singletons; `Dispose()` clears agent state.
- Determinism unit test: identical map + identical query sequence → identical results.

---

# Phase 3 — Crowd, off-mesh traversal & animation seam

### NAV-P3-T1
**`CrowdAgent` admission + `CrowdAgentUpdateSystem` + kinematics exclusion filters**
*Design refs:* §7.2, §7.2.1, §11 (stance).

Add the crowd-tag branch to `NavigationIntentBridgeSystem` (Infantry `MoveTo` → `NavState.Mode=Crowd`,
add `CrowdAgent` tag via ECB, register with `IDtCrowdProvider`). Implement `CrowdAgentUpdateSystem`
(early `Simulation`): for tagged entities, skip when `Phase==AwaitingTraversal`, else
`SimVelocity = dtCrowd.GetAgentVelocity`. Add `.Without<CrowdAgent>()` to `LinearKinematicsSystem` and
`CarKinematicsSystem`. Apply stance speed multiplier from `StanceStatus` (§11) at registration.
`CrowdAgentUpdateSystem` runs `ExecutionPolicy.Synchronous`/`DataStrategy.Direct` with the dtCrowd slot
pool sized from TKB humanoid count + 2× headroom (§15). Continuous-locomotion seam needs **zero new
contract** (§11): the existing `AnimationRuntimeBridgeSystem` already reads `SimVelocity` — no task.

**Success conditions:** DD-Tests-Nav §4.2 (`CrowdAgentUpdateSystemTests`) — [NAV-P9-T2](#nav-p9-t2);
plus `NavigationIntentBridgeSystemTests` crowd cases ([NAV-P9-T3](#nav-p9-t3)).

### NAV-P3-T2
**`OffMeshLinkDetectionSystem` + zero-frame suppression + montage emit**
*Design refs:* §7.2.2, §11 (discrete traversal).

Implement `OffMeshLinkDetectionSystem` (`SystemPhase.Simulation`, `[UpdateBefore(CrowdAgentUpdateSystem)]`):
look ahead in `NavigationCorridorMuscle` for the next `TraversalKind != Walk`; within look-ahead, write
`Phase=AwaitingTraversal` + `CurrentTraversalKind`, emit `AnimationChannel.PlayMontage` (TraversalKind
discriminant), `ECB.Remove<CrowdAgent>`, emit `OffMeshTraversalStartedEvent`. Handle `MontageEndedEvent`
to resume (`Phase=Following`, re-add `CrowdAgent`, advance segment, `OffMeshTraversalEndedEvent`) or fail.

**Success conditions:** DD-Tests-Nav §4.1 (`OffMeshLinkDetectionSystemTests`) — [NAV-P9-T1](#nav-p9-t1);
suppression proven same-tick (no velocity write between detection and montage end).

---

# Phase 4 — Brain-side execution & action surface

### NAV-P4-T1
**`MoveToExecutor` extension — handle pass-through + new verdicts**
*Design refs:* §6.1; verified [MoveToExecutor.cs](../../FDP/Toolkits/Fdp.Toolkits/Navigation/Executors/MoveToExecutor.cs).

The executor is already a thin channel dispatcher. Extend it to: read `routeHandle` from params/blackboard
and write it into `NavigationIntent.RouteHandle` (0 = fire-and-forget); map the new `NavigationStatus.Result`
members (`PathFound`→Success, `FailedUnreachable`/`NoPath`/`FailedInvalidHandle`→Failure); emit
`MoveStarted`/`MoveCompleted` events. Preemption still via `ActionInstanceId`/`IntentId` bump.

**Success conditions:** DD-Tests-Nav §4.5 (`MoveToExecutorTests`) — [NAV-P9-T5](#nav-p9-t5).

### NAV-P4-T2
**New BTree action executors: `PlanRoute`, `FollowPath`, `FetchPathDetails`, `ReleasePath`; remove `FollowRoadGraph`**
*Design refs:* §3.2, §3.3, §6.1, §13.2, §13.6, §17.

Add `PlanRouteExecutor`, `FollowPathExecutor`, `FetchPathDetailsExecutor` (blocking polls
`BrainPathRegistry.IsCached`; non-blocking returns Success), `ReleasePathExecutor` (cache-only, idempotent,
does not stop movement). Wire `NavigationHandleAllocator.Allocate(self)` into `PlanRoute` BTree wrapper +
blackboard helpers. Remove `FollowRoadGraphExecutor` (collapse into `MoveTo`+`BackendForce=RoadGraph`); migrate
its callers/tests. Register the new `ActionId`s in the `LocomotionChannel` action set and
Blueprint Channel Command Catalog (§13.5).

**Success conditions:**
- Each executor writes the correct `ActiveAction`/`RouteHandle` (DD-Tests-Nav §4.5 rows) — [NAV-P9-T5](#nav-p9-t5).
- `FollowRoadGraphExecutor` removed; no dangling references; its old tests retired or repointed to `MoveTo`.
- BTree author can allocate a handle and round-trip PlanRoute→FollowPath (proven by S11).

### NAV-P4-T3
**Brain-side ingress: `NavigationPathDetailsUpdateSystem` + `NavigationPathDetailsResponseEvent`**
*Design refs:* §3.3, §5.4, §6.2; DD-Fake-Nav §6.2.

Define `NavigationPathDetailsResponseEvent` (Muscle→Brain; DDS in default/scale-out, local in all-in-one)
+ its `PathfindingTranslators`-style ingress (`Hrot.Network.NED`). Implement Brain-side
`NavigationPathDetailsUpdateSystem` consuming it, materializing `NavigationPathDetailsBuffer` into
`BrainPathRegistry` (updating `LastObservedReplanCount`), and firing `NavigationPathDetailsArrivedEvent`.

**Success conditions:** DD-Tests-Nav §4.6 (`NavigationPathDetailsUpdateSystemTests`) — [NAV-P9-T6](#nav-p9-t6).

### NAV-P4-T4
**Engine Event Catalog entries for navigation**
*Design refs:* §12.

Register all §12 events in `EngineEventCatalog` (DD-3 pattern), `TargetFieldName="Target"`:
`MoveStartedEvent`, `MoveCompletedEvent`, `PathReplannedEvent`, `OffMeshTraversalStartedEvent`,
`OffMeshTraversalEndedEvent`, `MoveBlockedEvent`, `WaypointReachedEvent` (Muscle-local, BestEffort, no network),
`NavigationPathDetailsArrivedEvent`. Wire `WhenNode(EventFired)` consumption.

**Success conditions:**
- Every event registered with correct QoS / Brain-visibility / target filter per the §12 table.
- `WaypointReachedEvent` never crosses the network (negative-space test, DD-Tests-Nav §7.4).
- A Brain BTree can react to `MoveCompletedEvent` via `WhenNode`.

---

# Phase 5 — Replan, corridor preview & auto-refresh

### NAV-P5-T1
**Muscle-internal replan flow + `ReplanCount` + `PathReplannedEvent`**
*Design refs:* §3.4, §5.4, §7.3.

Extend `NavigationExecutionSystem` (frustration watchdog, verified solver-agnostic): on frustration with
`ReplanCount < MaxReplans` and within `ReplanTimeBudget`, re-publish `PathfindingRequestEvent` (same handle),
replace the `TrajectoryPoolManager` entry in place, bump `NavigationStatus.ReplanCount`, fire
`PathReplannedEvent`; on budget exhaustion write `FailedBlocked`. Throttle `MoveBlockedEvent` (once per episode).

**Success conditions:** DD-Tests-Nav §4.4 (`NavigationProgressTrackerSystemTests`) — [NAV-P9-T4](#nav-p9-t4);
end-to-end via S5/S8.

### NAV-P5-T2
**`NavigationCorridorPreview` opt-in window (N=8) + `StreamCorridorPreview` plumbing**
*Design refs:* §4.2, §13.4.

Populate `NavigationCorridorPreview` for entities whose intent set `Flags.StreamCorridorPreview`; maintain
the 8-waypoint sliding window, bump `PreviewVersion` on slide/replan; SmartEgress dirty-gate on
`PreviewVersion`. Absent component for non-opted entities (zero replication). Reset/remove on completion.

**Success conditions:** S2b ([NAV-P10-T3](#nav-p10-t3)) — preview present only when opted in, `PreviewVersion`
increases ≥2×, `WaypointCount≤8`, `GlobalSegmentStart` advances; sibling S2 never gains the component.

### NAV-P5-T3
**`AutoSendPathOnReplan` auto-refresh**
*Design refs:* §3.3, §3.4, §5.4.

When `Flags.AutoSendPathOnReplan` was set (and `RouteHandle != 0`), each Muscle replan additionally fires
`NavigationPathDetailsResponseEvent{IsAutoRefresh=true}`, keeping `BrainPathRegistry` fresh without explicit fetch.

**Success conditions:** S5b ([NAV-P10-T6](#nav-p10-t6)) — auto-refresh event observed, cache returns new path,
`StaleMisses==0`; sibling S5 fires no details event.

---

# Phase 6 — Engine-backed module + editor wiring (DD-EngineBacked-Nav)

### NAV-P6-T1
**`EngineBackedNavmeshProvider` (direct-line placeholder)**
*Design refs:* DD-EngineBacked-Nav §3.

Implement in `Fdp.Toolkit.Navigation.EngineBacked`: `IsWalkable`→true, `PathExists`→distance≤maxCost,
`PathCost`→Euclidean, `SampleNavmeshPoints`→grid, `QueryVersion`→1, `PlanPath`→2 walk waypoints.

**Success conditions:** unit test of each method per §3.1; `PlanPath` emits exactly `[start,end]` with `Walk`.

### NAV-P6-T2
**`EngineBackedDtCrowdProvider` (no-op stub) + tag suppression**
*Design refs:* DD-EngineBacked-Nav §4.

No-op `IDtCrowdProvider` (`GetAgentVelocity`→Zero); `NavigationIntentBridgeSystem` skips the `CrowdAgent`
tag when this provider is active (capability bit / mode flag). Humanoids then move via `LinearKinematicsSystem`.

**Success conditions:** with this provider, an Infantry `MoveTo` gets no `CrowdAgent` tag and still moves
(covered by editor-wiring test [NAV-P6-T6](#nav-p6-t6)); frustration watchdog still functions on `SimVelocity`.

### NAV-P6-T3
**`EngineBackedVolumetricPathProvider` (direct-line 3D)**
*Design refs:* DD-EngineBacked-Nav §5.

Direct-line 3D `Plan` returning 2 waypoints. Satisfies interface so a flying `MoveTo` doesn't throw.

**Success conditions:** unit test: `Plan` returns 2 waypoints; `IsFlyable`/`PathExists`→true.

### NAV-P6-T4
**`EngineBackedPathRegistry` over `TrajectoryPoolManager`**
*Design refs:* DD-EngineBacked-Nav §6.

Real `IPathRegistry` adapter: `RouteHandle` == existing `NavState.TrajectoryId`; `TryGetWaypoints` reads
`CustomTrajectory` interior, wraps each as `NavWaypoint{Walk,Default}`. Register/refresh-in-place/free
lifecycle; strict `ReplanCount` cache-miss policy. Serves both Brain & Muscle in all-in-one.

**Success conditions:** read-back matches registered trajectory; `Free` removes; strict miss on stale `ReplanCount`.

### NAV-P6-T5
**`EngineBackedNavigationModule` + `EngineBackedPathResponseSystem` + host selection**
*Design refs:* DD-EngineBacked-Nav §7, §8.

Implement `EngineBackedNavigationModule : IEcsModule, IDisposable, IWindowRegistrar` registering the four
providers + `EngineBackedPathResponseSystem` (adapts `PathfindingResultEvent`→`CustomTrajectory` in
`TrajectoryPoolManager`, sets `NavState.TrajectoryId`/`Mode`, populates `NavigationCorridorMuscle`,
action-specific tail). Mutual-exclusion host selection vs `NavigationFakesModule` (§7.3); all-in-one only (§7.4).
Kinematics routing collapses to vehicles→`CarKinematicsSystem`(RoadGraph Dijkstra), humanoids→`LinearKinematicsSystem`.

**Success conditions:**
- Exactly one of `NavigationFakesModule`/`EngineBackedNavigationModule` registers; registering both throws.
- `Action_MoveTo` on a vehicle over a real `RoadNetworkBlob` plans (Dijkstra), follows, arrives.
- `PlanRoute`→`FollowPath`→`FetchPathDetails` work against the real trajectory pool (handle is real).

### NAV-P6-T6
**Wire `EngineBackedNavigationModule` into the SimHost/editor host so `MoveTo` keeps working** *(maintainer requirement)*
*Design refs:* DD-EngineBacked-Nav §7.3–7.4; Navigation §2 (all-in-one); editor wiring fact above.

Register `EngineBackedNavigationModule` in [SimHostNodeBootstrapper.cs](../../Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs)
(alongside `CoreLogicPack` / `EqsModule`) for the editor / headless / all-in-one host, providing it the
scenario `RoadNetworkBlob` + `TrajectoryPoolManager` already owned by `GroundKinematicsModule`. Ensure the
existing `MoveToLocation` mission path (`MoveToExecutor` → `NavigationIntent` → bridge → `NavState` →
kinematics) still resolves end-to-end after the action-layer changes ([NAV-P0-T4](#nav-p0-t4),
[NAV-P1-T2](#nav-p1-t2)). Default scenarios select `NavigationBackend.EngineBacked`.

**Success conditions:**
- Existing editor/headless `MoveTo` integration tests stay green:
  `SimHost_MoveToLocationMission_EntityMovesWithoutGhostTick`,
  `MoveToLocation_TankNavigates_GeoSpatialChangesAfter10s`,
  `EntityMission_MoveToLocation_DoesNotThrowMissingNavigationIntent`.
- A new integration test: editor-issued `MoveToLocation` on a vehicle reaches the destination with
  `EngineBackedNavigationModule` active (real road graph), `MoveCompletedEvent.Reason==Arrived`.
- No double-registration of nav providers; host boots without exceptions.

### NAV-P6-T7
**Diagnostic window reuse in engine-backed mode**
*Design refs:* DD-EngineBacked-Nav §9.

`FakeNavigationInspectorWindow` (from [NAV-P7-T1](#nav-p7-t1)) detects the active provider type at draw time:
Navmesh/Crowd/Volumetric tabs show placeholder/disabled controls; Paths tab fully functional over the real
`TrajectoryPoolManager`; header shows "Backend: EngineBacked".

**Success conditions:** with engine-backed module active, Paths tab lists real trajectories; navmesh/crowd
controls disabled with tooltip; no exceptions.

---

# Phase 7 — Diagnostics, snapshot & gizmos

### NAV-P7-T1
**`FakeNavigationInspectorWindow` — four-tab ImGui inspector**
*Design refs:* DD-Fake-Nav §8.

Implement the four-tab window (Navmesh / Crowd / Volumetric / Paths) registered via `IWindowRegistrar`
(precedent: `Hrot.MuscleCharacter.Animation.Fake` + `Hrot.Editor.AiShared`
[SharedAiWindowRegistrar.cs](../../Hrot/Editor/Hrot.Editor.AiShared/Windows/SharedAiWindowRegistrar.cs)),
headless-guarded. Tab contents per §8.2–8.5 (polygon tree + block/bump; agent list/detail; no-fly zones;
Muscle pool / Brain caches / stats). Footer: Snapshot JSON / Reset crowd / Reload map.

**Success conditions:** window opens in editor; each tab renders loaded fake state; block/bump/override
buttons drive the test APIs; not registered in headless builds.

### NAV-P7-T2
**JSON snapshot export + AAR recording integration**
*Design refs:* DD-Fake-Nav §9, §10.

"Snapshot JSON" serializes all four backends + tick + map name to clipboard (§9 schema). Confirm fake state
components are `[ComponentId]` Tier-1 unmanaged so the Flight Recorder records them automatically; verify
replay restores agent state and that `Update` is suppressed under replay isolation (§10 caveats documented).

**Success conditions:** snapshot JSON matches the §9 schema for a known scenario; an AAR record→replay round
trip restores `FakeCrowdAgentState` and `FakeNavmeshState.Version`.

### NAV-P7-T3
**Planned-path gizmo from the 8-waypoint corridor preview** *(maintainer /btw request)*
*Design refs:* §4.2, §13.4; depends on [NAV-P5-T2](#nav-p5-t2).

Add an editor gizmo that renders the planned path of the **currently selected entity** by reading its
`NavigationCorridorPreview` (up to 8 `PreviewWaypoint`s): draw the polyline + waypoint markers, colored by
`TraversalKind`, in world space. On selection, auto-enable `StreamCorridorPreview` for that entity (and clear
it on deselection so non-inspected entities pay zero replication). Register through the editor gizmo infra
(precedent: [LocationPickerGizmo.cs](../../Hrot/Subsystems/Hrot.Editor/Gizmos/LocationPickerGizmo.cs)).

**Success conditions:**
- Selecting a moving entity shows its up-to-8-segment lookahead polyline; the line advances as `PreviewVersion` bumps.
- Deselecting removes the gizmo and clears `StreamCorridorPreview` (component absent → zero replication).
- Off-mesh segments (`TraversalKind != Walk`) render with a distinct marker.
- Headless-guarded; no effect in headless/test builds.

---

# Phase 8 — Layer-1 unit tests (DD-Tests-Nav §3)

Each task = the named NUnit fixture; success = all rows in the referenced DD-Tests-Nav table pass.
Tests live in the existing `Fdp.Toolkits.Tests` (`Navigation/` folder) — see [NAV-P0-T1](#nav-p0-t1).

### NAV-P8-T1
**`FakeNavmeshProviderTests`** — *DD-Tests-Nav §3.1* (14 rows: walkable/blocked/layer-mask, project, path-exists/cost, off-mesh link cost & traversal-kind, version bump, determinism). Depends on [NAV-P2-T1](#nav-p2-t1).

### NAV-P8-T2
**`FakeDtCrowdProviderTests`** — *DD-Tests-Nav §3.2* (10 rows: register/unregister, converge, idle, crossing avoid, deadlock, override, determinism, 200-agent stress). Depends on [NAV-P2-T2](#nav-p2-t2).

### NAV-P8-T3
**`FakeVolumetricPathProviderTests`** — *DD-Tests-Nav §3.3* (5 rows: straight line, route-around, start/end inside zone, altitude-max). Depends on [NAV-P2-T3](#nav-p2-t3).

### NAV-P8-T4
**`MusclePathRegistryTests`** — *DD-Tests-Nav §3.4* (9 rows: register/replace/free, get/slice, handle-range no-collision). Depends on [NAV-P2-T4](#nav-p2-t4).

### NAV-P8-T5
**`BrainPathRegistryTests`** — *DD-Tests-Nav §3.5* (7 rows: ingest/miss/stale-miss, LRU cap, per-entity eviction, stats). Depends on [NAV-P2-T4](#nav-p2-t4).

### NAV-P8-T6
**`SharedPathRegistryTests`** — *DD-Tests-Nav §3.6* (3 rows: same data both views, same-tick visibility, no staleness in shared mode). Depends on [NAV-P2-T4](#nav-p2-t4).

---

# Phase 9 — Layer-2 system tests (DD-Tests-Nav §4)

Tests live in `Fdp.Toolkits.Tests` (`Navigation/` folder). One fixture per system; success = all rows in the referenced table.

### NAV-P9-T1
**`OffMeshLinkDetectionSystemTests`** — *DD-Tests-Nav §4.1* (7 rows incl. same-tick suppression, tag removal via ECB, event emission). Depends on [NAV-P3-T2](#nav-p3-t2).

### NAV-P9-T2
**`CrowdAgentUpdateSystemTests`** — *DD-Tests-Nav §4.2* (4 rows: velocity write, suppression, filter exclusion, resume). Depends on [NAV-P3-T1](#nav-p3-t1).

### NAV-P9-T3
**`NavigationIntentBridgeSystemTests`** — *DD-Tests-Nav §4.3* (16 rows: crowd tag/registration, local path-request publish, vehicle no-tag, PlanRoute/FollowPath/Fetch/Release routing, instance-id idempotency). Depends on [NAV-P1-T2](#nav-p1-t2), [NAV-P3-T1](#nav-p3-t1).

### NAV-P9-T4
**`NavigationProgressTrackerSystemTests`** — *DD-Tests-Nav §4.4* (10 rows: MoveStarted, waypoint locality, arrival/fail reasons, throttled MoveBlocked, replan event & count, auto-refresh on/off, budget exhaustion). Depends on [NAV-P4-T4](#nav-p4-t4), [NAV-P5-T1](#nav-p5-t1), [NAV-P5-T3](#nav-p5-t3).

### NAV-P9-T5
**`MoveToExecutorTests`** (+ new executors) — *DD-Tests-Nav §4.5* (14 rows: intent write, handle default/explicit, status→verdict mapping, PlanRoute/FollowPath/Fetch(blocking/non)/Release, preemption). Depends on [NAV-P4-T1](#nav-p4-t1), [NAV-P4-T2](#nav-p4-t2).

### NAV-P9-T6
**`NavigationPathDetailsUpdateSystemTests`** — *DD-Tests-Nav §4.6* (5 rows: populate registry, fire arrived event, auto-refresh flag, LastObservedReplanCount, LRU cap). Depends on [NAV-P4-T3](#nav-p4-t3).

---

# Phase 10 — Layer-3 integration scenarios (DD-Tests-Nav §6)

Integration tests run all-in-one via `NavTestHarness` (DD-Tests-Nav §2.3, §5, §7, §8), in an existing
suitable integration-test assembly or one new project if none fits ([NAV-P0-T1](#nav-p0-t1)). **Each scenario is one task** (maintainer: reuse the integration tests as tasks). Success =
all asserts in the referenced DD-Tests-Nav section. Prerequisite for all: [NAV-P10-T0](#nav-p10-t0).

### NAV-P10-T0
**`NavTestHarness` + helpers + inline TKB templates**
*Design refs:* DD-Tests-Nav §2.3, §7, §8.

Implement `NavTestHarness` (all-in-one kernel, fakes, solver, Muscle + Brain systems, `CapturedEventLog`),
`PumpUntil`/`PumpFor`, `AssertNoBrainEvent<T>`, and `NavTestTemplates` (Infantry/Wheeled/Naval/Flying).

**Success conditions:** harness builds an all-in-one world with zero DDS; spawn + issue helpers work; a trivial
`IssueMoveTo` reaches `MoveCompletedEvent`.

### NAV-P10-T1
**`S1_SimpleCorridor`** — *DD-Tests-Nav §6.1*. Happy-path full pipeline. Map `corridor.json`.

### NAV-P10-T2
**`S2_LBendFollow`** — *DD-Tests-Nav §6.2*. Multi-segment corridor following + `CurrentSegmentIndex` advance. Map `l_bend.json`.

### NAV-P10-T3
**`S2b_LBendWithCorridorPreview`** — *DD-Tests-Nav §6.2b*. Opt-in preview window; sibling control vs S2. Depends on [NAV-P5-T2](#nav-p5-t2).

### NAV-P10-T4
**`S3_TwoLayersRouting`** — *DD-Tests-Nav §6.3*. `NavLayerMask` per-layer routing (Infantry vs Wheeled). Map `two_layers.json`.

### NAV-P10-T5
**`S4_OffMeshJumpAcross`** — *DD-Tests-Nav §6.4*. Off-mesh sequence + zero-frame suppression. Map `off_mesh_jump.json`. Depends on [NAV-P3-T2](#nav-p3-t2).

### NAV-P10-T6
**`S5_ReplanOnNavmeshPatch` + `S5b_ReplanWithAutoRefresh`** — *DD-Tests-Nav §6.5/§6.5b*. Muscle-internal replan; auto-refresh sibling. Map `replan.json`. Depends on [NAV-P5-T1](#nav-p5-t1), [NAV-P5-T3](#nav-p5-t3).

### NAV-P10-T7
**`S6_CrowdAvoidance`** — *DD-Tests-Nav §6.6*. Four crossing agents, no collisions/deadlocks. Map `crowded.json`. Depends on [NAV-P3-T1](#nav-p3-t1).

### NAV-P10-T8
**`S7_FailedUnreachable`** — *DD-Tests-Nav §6.7*. Fast correct failure; no `MoveStartedEvent`. Map `stuck.json`.

### NAV-P10-T9
**`S8_FrustrationWatchdog`** — *DD-Tests-Nav §6.8*. Deadlock → `FailedBlocked` after budget; throttled `MoveBlockedEvent`. Map `frustration.json`. Depends on [NAV-P5-T1](#nav-p5-t1).

### NAV-P10-T10
**`S9_FlyingAgentRouting`** — *DD-Tests-Nav §6.9*. `MobilityProfile=Flying`→volumetric; 3D waypoints; no crowd. Map `flying.json`. Depends on [NAV-P2-T3](#nav-p2-t3).

### NAV-P10-T11
**`S10_NavalLayerRouting`** — *DD-Tests-Nav §6.10*. Naval layer + `NavState.Mode=Naval`; around island. Map `naval.json`.

### NAV-P10-T12
**`S11_PlanRouteThenFollowPath`** — *DD-Tests-Nav §6.11*. Mode-2 plan-then-commit; no re-plan on FollowPath. Map `corridor.json`. Depends on [NAV-P4-T2](#nav-p4-t2).

### NAV-P10-T13
**`S12_FetchPathDetailsAndCacheInvalidation`** — *DD-Tests-Nav §6.12*. On-demand fetch + strict stale-miss + refetch. Map `replan.json`. Depends on [NAV-P4-T3](#nav-p4-t3).

---

## Notes on deferred / out-of-scope (not tasks here)

Per Navigation §1/§19/§20-Phase-B and DD scopes: real DotRecast/dtCrowd/volumetric backends, EQS
template `NavLayerMask` parameterization (separate mandatory mechanical doc), formations, flow fields,
threat-aware cost, submarine depth, root-motion authority flip, navmesh-patch propagation runtime, and the
scale-out DDS test suite (DD-Tests-Nav §10) are **not** tasked here. Forward-compat API hooks (§14) are
covered incidentally by [NAV-P1-T1](#nav-p1-t1)/[NAV-P0-T3](#nav-p0-t3) (`QueryVersion`, `NavmeshVersionAtPlan`).
