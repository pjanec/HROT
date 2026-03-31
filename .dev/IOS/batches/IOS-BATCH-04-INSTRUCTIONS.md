# IOS-BATCH-04: IOS Integration Testing

**Batch Number:** IOS-BATCH-04  
**Tasks:** IOS.9.1, IOS.9.2, IOS.9.3, IOS.9.4  
**Phase:** IOS-P9 (Integration Testing)  
**Estimated Effort:** ~24 hours  
**Priority:** HIGH  
**Dependencies:** IOS-BATCH-03, Base Network Setup

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome back! BATCH-03 successfully implemented our primary `IosLogic` shell and wired the interaction logs for thread safety. In BATCH-04, you will finalize the structural debt logged over the last three batches and conduct the critical cross-system integration testing verifying that our IOS Mock communicates intelligently with SimHost and the IG Mock.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Tracker:** `docs/design/TASK-TRACKER.md` - Context for the overall goal.
3. **Task Details:** `docs/design/TASK-DETAILS-IOS.md` - Scroll down to the "Testing & Integration" section containing scenarios 1 through 5.
4. **Code Standards:** `.dev-workstream/guides/CODE-STANDARDS.md`
5. **Debt Tracker:** `.dev-workstream/IOS-DEBT-TRACKER.md` - You are responsible for clearing items IOS-DEBT-031, IOS-DEBT-032, and IOS-DEBT-033!

### Source Code Location
- **Primary Work Area:** `Hrot.ExCon.Tests/IntegrationTests.cs`, `Hrot.ExCon.Tests/WorkflowTests.cs`
- **Solution File:** `IOS-IG-SimHost.sln`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/IOS-BATCH-04-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/IOS-BATCH-04-QUESTIONS.md`

---

## Context

Integration tests construct realistic subsystem interactions (like the IG and SimHost) transmitting DDS topics backward and forwards, assessing the correct reaction states inside the `IosLogic` and the corresponding UI panels.

---

## 🎯 Batch Objectives
- Address three Phase 9 deferred technical debts.
- Provide end-to-end integration and workflow sanity checks testing multiple node contexts.
- Achieve full feature closure for the Hrot.ExCon component!

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

### Corrective Task 0 (IOS-DEBT-031)
**Description:** `MissionEditorService` lacks ingress path (DDS reader) for the `MissionControlAck` topic. You must add the respective `DdsReader<MissionControlAck>` subscription and wire it to fire `OnAckReceived`.

### Corrective Task 1 (IOS-DEBT-032)
**Description:** `MissionEditorService` lacks `IDisposable` implementation. Pending TaskCompletionSources are thereby left orphaned upon teardown. Implement `IDisposable` cleanly and resolve pending TCS gracefully natively on teardown.

### Corrective Task 2 (IOS-DEBT-033)
**Description:** `OrbatPanel.FindChildren` scans all entities per node—O(n²) time complexity. Refactor this to pre-cache a Dictionary lookup `CommanderId -> List<enfants>` upon `GetVisibleNodes` evaluation.

### Task 1: Integration Fixtures (IOS.9.1)
**Files:** `Hrot.ExCon.Tests/IntegrationTests.cs`
**Task Definition:** Implement Scenario 1: Standalone IOS validation. Simulate booting `IosMock` and `IosLogic`, ensuring panel views render normally without a network and gracefully block interactions without throwing null references. 

### Task 2: Subsystem Integrations (IOS.9.2 & IOS.9.3)
**Files:** `Hrot.ExCon.Tests/IntegrationTests.cs`
**Task Definition:** Implement IOS+IG and IOS+SimHost interaction pathways natively. You will need to build lightweight stub logic that acts as the IG (e.g. emitting `MapClickEvent`) and SimHost (e.g. emitting `CreateEntityAck`). Validate that the IOS detects the updates and processes responses accordingly.

### Task 3: Workflow Sanity Checks (IOS.9.4)
**Files:** `Hrot.ExCon.Tests/WorkflowTests.cs`
**Task Definition:** Implement Scenario 4 (Full Stack) and Scenario 5 (Conflict Detection). The full stack test will simulate a multi-step placement + mission modification trace. The conflict detection mode will assert that two instances patching the same mission will throw the predicted Optimistic Lock trace errors expected from `MissionEditorService`.

---

## 🧪 Testing Requirements

You are writing the Integration Tests! Verify that DDS payloads serialize consistently mapped to our shared structures. Do not utilize `Thread.Sleep()`. Mock discrete clocks (e.g. `ITimeProvider` introduced in BATCH-01) if necessary to accelerate asynchronous timeout behaviors cleanly. 

---

## 📊 Report Requirements

Upon completion, generate `.dev-workstream/reports/IOS-BATCH-04-REPORT.md` answering the following context questions:

**Developer Insights**
**Q1:** How did you isolate DDS Domain boundaries for these integration tests to assure they don't flake out in parallel execution with other tests?
**Q2:** Did you successfully drop the O(n^2) computational overhead for `OrbatPanel` traversing logic correctly? Verify metrics.
**Q3:** During Full Stack validation logic testing, did anything fail structurally given the constraints imposed by `MissionEditorService`?
**Q4:** Were there any unexpected complexities correctly resolving orphaned TCS items during `MissionEditorService.Dispose` phase tracking? 

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Corrective tasks 0 through 2 implemented and resolving the `IOS-DEBT` entries securely.
- [ ] Integration tests structurally prove interaction capacities without flaky Thread.Sleep implementations.
- [ ] Report submitted addressing the questionnaire context.

---

## ⚠️ Common Pitfalls to Avoid
- **Test parallelization leaks:** A common pattern in DDS Integration testing is leaking topic publications between parallel unsegregated Domains. Assign unique Domain IDs or disable xUnit parallelism for these specific classes!
- **DDS Teardown limits:** Ensure readers/writers gracefully disconnect internally inside the fixture cleanup boundaries to prevent out-of-memory socket leaks running multiple test suites.

---

## 📚 Reference Materials
- **Task Tracker:** `docs/design/TASK-TRACKER.md`
- **Task Definitions:** `docs/design/TASK-DETAILS-IOS.md` (Testing & Integration Section)
- **Code Standards:** `.dev-workstream/guides/CODE-STANDARDS.md`
