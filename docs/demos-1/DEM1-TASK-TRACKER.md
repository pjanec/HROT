# DEM1 — Task Tracker

**Reference:** See [DEM1-TASK-DETAIL.md](./DEM1-TASK-DETAIL.md) for detailed task descriptions  
**Design:** See [DEM1-DESIGN.md](./DEM1-DESIGN.md) for architecture and phase overview  
**Onboarding:** See [DEM1-ONBOARDING.md](./DEM1-ONBOARDING.md) for newcomer guide

---

## Phase 0 — Demo Framework Foundation

**Goal:** Extend the generic `FDP.Framework.Runner` with deterministic time-stepping and create the `IScenario` / `ScenarioSubsystem` infrastructure inside `Fdp.Examples.Common`.

- [x] **DEM1-F001** Deterministic Mode in RunnerOptions / RunnerConfiguration — [details](./DEM1-TASK-DETAIL.md#dem1-f001--deterministic-mode-in-runneroptions-and-runnerconfiguration)
- [x] **DEM1-F002** IScenario Interface and ScenarioSubsystem — [details](./DEM1-TASK-DETAIL.md#dem1-f002--iscenario-interface-and-scenariosubsystem)
- [x] **DEM1-F003** ScenarioRegistry, CLI Program.cs, and Runner Project — [details](./DEM1-TASK-DETAIL.md#dem1-f003--scenarioregistry-cli-programcs-and-runner-project)
- [x] **DEM1-F004** NLog Trace Logging Setup — [details](./DEM1-TASK-DETAIL.md#dem1-f004--nlog-trace-logging-setup)
- [x] **DEM1-F005** ScenarioNames Constants and Base Test Infrastructure — [details](./DEM1-TASK-DETAIL.md#dem1-f005--scenarionames-constants-and-base-test-infrastructure)

---

## Phase 1 — Shared Infrastructure

**Goal:** Create the `Fdp.Examples.DDS` schema project and complete `Fdp.Examples.Common` with shared components, events, and helpers.

- [x] **DEM1-I001** Fdp.Examples.DDS Project (Cartesian DDS schemas) — [details](./DEM1-TASK-DETAIL.md#dem1-i001--fdpexamplesdds-project)
- [x] **DEM1-I002** Fdp.Examples.Common Infrastructure (components, events, helpers, constants) — [details](./DEM1-TASK-DETAIL.md#dem1-i002--fdpexamplescommon-infrastructure)

---

## Phase 2 — Simple Demos

**Goal:** Implement the two simplest single-toolkit scenarios as the first working CI tests.

- [x] **DEM1-D001** AutoDrive (Kinematics & RVO Avoidance) — [details](./DEM1-TASK-DETAIL.md#dem1-d001--autodrive-scenario)
- [x] **DEM1-D002** ComponentDamage (Partial Kill Pipeline) — [details](./DEM1-TASK-DETAIL.md#dem1-d002--componentdamage-scenario)

---

## Phase 3 — Mid-Complexity Demos

**Goal:** Prove Physics/CCD, Cognitive/BTree, and Perception/LOS toolkits in isolation.

- [x] **DEM1-D003** BallisticsAndHit (CCD Anti-Tunneling) — [details](./DEM1-TASK-DETAIL.md#dem1-d003--ballisticsandhit-scenario)
- [x] **DEM1-D004** BehaviorValidation (Cognitive Pipeline) — [details](./DEM1-TASK-DETAIL.md#dem1-d004--behaviorvalidation-scenario)
- [x] **DEM1-D005** SensorGrid (Perception & LOS Occlusion) — [details](./DEM1-TASK-DETAIL.md#dem1-d005--sensorgrid-scenario)

---

## Phase 4 — Advanced Demos

**Goal:** Prove multi-system interactions: mission arbitration, terrain Z-clamping, and AAR replay.

- [ ] **DEM1-D006** MissionCommand (Dynamic Mission + Preemption) — [details](./DEM1-TASK-DETAIL.md#dem1-d006--missioncommand-scenario)
- [ ] **DEM1-D007** TerrainClamping (Z-Height Smoothing & Jump Rejection) — [details](./DEM1-TASK-DETAIL.md#dem1-d007--terrainclamping-scenario)
- [ ] **DEM1-D008** ParallelStories (AAR Recording & Deterministic Replay) — [details](./DEM1-TASK-DETAIL.md#dem1-d008--parallelstories-scenario)

---

## Phase 5 — Network Demo

**Goal:** Prove split-authority DDS replication with ELM handshake and hierarchical ghosting.

- [ ] **DEM1-D009** DistributedTank (Component-Level Network Authority) — [details](./DEM1-TASK-DETAIL.md#dem1-d009--distributedtank-scenario)

---

## Phase 6 — Grand Integration Demo

**Goal:** The ultimate regression test — all toolkits working together deterministically.

- [ ] **DEM1-D010** UrbanCombat New (All Toolkits Grand Integration) — [details](./DEM1-TASK-DETAIL.md#dem1-d010--urbancombat-new-scenario)
