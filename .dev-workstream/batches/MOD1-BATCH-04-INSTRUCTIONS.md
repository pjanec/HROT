# MOD1-BATCH-04: Core Functionality Restoration & Presentation Module Split

**Batch Number:** MOD1-BATCH-04  
**Tasks:** CT-MOD1-D, CT-MOD1-E, CT-MOD1-F, MOD1-P4T1, MOD1-P4T2  
**Phase:** Phase 4 — Presentation Module Split + Dynamic Perspective Switching  
**Estimated Effort:** 10-12 hours  
**Priority:** CRITICAL  
**Dependencies:** MOD1-BATCH-03 (Needs Fixes Resolved)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to MOD1 Phase 4.

**🚨 STOP RIGHT HERE: CRITICAL FUNCTIONALITY & ARCHITECTURE REPAIRS 🚨**
The application is still broken. In Batch 03, the exception during "Spawn moving entity" was removed, but the entity **does not move** when issued a `MoveToLocation` command via the UI. You must debug the `Bagira.Runner -x all` composition pipeline and restore true navigation capability to the application immediately. The application is a simulation—vehicles must move. 

Furthermore, you are directed to urgently fix the architectural compromises made in Batch 02: `ActionDispatchModule` and `LinearKinematicsSystem` were left in the `Bagira.SimHost` domain due to circular dependencies. **This violates the core principle of our modularisation.** You must generalize these toolkits, breaking cycles through new assembly definitions (`FDP.Toolkit.Combat`, `FDP.Toolkit.Kinematics.Core`, etc.) or Dependency Inversion.

After solving these three corrective items natively, you can advance to Phase 4: creating formal `IModule` definitions for our Presentations (`IgPresentationModule`, `SimPresentationModule`) and implementing the `ActivePerspective` singleton for dynamic viewpoint swapping.

### Required Reading (IN ORDER)
1. **Developer workflow guide:** `.dev-workstream/README.md`
2. **Task Definitions:** `docs/modularizing/MOD1-TASK-DETAIL.md` - See Phase 4 tasks.
3. **Previous Review:** `.dev-workstream/reviews/MOD1-BATCH-03-REVIEW.md` - Context for the necessary corrective actions.

### Source Code Location
- **Primary Work Areas:**
  - `FDP/Toolkits/`
  - `Bagira.SimHost/`
- **Test Projects:**
  - `Bagira.SimHost.Integration.Tests/` (Must be significantly bolstered)

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/MOD1-BATCH-04-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/MOD1-BATCH-04-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task CT-MOD1-D:** Fix vehicle movement → Write strong Integration Tests asserting coordinates → **ALL tests AND runner works** ✅
2. **Task CT-MOD1-E:** Resolve `ActionDispatchModule` circle → Recompile & Test → **ALL tests pass** ✅
3. **Task CT-MOD1-F:** Resolve `LinearKinematicsSystem` circle → Recompile & Test → **ALL tests pass** ✅
4. **Task 1 (P4T1):** Implement → Write tests → **ALL tests pass** ✅
5. **Task 2 (P4T2):** Implement → Write tests → **ALL tests pass** ✅

---

## ✅ Tasks

### Corrective Task CT-MOD1-D: Restore Vehicle Movement

**Files:** Runner composition, Integration test suites, Navigation systems.

**Description:**
Commanding a spawned vehicle to `MoveToLocation` does nothing. 
- You must identify why execution fails natively when running via `Bagira.Runner` config. 
- Ensure `NavigationExecutionSystem`, `MoveToExecutor`, `NavigationIntentBridgeSystem`, and `CarKinematicsSystem` are properly interacting on the correct entity footprints, and all relevant systems are actually registered under `-x all`.
- **Testing Requirement:** You MUST create an integration test that creates a vehicle, issues a move intent, advances the simulation frames, and explicitly **`Assert.NotEqual()` against the original position coordinates.** Passing unit tests without system momentum verification is insufficient.

---

### Corrective Task CT-MOD1-E: Generalise `ActionDispatchModule`

**Files:** `ActionDispatchModule.cs`, Locomotion dispatchers, executors.

**Description:**
Move `ActionDispatchModule` into `FDP.Toolkit.Behavior`. 
- Extract weapon routines to `FDP.Toolkit.Combat` if necessary. 
- For the `JoinFormationExecutor` circular dependency (which relies on `Bagira.SimHost.Systems`), abstract it using Dependency Inversion. Create an interface (e.g. `IFormationExecutor`) inside FDP, and pass the specific implementation at composition-root level. 
- Make the underlying engine strictly generic.

---

### Corrective Task CT-MOD1-F: Resolve Kinematics Circular Dependency

**Files:** `FDP.Toolkit.Physics`, `FDP.Toolkit.CarKinem`, `LinearKinematicsSystem.cs`

**Description:**
Break the dependency cycle between Physics and CarKinematics so that `LinearKinematicsSystem` can be safely hosted within `GroundKinematicsModule` or its own generic `PhysicsModule`. 
- If they require shared ECS components like `SimTransform`, extract those structures to `FDP.Toolkit.Kinematics.Core` or `FDP.Kernel`. 
- The solution must compile without leaving core kinematic systems stranded in `Bagira.SimHost`.

---

### Task 1: MOD1-P4T1

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P4T1](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p4t1--create-igpresentationmodule-and-simpresentationmodule)

**Description:** Wrap IG and SimHost map presentations in formal `IModule` definitions.

**Tests Required:**
- ✅ Verify both modules cleanly build out their required systems into independent phases (`PresentationPhase`).

---

### Task 2: MOD1-P4T2

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P4T2](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p4t2--activeperspective-singleton--perspectivecoordinatorsystem)

**Description:** Establish `ActivePerspective` singleton component and the `PerspectiveCoordinatorSystem` to handle dynamic viewpoint swapping between the Sim Map and the IG window.

**Tests Required:**
- ✅ Verify toggling `ActivePerspective` smoothly halts updates for the occluded presentation tier.

---

## 📊 Report Requirements

Please submit `.dev-workstream/reports/MOD1-BATCH-04-REPORT.md` completing the following:

**Developer Insights**

**Q1:** Specifically for CT-MOD1-D, what was structurally blocking the vehicle from evaluating the Move command? Provide exact paths.

**Q2:** How exactly did you untangle the circular dependency for Action Dispatch (CT-MOD1-E)?

**Q3:** How exactly did you untangle the circular dependency for Linear Kinematics (CT-MOD1-F)?

**Q4:** What issues did you encounter implementing the Phase 4 presentation modules?

**Q5:** Did you observe any side-effects in integration tests stemming from the ActionDispatch relocation?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Vehicles accurately traverse geographical footprints when subjected to `MoveToLocation` intents through `Bagira.Runner`.
- [ ] A dedicated integration test proves positional updates over elapsed ticks.
- [ ] `ActionDispatchModule` and `LinearKinematicsSystem` have been completely stripped from the `Bagira.SimHost` aggregation space and live in generalized toolkits natively.
- [ ] Phase 4 modules (IG and Sim Map presentation wrappers) compile effectively.
