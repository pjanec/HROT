# SIM-BATCH-07: Technical Debt Resolution

**Batch Number:** SIM-BATCH-07  
**Tasks:** SIM-DEBT-[03, 05, 02, 04, 06, 07]  
**Phase:** Maintenance / Tech Debt  
**Estimated Effort:** 16 hours (2 days)  
**Priority:** HIGH  
**Dependencies:** Phase S6 Completed

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome back! We are skipping Phase S7 (Documentation) entirely. Instead, we are using this batch to pay down the technical debt we accrued during the rapid development of the SimHost executable.

The debt items have been sorted by highest priority. 

### Required Reading
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Debt Tracker:** `.dev-workstream/SIM-DEBT-TRACKER.md`

### Source Code Location
- **Primary Work Area:** `Hrot.SimHost/`, `Hrot.SimHost.Integration.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/SIM-BATCH-07-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/SIM-BATCH-07-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Task Progression

You must execute the following debt tasks in sequential descending order of severity. Ensure all tests continue to pass after each resolution!

---

## ✅ Tasks

### Task 1: Resolve Late-Join Race Condition (SIM-DEBT-03 | P2)
**Area:** `EntityMissionTranslator`
**Description:** `EntityMissionTranslator` drops `EntityMission` data silently if it arrives before `EntityMaster` has been registered in the `NetworkEntityMap`.
**Acceptance Criteria:** 
- Rework the ingestion logic to queue or reliably block/retry payloads if the target entity network ID is not yet known.
- Add/update tests in `EntityMissionTranslatorTests.cs`.

### Task 2: VehicleAPI Formation Overload (SIM-DEBT-05 | P2)
**Area:** `VehicleAPI`, `VehicleCommandSystem`
**Description:** `VehicleAPI.JoinFormation` does not accept the `FormationType` layout parameter decoded upstream.
**Acceptance Criteria:**
- Overload `VehicleAPI.JoinFormation` to take a `FormationType` enum.
- Route the enum safely through `VehicleCommandSystem` commands natively so physics can consume it.

### Task 3: Over-Scan Table Evaluation Flag Optimization (SIM-DEBT-02 | P3)
**Area:** `EntityMissionEgressTranslator`
**Description:** Table-level dirty flag tracking causes minor over-scan evaluation. When entity A changes but entity B in the same memory chunk does not, both trigger a component read eval.
**Acceptance Criteria:**
- Refactor to utilize an entity-level mask or versioning technique in ECS if supported, or cleanly implement a hash-based comparative state cache proxy to instantly break unmutated evaluates at line 1.

### Task 4: Idle Behavior Fallback (SIM-DEBT-04 | P3)
**Area:** `MissionAdapterSystem`
**Description:** Unregistered BehaviorId strings cause persistent warning outputs every frame. 
**Acceptance Criteria:**
- Implement fallback behavior targeting `SimHostBehaviorIds.Idle_HSM = 3010`.
- Throw a single warning per entity transitioning into this state safely to prevent CLI log flooding.
- Assert tests correctly map unknown ids to `Idle_HSM`.

### Task 5: Refactor Setup Boot Sequence (SIM-DEBT-06 | P4)
**Area:** `Program.cs`, `SimulationLogicModule`
**Description:** The instantiation of modules between the generic ECS/DDS components in `Program.cs` needs decoupling. 
**Acceptance Criteria:**
- Create a `SimulationLogicModule.Build(IKernelServices)` factory capability.
- Move logic node instantiation out of `Program.cs` into the module directly to maintain bounds.

### Task 6: Extract Integration Test Mocks (SIM-DEBT-07 | P4)
**Area:** `Hrot.SimHost.Integration.Tests`
**Description:** Extract the integration mocks to be shared for future toolkit node projects.
**Acceptance Criteria:**
- Create a new project `Hrot.DDS.TestMocks` or similar internal library.
- Move `SimHostInstance` and `MockIOSClient` implementations.
- Update Integration Tests to reference this new library to consume.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] DEBT 02 - 07 are all implemented.
- [ ] Trackers reflect the resolved state.
- [ ] No performance or latency regressions exist in `Integration.Tests`.
- [ ] Report submitted via markdown file.
