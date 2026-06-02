# Stride Integration — Task Tracker

**Reference:** see [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions and success
conditions, and [Stride-Integration_v0_3.md](./Stride-Integration_v0_3.md) for the design.
Technical debt: [DEBT-TRACKER.md](./DEBT-TRACKER.md).

Status: `[ ]` not done · `[x]` done. Build Mode 1 (P0–P5) first, then Mode 2 (P6).

---

## Phase 0 — Scaffolding, coordinate seam, first render

**Goal:** `editor_stride` boots under `OfflineNetworkFactory`, Stride drives its own loop,
entities spawn (owned instantly) and render — movement stubbed (v0.3 §14 step 0).

- [ ] **STR-P0-T1** Create `Hrot.Stride.Core` project + references [details](./TASK-DETAIL.md#str-p0-t1-create-hrotstridecore-project-and-references)
- [ ] **STR-P0-T2** Create `Hrot.Stride.Animation` project + references [details](./TASK-DETAIL.md#str-p0-t2-create-hrotstrideanimation-project-and-references)
- [ ] **STR-P0-T3** Wire `HrotStrideApp.Game` references [details](./TASK-DETAIL.md#str-p0-t3-wire-hrotstrideappgame-references)
- [ ] **STR-P0-T4** `FdpStrideTransform` coordinate seam [details](./TASK-DETAIL.md#str-p0-t4-fdpstridetransform-coordinate-seam)
- [ ] **STR-P0-T5** `StrideHrotGame` external host loop [details](./TASK-DETAIL.md#str-p0-t5-stridehrotgame-external-host-loop)
- [ ] **STR-P0-T6** `EditorStrideSubsystem` composition skeleton [details](./TASK-DETAIL.md#str-p0-t6-editorstridesubsystem-composition-skeleton)
- [ ] **STR-P0-T7** `StrideVisualBindingSystem` + procedural fallback [details](./TASK-DETAIL.md#str-p0-t7-stridevisualbindingsystem-and-procedural-fallback)
- [ ] **STR-P0-T8** End-to-end spawn + render smoke [details](./TASK-DETAIL.md#str-p0-t8-end-to-end-spawn-and-render-smoke)

## Phase 1 — Bullet movement + reverse-sync

**Goal:** Bullet authoritative; reverse-sync writes `SimTransform`/`SimVelocity`; FDP integrators
off (v0.3 §14 step 1).

- [ ] **STR-P1-T1** `StrideKinematicsModule` [details](./TASK-DETAIL.md#str-p1-t1-stridekinematicsmodule)
- [ ] **STR-P1-T2** `PhysicsBodyLifecycleSystem` + `PhysicsBodyReference` [details](./TASK-DETAIL.md#str-p1-t2-physicsbodylifecyclesystem-and-physicsbodyreference)
- [ ] **STR-P1-T3** `BulletCharacterMotor` [details](./TASK-DETAIL.md#str-p1-t3-bulletcharactermotor)
- [ ] **STR-P1-T4** `KinematicVehicleMotor` (collision response + velocity) [details](./TASK-DETAIL.md#str-p1-t4-kinematicvehiclemotor)
- [ ] **STR-P1-T5** `BulletReverseSyncSystem` (togglable group) [details](./TASK-DETAIL.md#str-p1-t5-bulletreversesyncsystem)
- [ ] **STR-P1-T6** `SplitAuthorityStrideSyncScript` [details](./TASK-DETAIL.md#str-p1-t6-splitauthoritystridesyncscript)
- [ ] **STR-P1-T7** Fixed timestep + reverse-sync ordering [details](./TASK-DETAIL.md#str-p1-t7-fixed-timestep-and-reverse-sync-ordering)

## Phase 2 — Navigation (DotRecast navmesh + dtCrowd + road graph)

**Goal:** real navmesh/crowd providers behind existing contracts; `Auto` selection (v0.3 §14 step 2).

- [ ] **STR-P2-T1** `StrideNavmeshBaker` [details](./TASK-DETAIL.md#str-p2-t1-stridenavmeshbaker)
- [ ] **STR-P2-T2** `DotRecastNavmeshProvider` [details](./TASK-DETAIL.md#str-p2-t2-dotrecastnavmeshprovider)
- [ ] **STR-P2-T3** `DotRecastDtCrowdProvider` [details](./TASK-DETAIL.md#str-p2-t3-dotrecastdtcrowdprovider)
- [ ] **STR-P2-T4** `CrowdAgentUpdateSystem` refactor (`CrowdMotorIntent`) [details](./TASK-DETAIL.md#str-p2-t4-crowdagentupdatesystem-refactor)
- [ ] **STR-P2-T5** Road-graph mode + `Auto` selection [details](./TASK-DETAIL.md#str-p2-t5-road-graph-mode-and-auto-selection)

## Phase 3 — Perception via Stride raycasts

**Goal:** real LOS/occlusion + ballistics against scene geometry (v0.3 §14 step 3).

- [ ] **STR-P3-T1** `StrideRaycastService` [details](./TASK-DETAIL.md#str-p3-t1-strideraycastservice)
- [ ] **STR-P3-T2** Perception LOS via Stride raycasts [details](./TASK-DETAIL.md#str-p3-t2-perception-los-via-stride-raycasts)
- [ ] **STR-P3-T3** Ballistics raycast seam [details](./TASK-DETAIL.md#str-p3-t3-ballistics-raycast-seam)

## Phase 4 — Animation

**Goal:** real Stride animation backend; locomotion blend + traversal montages (v0.3 §14 step 4).

- [ ] **STR-P4-T1** `StrideAnimationBackend` + `PerEntityBlendTreeBuilder` [details](./TASK-DETAIL.md#str-p4-t1-strideanimationbackend-and-perentityblendtreebuilder)
- [ ] **STR-P4-T2** `CharacterAnimationDefDto` demo content [details](./TASK-DETAIL.md#str-p4-t2-characteranimationdefdto-demo-content)
- [ ] **STR-P4-T3** Locomotion bridge [details](./TASK-DETAIL.md#str-p4-t3-locomotion-bridge)
- [ ] **STR-P4-T4** Montage dispatch [details](./TASK-DETAIL.md#str-p4-t4-montage-dispatch)

## Phase 5 — Gizmos, editor dual-window, record/replay

**Goal:** 3D gizmos, raylib/ImGui editor as a second window on the host thread, shared selection,
replay via the togglable group (v0.3 §14 step 5).

- [ ] **STR-P5-T1** `DebugPrimitiveRenderer3D` [details](./TASK-DETAIL.md#str-p5-t1-debugprimitiverenderer3d)
- [ ] **STR-P5-T2** Raylib/ImGui editor second window [details](./TASK-DETAIL.md#str-p5-t2-raylib-imgui-editor-second-window)
- [ ] **STR-P5-T3** Shared selection + `CenterOnEntityCommand` [details](./TASK-DETAIL.md#str-p5-t3-shared-selection-and-centeronentitycommand)
- [ ] **STR-P5-T4** Record/replay togglable reverse-sync [details](./TASK-DETAIL.md#str-p5-t4-record-and-replay-togglable-reverse-sync)

## Phase 6 — Mode 2 (networked Stride node)

**Goal:** slave-only Stride node over real DDS to a remote Brain+Master; deferred handover; egress
(v0.3 §13, §14 step 6).

- [ ] **STR-P6-T1** `StrideMuscleNodeBootstrapper` [details](./TASK-DETAIL.md#str-p6-t1-stridemusclenodebootstrapper)
- [ ] **STR-P6-T2** `StrideMuscleNodeApp` [details](./TASK-DETAIL.md#str-p6-t2-stridemusclenodeapp)
- [ ] **STR-P6-T3** Deferred authority handover [details](./TASK-DETAIL.md#str-p6-t3-deferred-authority-handover)
- [ ] **STR-P6-T4** Egress + dead-reckoning velocity invariant [details](./TASK-DETAIL.md#str-p6-t4-egress-and-dead-reckoning-velocity-invariant)
- [ ] **STR-P6-T5** Two-process end-to-end bring-up [details](./TASK-DETAIL.md#str-p6-t5-two-process-end-to-end-bring-up)
