# Stride Integration — Task Tracker

**Reference:** see [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions and success
conditions, and [Stride-Integration_v0_3.md](./Stride-Integration_v0_3.md) for the design.
Technical debt: [DEBT-TRACKER.md](./DEBT-TRACKER.md).

Status: `[ ]` not done · `[x]` done. Build Mode 1 (P0–P5) first, then Mode 2 (P6).

---

## Phase 0 — Scaffolding, coordinate seam, first render

**Goal:** `editor_stride` boots under `OfflineNetworkFactory`, Stride drives its own loop,
entities spawn (owned instantly) and render — movement stubbed (v0.3 §14 step 0).

- [x] **STR-P0-T1** Create `Hrot.Stride.Core` project + references [details](./TASK-DETAIL.md#str-p0-t1-create-hrotstridecore-project-and-references) — BATCH-01 ✅
- [x] **STR-P0-T2** Create `Hrot.Stride.Animation` project + references [details](./TASK-DETAIL.md#str-p0-t2-create-hrotstrideanimation-project-and-references) — BATCH-01 ✅
- [x] **STR-P0-T3** Wire `HrotStrideApp.Game` references [details](./TASK-DETAIL.md#str-p0-t3-wire-hrotstrideappgame-references) — BATCH-01 ✅
- [x] **STR-P0-T4** `FdpStrideTransform` coordinate seam [details](./TASK-DETAIL.md#str-p0-t4-fdpstridetransform-coordinate-seam) — BATCH-01 ✅
- [x] **STR-P0-T5** `StrideHrotGame` external host loop [details](./TASK-DETAIL.md#str-p0-t5-stridehrotgame-external-host-loop) — BATCH-02 ✅ (GPU/render path deferred to T8 smoke)
- [x] **STR-P0-T6** `EditorStrideSubsystem` composition skeleton [details](./TASK-DETAIL.md#str-p0-t6-editorstridesubsystem-composition-skeleton) — BATCH-02 ✅
- [x] **STR-P0-T7** `StrideVisualBindingSystem` + procedural fallback [details](./TASK-DETAIL.md#str-p0-t7-stridevisualbindingsystem-and-procedural-fallback) — BATCH-03 ✅ (procedural mesh = STR-D9)
- [x] **STR-P0-T8** End-to-end spawn + render smoke [details](./TASK-DETAIL.md#str-p0-t8-end-to-end-spawn-and-render-smoke) — BATCH-03 ✅ (real GPU render carried as STR-D4 manual obligation)

**Live physics demo — GPU-VERIFIED WORKING (BATCH-17, user-confirmed 2026-06-04):** `editor_stride` uses the concrete `BulletPhysicsBodyService` (real Bullet). Three keyboard-triggered harness cases all confirmed on the GPU by the user:
- **D0 Physics Drop** — capsule falls under gravity and rests on the floor.
- **F1 Physics Walk** — mannequin walks via `CrowdMotorIntent → BulletCharacterMotor → CharacterComponent`, **animates** (locomotion blend driven by measured SimVelocity), and stops cleanly at walls.
- **F2 Physics Drive** — the vehicle is a **Bullet DYNAMIC rigidbody** (deviation from the original kinematic design — see STR-D18): rests on the floor, drives smoothly, **turns at exactly the commanded yaw rate** (`[VehicleYaw] ratio=100%`), and stops/slides at walls via the solver.
- **F3 Drive To Waypoint** — closed-loop `VehicleWaypointController` steers the dynamic car through 3 visible waypoint markers (`reached 3/3`); backed by 33 headless convergence tests incl. perturbed/laggy models — PROOF that the dynamic vehicle is steerable to a goal (the navigation prerequisite).

Follow-ups recorded as debt: STR-D17 (placeholder vehicle model), STR-D18 (kinematic→dynamic vehicle deviation, revisit for Mode-2), STR-D19 (wire the steering controller into the real navmesh path for obstacle-avoiding navigation).

**Navmesh runtime + vehicle navigation (BATCH-18, 2026-06-04):** Real DotRecast navmesh baked from the arena's 144 static colliders at runtime (`StrideSceneGeometrySource` box-exact + AABB-fallback extraction → `StrideNavmeshBaker` → `DotRecastNavmeshProvider` registered as `INavmeshProvider` singleton replacing the fake). F4 demo "Navmesh Drive" spawns the MilitaryAPC west of an obstacle, plans a DotRecast path to the east goal, and drives it via `VehicleWaypointController` along the path corners. 9 new headless tests (box extraction math + PlanPath-around-wall integration). **GPU-VERIFIED WORKING (user-confirmed): the APC follows the planned navmesh path corners.** Partially addresses STR-D19 (vehicle nav wired; crowd nav still uses FakeDtCrowdProvider).

**Infantry crowd navigation (BATCH-19, 2026-06-04):** `FakeDtCrowdProvider` replaced by a real `DotRecastDtCrowdProvider` using the Infantry DtNavMesh (already baked by BATCH-18 startup). Deferred-init pattern: provider constructed before bake, initialized after via `TryInitializeNavMesh`. `NavigationIntentBridgeSystem` wired with the real crowd provider (3-arg constructor). F5 demo "Navmesh Walk" (key F5, index 14): spawns InfantrySoldier mannequin west of interior walls, registers it as a DtCrowd agent on the Infantry navmesh, and the mannequin walks (animated) around the wall obstacles to the east-north goal marker. Chain: `DotRecastDtCrowdProvider → CrowdAgentUpdateSystem → CrowdMotorIntent → BulletCharacterMotor → CharacterComponent → walk+animation`. 5 new headless tests (Infantry bake+TryGetNavMesh, deferred-init no-op/functional/idempotent, L-corridor detour proof, full chain IntentSystem→CrowdMotorIntent). Discharges STR-D19 (crowd nav now real). **GPU-VERIFIED WORKING (user-confirmed): the mannequin pathfinds and walks (animated) to the goal over the live navmesh.** Fix applied: `CrowdAgent` component type registered in `EditorStrideSubsystem.Initialize` (was missing) + start/goal snapped to nearest navmesh polygon.

**Production-front-door navigation (BATCH-20, 2026-06-05):** Navigation now driven through the **PRODUCTION FDP command interface** (`NavigationIntent` / `LocomotionChannel`) for BOTH characters and vehicles — closing the BATCH-19 fidelity gap. **Front-door trigger discovered:** `NavigationIntentBridgeSystem` auto-registers a crowd agent on the `LocomotionChannel` MoveTo *action* (`ActiveAction=ActionIdMoveTo` + fresh `ActionInstanceId`), NOT on `NavigationIntent` alone; `MoveToExecutor` is NOT ticked in editor_stride, so the harness sets the channel action exactly as a BehaviorTree node does (and also writes `NavigationIntent` the way `MoveToExecutor.OnEnter` does). **F6 "FDP Move Order (char)" (index 15):** spawns an InfantrySoldier, issues the production MoveTo via `FdpNavigationOrders.IssueMoveTo` (new helper in Hrot.Stride.Core); the bridge AUTO-REGISTERS the crowd agent (no direct `RegisterAgent`) and the mannequin pathfinds around a wall. **F7 "FDP Move Order (vehicle)" (index 16):** spawns a MilitaryAPC and sets `NavigationIntent` (DirectPoint, goal behind a wall) — no manual PlanPath; the **new `VehicleNavigationIntentSystem`** (Hrot.Stride.Core, Simulation phase after `NavigationExecutionSystem`, reads the `INavmeshProvider` singleton) plans the DotRecast path, steers via `VehicleWaypointController`, advances corners, and sets `NavigationStatus.Arrived`. Keymap extended F1–F12. 6 new headless tests: B20-A1 (char front door → bridge auto-registration → nonzero `CrowdMotorIntent`, north detour), B20-A2 (vehicle excluded from crowd bridge), B20-B1 (vehicle plans + steers toward first corner), B20-B2 (closed-loop: corners advance + Arrived at goal, route detours |X|>4 around wall), B20-B3 (NoPath → halt + NavigationStatus=NoPath), B20-B4 (no-navmesh graceful no-op). Headless-tested green, but **GPU TEST FAILED (user-confirmed 2026-06-05): F6 and F7 spawn the entity but it does NOT move.** Diagnostics captured: **F6** — `bridgeRegisteredAgent=False` (the `LocomotionChannel` MoveTo action did NOT trigger crowd auto-registration in the live system) and `NavigationStatus` goes `Arrived` immediately while the mannequin sits at spawn → nothing drives it. **F7** — `VehicleNavigationIntentSystem` DOES plan the path (11 corners) and command `VehicleState(spd≈2, steer=0.7)`, but the dynamic body never moves (pos frozen) → the commanded `VehicleState` isn't reaching/driving the body on this path (NavStatus=FailedBlocked). **STR-D19 NOT fully resolved — see STR-D21.** Deferred at user request to do the P5 editor work (gizmos/dual-window) first. See `reports/BATCH-20-REPORT.md`.

**Navigation milestone (BATCH-18/19) — GPU-VERIFIED:** both vehicle (F4) and character (F5) navigate the live DotRecast navmesh baked from the real arena geometry. NOTE: F5 registers the crowd agent directly (RegisterAgent) for the demo; the fully HROT-faithful path (set `NavigationIntent` → action/`NavigationIntentBridgeSystem` auto-registers the agent) is now the F6/F7 BATCH-20 demos.

**3D gizmo GPU draw sink (BATCH-21, 2026-06-05):** Resolves **STR-D16**. `PooledEntityDebugDrawSink3D` in `Hrot.Stride.Core`: pool of Stride entities with emissive materials (unit cube for lines + boxes, UV sphere for spheres) so gizmo shapes actually render in the Stride window. Wired into `StrideHrotGame.BootEditorSubsystem` and `EditorStrideSubsystem.Initialize`. The D8 "Draw Test Gizmo" harness case upgraded to emit a rich set of shapes: R/G/B axis triad (2 m), red sphere r=0.75 floating 2 m up, white sphere, cyan/magenta/yellow line segments — all at Stride (0,0,6), persisting 8 s. `IDebugDrawSink3D` extended with `BeginFrame()`/`EndFrame()` default no-op methods. 20 new headless tests: `RotationFromTo` geometry (5 cases), line midpoint+length math (3), sphere scale formula (4), default interface no-op, `Sink` property, host frame-boundary protocol. All three test suites green: Core 327/327, Animation 48/48, Game 136/136. **GPU verification pending user pressing D8.**

**Live bring-up (BATCH-10, glue):** `HrotStrideApp` entry point now boots `editor_stride` and renders the UrbanCombat demo entities as Stride models (static; physics still NoOp). First visually-testable build — see `reports/BATCH-10-REPORT.md` for the run procedure. Discharges STR-D4 pending a GPU run.

## Phase 1 — Bullet movement + reverse-sync

**Goal:** Bullet authoritative; reverse-sync writes `SimTransform`/`SimVelocity`; FDP integrators
off (v0.3 §14 step 1).

- [x] **STR-P1-T1** `StrideKinematicsModule` [details](./TASK-DETAIL.md#str-p1-t1-stridekinematicsmodule) — BATCH-04 ✅
- [x] **STR-P1-T2** `PhysicsBodyLifecycleSystem` + `PhysicsBodyReference` [details](./TASK-DETAIL.md#str-p1-t2-physicsbodylifecyclesystem-and-physicsbodyreference) — BATCH-04 ✅ (IPhysicsBodyService seam; concrete impl deferred = STR-D11)
- [x] **STR-P1-T3** `BulletCharacterMotor` [details](./TASK-DETAIL.md#str-p1-t3-bulletcharactermotor) — BATCH-05 ✅
- [x] **STR-P1-T4** `KinematicVehicleMotor` (collision response + velocity) [details](./TASK-DETAIL.md#str-p1-t4-kinematicvehiclemotor) — BATCH-05 ✅
- [x] **STR-P1-T5** `BulletReverseSyncSystem` (togglable group) [details](./TASK-DETAIL.md#str-p1-t5-bulletreversesyncsystem) — BATCH-06 ✅ (resolves STR-D5)
- [x] **STR-P1-T6** `SplitAuthorityStrideSyncScript` [details](./TASK-DETAIL.md#str-p1-t6-splitauthoritystridesyncscript) — BATCH-06 ✅
- [x] **STR-P1-T7** Fixed timestep + reverse-sync ordering [details](./TASK-DETAIL.md#str-p1-t7-fixed-timestep-and-reverse-sync-ordering) — BATCH-06 ✅

## Phase 2 — Navigation (DotRecast navmesh + dtCrowd + road graph)

**Goal:** real navmesh/crowd providers behind existing contracts; `Auto` selection (v0.3 §14 step 2).

- [x] **STR-P2-T1** `StrideNavmeshBaker` [details](./TASK-DETAIL.md#str-p2-t1-stridenavmeshbaker) — BATCH-07 ✅ (scene extraction seamed = STR-D11)
- [x] **STR-P2-T2** `DotRecastNavmeshProvider` [details](./TASK-DETAIL.md#str-p2-t2-dotrecastnavmeshprovider) — BATCH-07 ✅ (real DotRecast)
- [x] **STR-P2-T3** `DotRecastDtCrowdProvider` [details](./TASK-DETAIL.md#str-p2-t3-dotrecastdtcrowdprovider) — BATCH-08 ✅ (real DtCrowd)
- [x] **STR-P2-T4** `CrowdAgentUpdateSystem` refactor (`CrowdMotorIntent`) [details](./TASK-DETAIL.md#str-p2-t4-crowdagentupdatesystem-refactor) — BATCH-08 ✅ (resolves STR-D12; see STR-D14)
- [x] **STR-P2-T5** Road-graph mode + `Auto` selection [details](./TASK-DETAIL.md#str-p2-t5-road-graph-mode-and-auto-selection) — BATCH-08 ✅

## Phase 3 — Perception via Stride raycasts

**Goal:** real LOS/occlusion + ballistics against scene geometry (v0.3 §14 step 3).

- [x] **STR-P3-T1** `StrideRaycastService` [details](./TASK-DETAIL.md#str-p3-t1-strideraycastservice) — BATCH-09 ✅ (concrete Simulation.Raycast GPU-deferred)
- [x] **STR-P3-T2** Perception LOS via Stride raycasts [details](./TASK-DETAIL.md#str-p3-t2-perception-los-via-stride-raycasts) — BATCH-09 ✅
- [x] **STR-P3-T3** Ballistics raycast seam [details](./TASK-DETAIL.md#str-p3-t3-ballistics-raycast-seam) — BATCH-09 ✅

## Phase 4 — Animation

**Goal:** real Stride animation backend; locomotion blend + traversal montages (v0.3 §14 step 4).

- [x] **STR-P4-T1** `StrideAnimationBackend` + `PerEntityBlendTreeBuilder` [details](./TASK-DETAIL.md#str-p4-t1-strideanimationbackend-and-perentityblendtreebuilder) — BATCH-13 ✅ (Stride playback GPU-deferred)
- [x] **STR-P4-T2** `CharacterAnimationDefDto` demo content [details](./TASK-DETAIL.md#str-p4-t2-characteranimationdefdto-demo-content) — BATCH-13 ✅
- [x] **STR-P4-T3** Locomotion bridge [details](./TASK-DETAIL.md#str-p4-t3-locomotion-bridge) — BATCH-14 ✅
- [x] **STR-P4-T4** Montage dispatch [details](./TASK-DETAIL.md#str-p4-t4-montage-dispatch) — BATCH-14 ✅ (real OffMeshTraversalStartedEvent)

## Phase 5 — Gizmos, editor dual-window, record/replay

**Goal:** 3D gizmos, raylib/ImGui editor as a second window on the host thread, shared selection,
replay via the togglable group (v0.3 §14 step 5).

- [x] **STR-P5-T1** `DebugPrimitiveRenderer3D` [details](./TASK-DETAIL.md#str-p5-t1-debugprimitiverenderer3d) — BATCH-15 ✅; **GPU draw sink (STR-D16) — BATCH-21 ✅ (`PooledEntityDebugDrawSink3D`, D8 upgraded, GPU pending user confirm)**
- [x] **STR-P5-T2** Raylib/ImGui editor second window [details](./TASK-DETAIL.md#str-p5-t2-raylib-imgui-editor-second-window) — BATCH-22 ✅ (Option A: interleaved host-thread pump; `StrideInspectorWindow` per-frame pump in `StrideHrotGame.Update()`; entity list + basic inspector; enabled via `STRIDE_EDITOR_WINDOW=1`; GPU verification pending user confirm)
- [x] **STR-P5-T3** Shared selection + `CenterOnEntityCommand` [details](./TASK-DETAIL.md#str-p5-t3-shared-selection-and-centeronentitycommand) — BATCH-23 ✅ (`EditorSelectionState`, inspector selection→highlight gizmo, `CenterOnEntityCommand`, C-key + "Center [C]" button; 24 new tests; GPU verification pending user confirm)
- [x] **STR-P5-T4** Record/replay togglable reverse-sync [details](./TASK-DETAIL.md#str-p5-t4-record-and-replay-togglable-reverse-sync) — BATCH-15 ✅ (resolves STR-D5)

**Dual-window inspector (BATCH-22, 2026-06-05):** Second raylib/ImGui OS window pumped on the same host thread as Stride (Option A — design §8.3: "Graphics contexts don't conflict"; Stride=DirectX, raylib=GLFW/OpenGL, independent APIs). `StrideInspectorWindow` opened in `BootEditorSubsystem`; per-frame `PumpFrame()` called from `StrideHrotGame.Update()` after `_testHarness.Update()`. Window shows entity list (left panel: TKB type/name + SimTransform position) and inspector (right panel: SimTransform position+rotation, SimVelocity, NavigationStatus, authority bit). Enabled via `STRIDE_EDITOR_WINDOW=1` env var (default disabled — CI/headless safe). 18 new headless tests (view model + config): all green. Existing D0/F1–F12 harness keys unchanged. V1 is read-only; write/selection-sync (STR-P5-T3) is follow-up. **GPU verification pending user confirm.**

**Shared selection + CenterOnEntityCommand (BATCH-23, 2026-06-05):** Completes STR-P5-T3 and thereby all of Phase 5. `EditorSelectionState` (plain shared object on the host thread): `Select`/`Clear`/`ClearIfDead`/`Version`/`RequestCenter`/`ConsumeCenter`. Inspector window clicking a row calls `Select`; a "Center [C]" button in the inspector header calls `RequestCenter`. `EditorStrideSubsystem.Tick` step 7 calls `ClearIfDead` + `EmitSelectionHighlight` (bright cyan ±1 m AABB, 12-edge line box, 1-frame lifetime, tracks moving entities). `CenterOnEntityCommand.Compute` (pure math): FDP position → FDP→Stride swizzle → camera offset `(0,+2,−3)` → look-at quaternion. `ExecuteCenterOnEntity` in `StrideHrotGame` applies position + rotation to `_cameraEntity.Transform` instantly; free-flight controls continue from new position. Trigger key: **C** (Stride window, `Input.IsKeyPressed`) + "Center [C]" button (inspector panel). 24 new headless tests (11 selection-state + 8 camera-math + 5 others): all green. Full suite: Core 327/327, Animation 48/48, Game 178/178 — all green. **GPU verification pending user confirm.** STR-D2 (`ScreenRayToFdp`) not exercised.

## Phase 6 — Mode 2 (networked Stride node)

**Goal:** slave-only Stride node over real DDS to a remote Brain+Master; deferred handover; egress
(v0.3 §13, §14 step 6).

- [ ] **STR-P6-T1** `StrideMuscleNodeBootstrapper` [details](./TASK-DETAIL.md#str-p6-t1-stridemusclenodebootstrapper)
- [ ] **STR-P6-T2** `StrideMuscleNodeApp` [details](./TASK-DETAIL.md#str-p6-t2-stridemusclenodeapp)
- [ ] **STR-P6-T3** Deferred authority handover [details](./TASK-DETAIL.md#str-p6-t3-deferred-authority-handover)
- [ ] **STR-P6-T4** Egress + dead-reckoning velocity invariant [details](./TASK-DETAIL.md#str-p6-t4-egress-and-dead-reckoning-velocity-invariant)
- [ ] **STR-P6-T5** Two-process end-to-end bring-up [details](./TASK-DETAIL.md#str-p6-t5-two-process-end-to-end-bring-up)
