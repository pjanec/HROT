# Navigation Subsystem v2 — Task Tracker

**Reference:** See [TASK-DETAILS.md](./TASK-DETAILS.md) for detailed task descriptions and success conditions.
**Design:** [Navigation_Design_v2_0.md](./Navigation_Design_v2_0.md) · [DD-Fake-Nav.md](./DD-Fake-Nav.md) · [DD-EngineBacked-Nav.md](./DD-EngineBacked-Nav.md) · [DD-Tests-Nav.md](./DD-Tests-Nav.md)
**Debt:** [DEBT-TRACKER.md](./DEBT-TRACKER.md)

> ⚠️ Read the **"Verified codebase facts & design discrepancies"** section of TASK-DETAILS first —
> several design claims (INavmeshProvider, KinematicsMode collision, NavigationIntent layout, assembly placement)
> do not match the codebase and are corrected by Phase-0 tasks.

Status legend: `[ ]` not done · `[x]` done.

---

## Phase 0 — Foundations, contracts & corrective migration

**Goal:** Reconcile the design contracts with the real codebase before building on them.

- [ ] **NAV-P0-T1** Place new nav code in existing `Fdp.Toolkits` (no new assemblies) [details](./TASK-DETAILS.md#nav-p0-t1)
- [ ] **NAV-P0-T2** Resolve `KinematicsMode` extension without enum collision [details](./TASK-DETAILS.md#nav-p0-t2)
- [ ] **NAV-P0-T3** Redefine `INavmeshProvider` + migrate EQS callers [details](./TASK-DETAILS.md#nav-p0-t3)
- [ ] **NAV-P0-T4** Action-based command layer (`ActiveAction`/params/`RouteHandle`) [details](./TASK-DETAILS.md#nav-p0-t4)
- [ ] **NAV-P0-T5** `NavWaypoint`/enums/`NavAgentProfile`/corridor components + ComponentIds [details](./TASK-DETAILS.md#nav-p0-t5)

## Phase 1 — Muscle ↔ Solver path-query pipeline

**Goal:** Route intents to the solver and materialize corridors on the Muscle.

- [ ] **NAV-P1-T1** Extend `PathfindingRequestEvent`/result (handle, layer, backend, cost) [details](./TASK-DETAILS.md#nav-p1-t1)
- [ ] **NAV-P1-T2** `NavigationIntentBridgeSystem` — publish requests & route by action [details](./TASK-DETAILS.md#nav-p1-t2)
- [ ] **NAV-P1-T3** Multi-modal backend selection in `PathfindingSolverSystem` [details](./TASK-DETAILS.md#nav-p1-t3)
- [ ] **NAV-P1-T4** Response materialization → `NavigationCorridorMuscle` + status; resize batch to 256 [details](./TASK-DETAILS.md#nav-p1-t4)

## Phase 2 — Fake backends (DD-Fake-Nav)

**Goal:** Render-free, deterministic provider implementations + test map.

- [ ] **NAV-P2-T1** `FakeNavmeshProvider` + polygon A* + test API [details](./TASK-DETAILS.md#nav-p2-t1)
- [ ] **NAV-P2-T2** `FakeDtCrowdProvider` + `IDtCrowdProvider` + tick algorithm [details](./TASK-DETAILS.md#nav-p2-t2)
- [ ] **NAV-P2-T3** `FakeVolumetricPathProvider` + `IVolumetricPathProvider` [details](./TASK-DETAILS.md#nav-p2-t3)
- [ ] **NAV-P2-T4** `IPathRegistry` + Muscle/Brain/Shared registries + allocator [details](./TASK-DETAILS.md#nav-p2-t4)
- [ ] **NAV-P2-T5** `NavTestMap` (JSON+DSL) + canonical fixtures + `NavigationFakesModule` [details](./TASK-DETAILS.md#nav-p2-t5)

## Phase 3 — Crowd, off-mesh traversal & animation seam

**Goal:** dtCrowd routing, zero-frame off-mesh suppression, montage emit.

- [ ] **NAV-P3-T1** `CrowdAgent` admission + `CrowdAgentUpdateSystem` + kinematics `.Without` filters [details](./TASK-DETAILS.md#nav-p3-t1)
- [ ] **NAV-P3-T2** `OffMeshLinkDetectionSystem` + zero-frame suppression + montage [details](./TASK-DETAILS.md#nav-p3-t2)

## Phase 4 — Brain-side execution & action surface

**Goal:** Thin Brain dispatch + the full action surface + event catalog.

- [ ] **NAV-P4-T1** `MoveToExecutor` extension — handle pass-through + verdicts [details](./TASK-DETAILS.md#nav-p4-t1)
- [ ] **NAV-P4-T2** New executors `PlanRoute`/`FollowPath`/`FetchPathDetails`/`ReleasePath`; remove `FollowRoadGraph` [details](./TASK-DETAILS.md#nav-p4-t2)
- [ ] **NAV-P4-T3** Brain ingress `NavigationPathDetailsUpdateSystem` + response event [details](./TASK-DETAILS.md#nav-p4-t3)
- [ ] **NAV-P4-T4** Engine Event Catalog entries (§12) [details](./TASK-DETAILS.md#nav-p4-t4)

## Phase 5 — Replan, corridor preview & auto-refresh

**Goal:** Muscle-internal recovery + opt-in introspection surfaces.

- [ ] **NAV-P5-T1** Muscle-internal replan + `ReplanCount` + `PathReplannedEvent` [details](./TASK-DETAILS.md#nav-p5-t1)
- [ ] **NAV-P5-T2** `NavigationCorridorPreview` opt-in 8-waypoint window [details](./TASK-DETAILS.md#nav-p5-t2)
- [ ] **NAV-P5-T3** `AutoSendPathOnReplan` auto-refresh [details](./TASK-DETAILS.md#nav-p5-t3)

## Phase 6 — Engine-backed module + editor wiring (DD-EngineBacked-Nav)

**Goal:** Real road-network demoability; keep editor `MoveTo` working.

- [ ] **NAV-P6-T1** `EngineBackedNavmeshProvider` (direct-line placeholder) [details](./TASK-DETAILS.md#nav-p6-t1)
- [ ] **NAV-P6-T2** `EngineBackedDtCrowdProvider` (stub) + tag suppression [details](./TASK-DETAILS.md#nav-p6-t2)
- [ ] **NAV-P6-T3** `EngineBackedVolumetricPathProvider` (direct-line 3D) [details](./TASK-DETAILS.md#nav-p6-t3)
- [ ] **NAV-P6-T4** `EngineBackedPathRegistry` over `TrajectoryPoolManager` [details](./TASK-DETAILS.md#nav-p6-t4)
- [ ] **NAV-P6-T5** `EngineBackedNavigationModule` + response system + host selection [details](./TASK-DETAILS.md#nav-p6-t5)
- [ ] **NAV-P6-T6** Wire module into SimHost/editor host — `MoveTo` keeps working [details](./TASK-DETAILS.md#nav-p6-t6)
- [ ] **NAV-P6-T7** Diagnostic window reuse in engine-backed mode [details](./TASK-DETAILS.md#nav-p6-t7)

## Phase 7 — Diagnostics, snapshot & gizmos

**Goal:** Developer visibility incl. the planned-path gizmo.

- [ ] **NAV-P7-T1** `FakeNavigationInspectorWindow` — four-tab ImGui inspector [details](./TASK-DETAILS.md#nav-p7-t1)
- [ ] **NAV-P7-T2** JSON snapshot export + AAR recording integration [details](./TASK-DETAILS.md#nav-p7-t2)
- [ ] **NAV-P7-T3** Planned-path gizmo from 8-waypoint corridor preview [details](./TASK-DETAILS.md#nav-p7-t3)

## Phase 8 — Layer-1 unit tests (DD-Tests-Nav §3)

**Goal:** Each fake proven in isolation.

- [ ] **NAV-P8-T1** `FakeNavmeshProviderTests` [details](./TASK-DETAILS.md#nav-p8-t1)
- [ ] **NAV-P8-T2** `FakeDtCrowdProviderTests` [details](./TASK-DETAILS.md#nav-p8-t2)
- [ ] **NAV-P8-T3** `FakeVolumetricPathProviderTests` [details](./TASK-DETAILS.md#nav-p8-t3)
- [ ] **NAV-P8-T4** `MusclePathRegistryTests` [details](./TASK-DETAILS.md#nav-p8-t4)
- [ ] **NAV-P8-T5** `BrainPathRegistryTests` [details](./TASK-DETAILS.md#nav-p8-t5)
- [ ] **NAV-P8-T6** `SharedPathRegistryTests` [details](./TASK-DETAILS.md#nav-p8-t6)

## Phase 9 — Layer-2 system tests (DD-Tests-Nav §4)

**Goal:** Each system proven against synthetic ECS state.

- [ ] **NAV-P9-T1** `OffMeshLinkDetectionSystemTests` [details](./TASK-DETAILS.md#nav-p9-t1)
- [ ] **NAV-P9-T2** `CrowdAgentUpdateSystemTests` [details](./TASK-DETAILS.md#nav-p9-t2)
- [ ] **NAV-P9-T3** `NavigationIntentBridgeSystemTests` [details](./TASK-DETAILS.md#nav-p9-t3)
- [ ] **NAV-P9-T4** `NavigationProgressTrackerSystemTests` [details](./TASK-DETAILS.md#nav-p9-t4)
- [ ] **NAV-P9-T5** `MoveToExecutorTests` (+ new executors) [details](./TASK-DETAILS.md#nav-p9-t5)
- [ ] **NAV-P9-T6** `NavigationPathDetailsUpdateSystemTests` [details](./TASK-DETAILS.md#nav-p9-t6)

## Phase 10 — Layer-3 integration scenarios (DD-Tests-Nav §6)

**Goal:** The assembled Brain ↔ Muscle ↔ Solver mechanism, all-in-one.

- [ ] **NAV-P10-T0** `NavTestHarness` + helpers + inline TKB templates [details](./TASK-DETAILS.md#nav-p10-t0)
- [ ] **NAV-P10-T1** `S1_SimpleCorridor` [details](./TASK-DETAILS.md#nav-p10-t1)
- [ ] **NAV-P10-T2** `S2_LBendFollow` [details](./TASK-DETAILS.md#nav-p10-t2)
- [ ] **NAV-P10-T3** `S2b_LBendWithCorridorPreview` [details](./TASK-DETAILS.md#nav-p10-t3)
- [ ] **NAV-P10-T4** `S3_TwoLayersRouting` [details](./TASK-DETAILS.md#nav-p10-t4)
- [ ] **NAV-P10-T5** `S4_OffMeshJumpAcross` [details](./TASK-DETAILS.md#nav-p10-t5)
- [ ] **NAV-P10-T6** `S5_ReplanOnNavmeshPatch` + `S5b_ReplanWithAutoRefresh` [details](./TASK-DETAILS.md#nav-p10-t6)
- [ ] **NAV-P10-T7** `S6_CrowdAvoidance` [details](./TASK-DETAILS.md#nav-p10-t7)
- [ ] **NAV-P10-T8** `S7_FailedUnreachable` [details](./TASK-DETAILS.md#nav-p10-t8)
- [ ] **NAV-P10-T9** `S8_FrustrationWatchdog` [details](./TASK-DETAILS.md#nav-p10-t9)
- [ ] **NAV-P10-T10** `S9_FlyingAgentRouting` [details](./TASK-DETAILS.md#nav-p10-t10)
- [ ] **NAV-P10-T11** `S10_NavalLayerRouting` [details](./TASK-DETAILS.md#nav-p10-t11)
- [ ] **NAV-P10-T12** `S11_PlanRouteThenFollowPath` [details](./TASK-DETAILS.md#nav-p10-t12)
- [ ] **NAV-P10-T13** `S12_FetchPathDetailsAndCacheInvalidation` [details](./TASK-DETAILS.md#nav-p10-t13)
