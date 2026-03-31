# IOS-BATCH-05: IOS Advanced Features & Diagnostics

**Batch Number:** IOS-BATCH-05  
**Tasks:** IOS.10.1, IOS.10.2, IOS.10.3, IOS.10.4  
**Phase:** IOS-P10 (Advanced Features)  
**Estimated Effort:** ~20 hours  
**Priority:** MEDIUM  
**Dependencies:** IOS-BATCH-04, Shared Components Base

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome back! BATCH-04 finalized the core integration testing and proved the overall system flow between the IOS Mock, SimHost, and IG. This batch (BATCH-05) will cap off the IOS Mock component development by implementing Advanced Features mapped under Phase P10. This includes inspector panels, diagnostics, conflict resolution dialogs, and simulated multi-IOS concurrency testing.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Tracker:** `docs/design/TASK-TRACKER.md` - Context for the overall goal.
3. **Task Details:** Note that `TASK-DETAILS-IOS.md` does not rigidly define the UI code blocks for Phase P10. You will have creative liberty to fulfill the Acceptance Criteria using the established `rlImGui` panel structures.
4. **Code Standards:** `.dev-workstream/guides/CODE-STANDARDS.md`
5. **Debt Tracker:** `.dev-workstream/IOS-DEBT-TRACKER.md` - If you touch `IDerEntity.GetDescriptor<T>`, please attempt to address IOS-DEBT-029.

### Source Code Location
- **Primary Work Area:** `Hrot.ExCon/Panels/`, `Hrot.ExCon.Tests/`
- **Solution File:** `IOS-IG-SimHost.sln`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/IOS-BATCH-05-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/IOS-BATCH-05-QUESTIONS.md`

---

## Context

Phase P10 pushes the minimal viable product of the IOS Mock into a robust diagnostic tool capable of rendering deep entity introspection, networking health checks, and user-facing optimistic lock rejection dialogs.

---

## 🎯 Batch Objectives
- Implement an `InspectorPanel` to view raw ECS Descriptor values serialized to JSON or tabular strings.
- Implement a `DiagnosticsPanel` exposing internal `IRequestTransactionManager` timeout queues and overall FDP throughput.
- Wire a UI alert system (Conflict Detection UI) for `ERR_VERSION_CONFLICT` results when committing a Mission.
- Build Multi-IOS Integration Tests asserting proper optimistic locking across concurrent command shells.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task ...**

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## ✅ Tasks

### Task 1: Inspector Panel (IOS.10.1)
**Files:** `Hrot.ExCon/Panels/InspectorPanel.cs`
**Description:** Implement `InspectorPanel` which subscribes to the `IosLogic`'s currently selected entity.
**Requirements:** When an entity is selected, query the `IDerRepo` for all active descriptors. Map their field properties out through ImGui dynamically (using reflection or explicit mapping per descriptor). Expose raw data for diagnostic validation.

### Task 2: Diagnostics Panel (IOS.10.2)
**Files:** `Hrot.ExCon/Panels/DiagnosticsPanel.cs`
**Description:** A dashboard panel providing runtime diagnostics.
**Requirements:** Interface with `TransactionManager.GetPendingRequests()` to display a live queue of currently pending DDS interactions. Show the entity count from the Repo. Calculate metrics (e.g. DDS events per second if trackable). 

### Task 3: Conflict Detection UI (IOS.10.3)
**Files:** `Hrot.ExCon/Panels/MissionPanel.cs` (or create a dedicated alert overlay)
**Description:** Display a user-facing visual prompt when a mission commit is rejected due to version conflicts.
**Requirements:** Intercept the `Success=false` / `ErrorCode=7` trace from `MissionEditorService`. Halt the user with an ImGui Modal window showing the conflict message retrieved.

### Task 4: Multi-IOS Synchronization Tests (IOS.10.4)
**Files:** `Hrot.ExCon.Tests/MultiIosIntegrationTests.cs`
**Description:** Write integration tests simulating two `IosLogic` environments editing the same entity concurrently.
**Requirements:** Boot two distinct IOS instances hitting the same mocked `IDerRepo` network events. Issue competing `CommitMissionAsync` changes and verify that the optimistic locking appropriately cascades the failure back to the second client.

---

## 🧪 Testing Requirements

You must mock the UI abstraction similarly to preceding UI batches without triggering true `rlImGui` loops. For Task 4, construct a tightly controlled async interleaving test evaluating the conflict logic predictably.

---

## 📊 Report Requirements

Upon completion, generate `.dev-workstream/reports/IOS-BATCH-05-REPORT.md` answering the following context questions:

**Developer Insights**
**Q1:** The `InspectorPanel` involves mapping complex domain descriptor structs to user-readable ImGui formats. How did you resolve the dynamic introspection of the descriptors without allocating massive GC pauses?
**Q2:** How exactly did the Multi-IOS Integration scenarios handle internal networking? Did you discover flaws concerning message reflection or domain separation?
**Q3:** Did you intercept `IOS-DEBT-029` or `IOS-DEBT-030` safely while designing the Inspector? If so, did resolving the interface mismatch risk cascade through prior panels?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Task 1 through 4 are complete and mapped correctly into `IosMock.DrawUI`.
- [ ] Multi-IOS testing passes locally without parallelization drops.
- [ ] Report submitted addressing the questionnaire context.

---

## ⚠️ Common Pitfalls to Avoid
- **Reflection inside ImGui `Draw`:** Be extremely careful not to execute heavy `typeof(X).GetProperties()` during every ImGui frame. Cache the structural reflection map upon selection change.
- **Race conditions with `Dispose`:** When running multiple instances of `IosLogic`, confirm your teardowns inside the Multi-IOS Integration Tests don't falsely trigger `ObjectDisposedException` if tests cascade concurrently.

---

## 📚 Reference Materials
- **Task Tracker:** `docs/design/TASK-TRACKER.md`
- **Code Standards:** `.dev-workstream/guides/CODE-STANDARDS.md`
