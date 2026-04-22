# MOD1-BATCH-05: Component Registry Split & Combat Toolkit Extraction

**Batch Number:** MOD1-BATCH-05  
**Tasks:** DB-MOD1-11, CT-MOD1-I, MOD1-P5T1, MOD1-P6T1, MOD1-P6T2, MOD1-P6T3  
**Phase:** Phase 5 (Component ID Split) & Phase 6 (Distributed Perception)  
**Estimated Effort:** 10-12 hours  
**Priority:** CRITICAL  
**Dependencies:** MOD1-BATCH-04 

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to MOD1 Phase 5.

**🚨 CRITICAL INSTRUCTIONS REGARDING PREVIOUS BATCHES 🚨**

1. **Your DI fix for ActionDispatch was a band-aid.** Leaving `AimAndFireExecutor` and `JoinFormationExecutor` in the `Hrot` domain is unacceptable. If it handles generic combat or formation behavior, it belongs in FDP. **You are explicitly authorized to create new toolkit assemblies (e.g., `FDP.Toolkit.Combat`)** to physically house these executors generically.

You must solve this corrective item before proceeding with the formal Phase 5 (Component ID splint) and the beginning of Phase 6 (Perception ECS structs).

### Required Reading (IN ORDER)
1. **Developer workflow guide:** `.dev-workstream/README.md`
2. **Task Definitions:** `docs/modularizing/MOD1-TASK-DETAIL.md` - See Phase 5 and beginning of Phase 6.
3. **Previous Review:** `.dev-workstream/reviews/MOD1-BATCH-04-REVIEW.md` 

### Source Code Location
- **Primary Work Areas:**
  - `Hrot.ClusterRunner/`
  - `FDP.Toolkit.Combat/` (New Assembly to create)
  - `FDP.Kernel/`
  - `Hrot.Map.Definitions/`
  - `FDP.Toolkit.Perception/`
- **Test Projects:**
  - `Hrot.SimHost.Integration.Tests/` (Critical for Right-Click UI testing validation)

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/MOD1-BATCH-05-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Task DB-MOD1-11:** Wire UI TogglePerspectiveEvent → **ALL tests pass** ✅
2. **Task CT-MOD1-I:** Extract `FDP.Toolkit.Combat` Toolkit → **ALL tests pass** ✅
3. **Task 1 (P5T1):** Implement Component ID Split → **ALL tests pass** ✅
4. **Task 2 (P6T1):** Implement SensorModality → **ALL tests pass** ✅
5. **Task 3 (P6T2):** Implement DDS Descriptors → **ALL tests pass** ✅
6. **Task 4 (P6T3):** Implement PathfindingBatchData → **ALL tests pass** ✅

---

## ✅ Tasks



### Corrective Task DB-MOD1-11: Wire TogglePerspectiveEvent in UI

**Files:** `Hrot.SimHost` UI layer / Output panels.

**Description:**
The `PerspectiveCoordinatorSystem` and `ActivePerspective` were created in BATCH-04 but lack physical UI integration.
- Map an ImGui button or viewport interaction in the `SimHostVisualization.DrawUI` routine to actively fire the `TogglePerspectiveEvent`.
- You MUST use `world.Bus.Publish(evt)` followed immediately by `world.Bus.SwapBuffers()` so that the engine successfully reads the toggle event. 
- You MUST ensure the label visually flips to correctly mirror the underlying perspective shift.

---

### Corrective Task CT-MOD1-I: Extract `FDP.Toolkit.Combat`

**Files:** `AimAndFireExecutor.cs`, `JoinFormationExecutor.cs`, Solution files.

**Description:**
Leaving generic kinetic behaviors in `Hrot.SimHost` negates the modularization effort. 
- Create a new assembly: `FDP.Toolkit.Combat`.
- Move `AimAndFireExecutor` and all associated core weapon dispatch logic into this new assembly.
- Relocate `JoinFormationExecutor` into `FDP.Toolkit.Behavior` or a suitable generic toolkit.
- Adjust project references. `Hrot.SimHost` should consume these toolkits, not house their guts.

---

### Task 1: MOD1-P5T1

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P5T1](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p5t1--create-hrotcomponentids-in-hrotmapdefinitions)

**Description:** Create `HrotComponentIds` in `Hrot.Map.Definitions` and strip all Hrot application constants out of `GlobalComponentIds`.

**Tests Required:**
- ✅ Ensure zero `Hrot.*` structs rely on `GlobalComponentIds` constants internally.

---

### Task 2: MOD1-P6T1

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P6T1](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p6t1--fix-perception-component-ids--add-sensormodality-bitmask-to-targetmemory--per-modality-receptor-components)

**Description:** Fix `Faction` / `PerceptionReceptor` ID ranges so they reside in the FDP Toolkit block natively. Implement `SensorModality` memory updates.

**Tests Required:**
- ✅ `TargetMemory_ModalityFusion_OrsModalities` and eviction resets.

---

### Task 3: MOD1-P6T2

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P6T2](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p6t2--add-dds-descriptors-for-perception--pathfinding)

**Description:** Define all network translation interfaces for Raycasts and Sensors.

**Tests Required:**
- ✅ Validate SSTD definitions compile flawlessly into the DDS infrastructure.

---

### Task 4: MOD1-P6T3

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P6T3](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p6t3--add-pathfindingbatchdata-ecs-singleton)

**Description:** Establish the zero-allocation `NativeArray`-backed singleton for pathfinding requests/results.

**Tests Required:**
- ✅ Verify default capacity mapping on allocation.

---

## 📊 Report Requirements

Please submit `.dev-workstream/reports/MOD1-BATCH-05-REPORT.md` completing the following:

**Developer Insights**

**Q1:** For CT-MOD1-I, did creating `FDP.Toolkit.Combat` expose any other tightly coupled Hrot classes? How did you resolve the transitive dependencies?

**Q2:** Did any component ID collisions occur after splitting `GlobalComponentIds` in P5T1? 

**Q3:** Are there any performance concerns with bitmask evaluations inside `TargetMemory` introduced during P6T1?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Perspective UI Toggle works physically from the Runner frontend.
- [ ] `AimAndFireExecutor` and `JoinFormationExecutor` live in fully generalized FDP toolkits (`FDP.Toolkit.Combat`, etc.), completely removing them from Hrot domains.
- [ ] `HrotComponentIds` encapsulates all Hrot-specific IDs, leaving `GlobalComponentIds` strictly for engine primitives.
- [ ] Perception modality pipelines successfully compile.
