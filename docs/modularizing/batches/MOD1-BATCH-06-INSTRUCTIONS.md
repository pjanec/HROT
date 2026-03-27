# MOD1-BATCH-06: Distributed Module Wiring & Pathfinding Stubs Cleanup

**Batch Number:** MOD1-BATCH-06  
**Tasks:** CT-MOD1-J, MOD1-P6T4, MOD1-P6T5, MOD1-P6T6, MOD1-P6T7, MOD1-P6T8  
**Phase:** Phase 6 (Distributed Perception & Pathfinding)  
**Estimated Effort:** 10-12 hours  
**Priority:** CRITICAL  
**Dependencies:** MOD1-BATCH-05 

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to BATCH-06.

**🚨 CRITICAL INSTRUCTION REGARDING TEST FAILURES 🚨**

In your last report, you noted that after fixing the build path for `Bagira.IG.Tests`, you found 4 failing tests (`EditToolTests` & `AdvancedFeaturesIntegrationTests.Phase4`). You marked them as "pre-existing" and ignored them. 
**This is unacceptable.** During a massive architecture refactor, tests suddenly failing (or being exposed as failing) often indicates subtle decoupling damage, race conditions, or incorrect topological system ordering. You must trace these 4 tests and fix the application logic so they pass. **A green pipeline means NO failing tests, period.**

You must solve this corrective item before proceeding with the remaining Phase 6 tasks. Phase 6 focuses on removing the `BTreeContext` raycast/pathfinding stubs in favor of generic Physics/Navigation node definitions, and wiring the perception modules.

### Required Reading (IN ORDER)
1. **Developer workflow guide:** `.dev-workstream/README.md`
2. **Task Definitions:** `docs/modularizing/MOD1-TASK-DETAIL.md` - See Phase 6 remaining tasks.
3. **Previous Review:** `.dev-workstream/reviews/MOD1-BATCH-05-REVIEW.md` 

### Source Code Location
- **Primary Work Areas:**
  - `Bagira.IG.Tests/` (Critical test coverage)
  - `FDP.Toolkit.Behavior/`
  - `FDP.Toolkit.Physics/`
  - `FDP.Toolkit.Navigation/`
  - `FDP.Toolkit.Perception/`
  - `Bagira.SimHost/Network/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/MOD1-BATCH-06-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Task CT-MOD1-J:** Fix 4 failing IG tests → **ALL 296 tests pass** ✅
2. **Task 1 (P6T4):** Delete `BTreeContext` raycast stubs + create `PhysicsQueryActionNode` → **ALL tests pass** ✅
3. **Task 2 (P6T5):** Delete `RequestPath` stubs + create `PathfindingActionNode` → **ALL tests pass** ✅
4. **Task 3 (P6T6):** Create `AutonomousPerceptionModule` and `PhysicsQueryModule` → **ALL tests pass** ✅
5. **Task 4 (P6T7):** Create `NavigationSolverModule` → **ALL tests pass** ✅
6. **Task 5 (P6T8):** Create Translator Packs for Perception & Pathfinding → **ALL tests pass** ✅

---

## ✅ Tasks

### Corrective Task CT-MOD1-J: Fix `Bagira.IG.Tests`

**Files:** `Bagira.IG.Tests` test suite and corresponding source logic.

**Description:**
4 tests are failing in `Bagira.IG.Tests` (`EditToolTests.HandleDrag_*` and `AdvancedFeaturesIntegrationTests.Phase4_AllSubsystems_WorkTogetherInSharedRepo`).
- Investigate the state mismatch or exception causing these tests to fail.
- It is highly likely they are affected by the component ID shuffles, authority checks, or module extraction from earlier batches.
- Fix the logic or the test constraints natively. Do not merely bypass the assertions. 

---

### Task 1: MOD1-P6T4

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P6T4](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p6t4--delete-requestraycastgetraycastresult-from-btreecontext-and-create-physicsqueryactionnode)

**Description:** Delete `RequestRaycast`/`GetRaycastResult` from `BTreeContext` and `IAIContext`. Create `PhysicsQueryActionNode` to ensure `FDP.Toolkit.Behavior` does not couple to `FDP.Toolkit.Physics`.

---

### Task 2: MOD1-P6T5

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P6T5](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p6t5--delete-requestpathgetpathresult-from-btreecontext-and-create-pathfindingactionnode)

**Description:** Delete `RequestPath`/`GetPathResult` from `BTreeContext`. Create `PathfindingActionNode` in `FDP.Toolkit.Navigation` to prevent a circular reference back to Behavior.

---

### Task 3: MOD1-P6T6

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P6T6](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p6t6--create-autonomousperceptionmodule-and-physicsquerymodule)

**Description:** Wrap the existing perception and raycast systems into `AutonomousPerceptionModule` and `PhysicsQueryModule`. Stop registering these directly within `SimulationLogicModule`.

---

### Task 4: MOD1-P6T7

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P6T7](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p6t7--create-navigationsolvermodule)

**Description:** Extract on-demand path computation (`PathfindingSolverSystem`) into `NavigationSolverModule` for standalone solver Node roles.

---

### Task 5: MOD1-P6T8

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P6T8](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p6t8--create-perception--pathfinding-translator-packs)

**Description:** Build out the static Dds translator packs: `BrainPerceptionTranslatorPack`, `SimPerceptionTranslatorPack`, `BrainPathfindingTranslatorPack`, and `SimPathfindingTranslatorPack`. Integrate with `NodeBootstrapper`.

---

## 📊 Report Requirements

Please submit `.dev-workstream/reports/MOD1-BATCH-06-REPORT.md` completing the following:

**Developer Insights**

**Q1:** For CT-MOD1-J, what exactly was causing the 4 IG tests to fail natively? Was it related to the component ID remapping or something else?

**Q2:** When deleting the stubs from `BTreeContext` (P6T4/P6T5), did you have to aggressively rewrite many existing mock tests that relied on the interface definitions? 

**Q3:** During the creation of `AutonomousPerceptionModule` and `NavigationSolverModule`, did any topological ordering issues surface in the `SystemPhase.Simulation` group?

**Q4:** Are all four translation packs thoroughly compiling and integrating securely into `NodeBootstrapper`?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] ALL 300 tests in `Bagira.IG.Tests` pass unconditionally.
- [ ] `BTreeContext` and `IAIContext` have completely dropped the Raycast and Pathfinding stubs, breaking the topological coupling into independent node bases.
- [ ] Perception and Physics wrappers are fully extracted into `IModule` definitions.
- [ ] Network translator packs for nodes properly route DDS messages for perception/pathfinding.
