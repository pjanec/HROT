# BD1 — Task Tracker

**Reference:** See [BD1-TASK-DETAIL.md](./BD1-TASK-DETAIL.md) for detailed task descriptions  
**Design:** See [BD1-DESIGN.md](./BD1-DESIGN.md) for architecture and rationale

---

## Phase 1 — Core Brain-Death Lifecycle

**Goal:** Ensure doctrine is explicitly cleared to `DoctrineIds.None` when a mission ends or is aborted, and that `ChannelArbitrationSystem` always triggers `OnExit` so the muscle layer is cleanly shut down. Two distinct events are introduced: `DoctrineFinishedEvent` (notification, bottom-up, from behavior machinery) and `ClearDoctrineEvent` (imperative, top-down, from mission/control tier).

- [ ] **BD1-P1T0a** DoctrineFinishedEvent — Bottom-Up Notification from LocomotionDispatcherSystem  [details](./BD1-TASK-DETAIL.md#bd1-p1t0a-doctrinefinishedevent--bottom-up-notification-from-locomotiondispatchersystem)
- [ ] **BD1-P1T0b** ClearDoctrineEvent — Top-Down Imperative via DoctrineIngressSystem  [details](./BD1-TASK-DETAIL.md#bd1-p1t0b-cleardoctrineevent--top-down-imperative-via-doctrineingresssystem)
- [ ] **BD1-P1T1** ChannelArbitrationSystem — OnExit Guarantee  [details](./BD1-TASK-DETAIL.md#bd1-p1t1-channelarbitrationsystem--onexit-guarantee)
- [ ] **BD1-P1T2** MissionDirectorSystem — DoctrineFinished Trigger + End-of-Mission Clear  [details](./BD1-TASK-DETAIL.md#bd1-p1t2-missiondirectorsystem--doctrinefinished-trigger--end-of-mission-clear)
- [ ] **BD1-P1T3** MissionControlRequestSystem — CMD_ABORT_ALL Doctrine Clear  [details](./BD1-TASK-DETAIL.md#bd1-p1t3-missioncontrolrequestsystem--cmd_abort_all-doctrine-clear)

---

## Phase 2 — Right-Click Mission UX

**Goal:** The SimHost right-click handler correctly routes commands to either the muscle layer (brain-dead entities) or the mission system (brain-active entities), with proper trigger configuration to enable the Phase 1 brain-death transition.

- [ ] **BD1-P2T1** SimHostVisualization — Brain-Aware Right-Click Handler  [details](./BD1-TASK-DETAIL.md#bd1-p2t1-simhostvisualization--brain-aware-right-click-handler)

---

## Phase 3 — RVO Spatial Hash / Physics Collider

**Goal:** Restore RVO vehicle-to-vehicle collision avoidance by ensuring all spawned vehicles carry the `PhysicsCollider` component required by `SpatialHashSystem`.

- [ ] **BD1-P3T1** BdcTkbBuilder — Add PhysicsCollider to WithPhysics  [details](./BD1-TASK-DETAIL.md#bd1-p3t1-bdctkbbuilder--add-physicscollider-to-withphysics)
- [ ] **BD1-P3T2** SimHostScenarioManager — Add PhysicsCollider to SpawnEntityLocal  [details](./BD1-TASK-DETAIL.md#bd1-p3t2-simhostscenariomanager--add-physicscollider-to-spawnentitylocal)

---

## Phase 4 — Camera Offset Fix

**Goal:** Fix the "Center on entity" map context menu teleporting the view to the top-left corner of the SimHost standalone window.

- [ ] **BD1-P4T1** SimHostVisualization — Set Camera Offset on Initialize  [details](./BD1-TASK-DETAIL.md#bd1-p4t1-simhostvisualization--set-camera-offset-on-initialize)

---

## Phase 5 — DisType DDS Struct

**Goal:** Replace the plain `long DisType` field on `EntityMaster` DDS topic with a named 8-field struct for readability in DDS monitoring tools.

- [ ] **BD1-P5T1** EntityMaster — Replace Plain long DisType with DisTypeStruct  [details](./BD1-TASK-DETAIL.md#bd1-p5t1-entitymaster--replace-plain-long-distype-with-distypestruct)

---

## Phase 6 — Entity Inspector Component Change Detection

**Goal:** Highlight mutated ECS components in the ImGui entity inspector by comparing per-frame byte snapshots.

- [ ] **BD1-P6T1** ComponentReflector — Byte-Cache Change Detection  [details](./BD1-TASK-DETAIL.md#bd1-p6t1-componentreflector--byte-cache-change-detection)

---

## Phase 7 — CreateEntityRequestSystem Hot-Path Delegate Caching

**Goal:** Eliminate a per-tick `Action<CreateEntityRequest>` delegate allocation on the SimHost ingress hot path.

- [ ] **BD1-P7T1** CreateEntityRequestSystem — Cache ProcessRequest Delegate  [details](./BD1-TASK-DETAIL.md#bd1-p7t1-createentityrequestsystem--cache-processrequest-delegate)
